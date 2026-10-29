// ============================================================================
// 【模板】CSV 表读取：把策划表在运行时读成「列名 -> 值」的行集合。
// 复制到 <工程根>/Assets/Scripts/Core/ 后由 Unity 编译。
// 纯 System 依赖，不引用 UnityEngine，可脱离 Unity 做单元测试。
//
// 用法：
//   TextAsset asset = ...;                       // 表放 Assets/Resources? 不要；
//   CsvTable table = CsvTable.Parse(asset.text);  // 用 Addressables/YooAsset 加载 TextAsset
//   float hp = table.GetFloat(row, "hp", 0f);
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace GameFramework
{
    /// <summary>
    /// 极简 CSV 解析器，支持 RFC 4180 的引号、双引号转义、CRLF 与 UTF-8 BOM。
    /// 只做「读」：数值表的权威副本在 Docs/Balance/*.csv，运行时用 ScriptableObject 或本表缓存。
    /// </summary>
    public sealed class CsvTable
    {
        private readonly List<string> _headers = new List<string>();
        private readonly List<string[]> _rows = new List<string[]>();

        public int RowCount { get { return _rows.Count; } }
        public int ColumnCount { get { return _headers.Count; } }
        public IList<string> Headers { get { return _headers; } }

        public static CsvTable Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");

            CsvTable table = new CsvTable();
            List<string> fields = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            bool anyContent = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // "" 表示一个字面量引号；单个 " 结束引号段
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    anyContent = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                    anyContent = true;
                }
                else if (c == '\r')
                {
                    // 交给 '\n' 处理，兼容 CRLF 与单独 CR
                }
                else if (c == '\n')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                    table.AddRow(fields);
                    fields.Clear();
                    anyContent = false;
                }
                else
                {
                    field.Append(c);
                    anyContent = true;
                }
            }

            if (anyContent || field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                table.AddRow(fields);
            }

            return table;
        }

        private void AddRow(List<string> fields)
        {
            if (_headers.Count == 0)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    string header = fields[i].Trim();
                    if (i == 0) header = header.TrimStart('\uFEFF'); // UTF-8 BOM
                    _headers.Add(header);
                }
                return;
            }

            bool blank = true;
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Trim().Length > 0) { blank = false; break; }
            }
            if (blank) return;

            string[] row = new string[_headers.Count];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = i < fields.Count ? fields[i].Trim() : string.Empty;
            }
            _rows.Add(row);
        }

        public int IndexOf(string column)
        {
            for (int i = 0; i < _headers.Count; i++)
            {
                if (string.Equals(_headers[i], column, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        public string Get(int row, string column)
        {
            int columnIndex = IndexOf(column);
            if (columnIndex < 0 || row < 0 || row >= _rows.Count) return string.Empty;
            return _rows[row][columnIndex];
        }

        public bool TryGetFloat(int row, string column, out float value)
        {
            value = 0f;
            string text = Get(row, column).Trim();
            if (text.Length == 0) return false;
            return float.TryParse(text, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        public float GetFloat(int row, string column, float fallback)
        {
            float value;
            return TryGetFloat(row, column, out value) ? value : fallback;
        }
    }
}
