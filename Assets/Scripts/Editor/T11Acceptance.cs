using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using StarEco.Combat;

namespace StarEco.EditorTools
{
    /// <summary>
    /// T11 战斗验收 harness（M3 历史遗留，已完成使命）：
    /// - 按时刻表注入攻击输入（OS 键盘注入被环境阻断，故顶替「按键→AttackPressedThisFrame」这一跳：
    ///   注入前禁用 PlayerCombat.Update，防止每帧用真实 Input 覆盖注入值）
    /// - 三段动画衔接、连段窗口缓冲、Animation Event 开窗、命中扣血、第三段击退 全部走真实游戏链路
    /// - 每条事件实时写入 .workbuddy/t11_report.txt
    ///
    /// M7 起停用自动运行：原 [InitializeOnLoad] 会在每次 Play 时无条件禁用 PlayerCombat.Update
    /// 并挪动靶子，干扰 M4 之后的正常试玩。需要复跑验收时，临时恢复该特性或在 Unity 菜单
    /// 里手动调用 Tick 即可（逻辑保持原样未删）。
    /// </summary>
    public static class T11Acceptance
    {
        // 三连击时刻：第1击进段1；第2击在段1的 0.15s 连段窗口内；第3击在段2窗口内
        // 段1长 0.4167s、段2长 0.3333s（SegmentEndTimes）
        private static readonly double[] DriveAt = { 0.05, 0.15, 0.55 };

        private static readonly StringBuilder Sb = new StringBuilder();
        private static readonly string ReportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".workbuddy", "t11_report.txt"));

        private static bool _wasPlaying;
        private static double _t0;
        private static int _seg;
        private static int _hp0 = -1;
        private static bool _win;
        private static bool _knockLogged;
        private static int _driveIdx;
        private static FieldInfo _hpField;
        private static string _lastState = "";
        private static int _traceFrames;

        static T11Acceptance()
        {
            EditorApplication.update += Tick;
        }

        private static void Log(string msg)
        {
            Sb.Append("[T11] ").Append(msg).AppendLine();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, Sb.ToString());
            }
            catch { }
            Debug.Log("[T11] " + msg);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (_wasPlaying)
                {
                    Log("=== 退出 Play，本轮结束 ===");
                }
                _wasPlaying = false;
                return;
            }

            if (!_wasPlaying)
            {
                _wasPlaying = true;
                Sb.Length = 0;
                _t0 = EditorApplication.timeSinceStartup;
                _seg = 0;
                _hp0 = -1;
                _win = false;
                _knockLogged = false;
                _driveIdx = 0;
                Log("=== 进入 Play，观测开始 ===");

                // 禁用 PlayerCombat.Update：停止其用真实 Input 每帧覆盖 AttackPressedThisFrame
                var player0 = GameObject.Find("Player");
                var combat0 = player0 != null ? player0.GetComponent<PlayerCombat>() : null;
                if (combat0 != null && combat0.enabled)
                {
                    combat0.enabled = false;
                    Log("PlayerCombat.Update 已禁用（输入由 harness 按时刻表注入）");
                }

                // 靶子就位：玩家右侧 1.3u（hitbox offset 0.55 / 宽 1.7 可覆盖）
                var target0 = UnityEngine.Object.FindObjectOfType<TestTarget>();
                if (player0 != null && target0 != null)
                {
                    var pp = player0.transform.position;
                    var tp = target0.transform.position;
                    if (Mathf.Abs(tp.x - pp.x) > 1.6f || Mathf.Abs(tp.y - pp.y) > 1.2f)
                    {
                        target0.transform.position = new Vector3(pp.x + 1.3f, pp.y, pp.z);
                        Log("靶子已挪到 " + target0.transform.position);
                    }
                    var hpF = typeof(TestTarget).GetField("_hp", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (hpF != null)
                    {
                        _hp0 = (int)hpF.GetValue(target0);
                        Log("靶子初始 HP=" + _hp0);
                    }
                }
            }

            double el = EditorApplication.timeSinceStartup - _t0;
            string tag = "t=" + el.ToString("F2");

            // 真实按键（若 OS 注入能进来也会被记录）
            if (Input.GetKeyDown(KeyCode.J))
            {
                Log("Unity 收到真实 J 键 " + tag);
            }

            var player = GameObject.Find("Player");
            var target = UnityEngine.Object.FindObjectOfType<TestTarget>();
            if (player == null || target == null)
            {
                return;
            }
            var combat = player.GetComponent<PlayerCombat>();

            // ===== 输入注入（时刻表）=====
            if (_driveIdx < DriveAt.Length && el >= DriveAt[_driveIdx])
            {
                bool locked = false;
                if (combat != null)
                {
                    var t = typeof(PlayerCombat);
                    var pcT = typeof(StarEco.Player.PlayerController);
                    var pc = player.GetComponent<StarEco.Player.PlayerController>();
                    var lockPr = pcT.GetProperty("InputLocked", BindingFlags.Public | BindingFlags.Instance);
                    var deadPr = pcT.GetProperty("IsDead", BindingFlags.Public | BindingFlags.Instance);
                    locked = (lockPr != null && (bool)lockPr.GetValue(pc, null)) || (deadPr != null && (bool)deadPr.GetValue(pc, null));
                }
                if (!locked && combat != null)
                {
                    var pr = typeof(PlayerCombat).GetProperty("AttackPressedThisFrame");
                    var setter = pr != null ? pr.GetSetMethod(true) : null;
                    if (setter != null)
                    {
                        setter.Invoke(combat, new object[] { true });
                        _traceFrames = 10;
                        Log("注入攻击输入 #" + (_driveIdx + 1) + " " + tag);
                        // 注入时输入存活诊断
                        bool alive = (bool)pr.GetValue(combat, null);
                        var pcDiag = player.GetComponent<StarEco.Player.PlayerController>();
                        Log("  诊断: AttackPressed存活=" + alive + " InputLocked=" + pcDiag.InputLocked);
                    }
                }
                else
                {
                    Log("跳过注入 #" + (_driveIdx + 1) + "（locked=" + locked + "）" + tag);
                }
                _driveIdx++;
            }

            var animator = player.GetComponent<Animator>();
            var hitbox = player.GetComponent<AttackHitbox>();
            if (animator == null || hitbox == null)
            {
                return;
            }

            // ===== 状态机轨迹（变化才记录）=====
            var pcTrace = player.GetComponent<StarEco.Player.PlayerController>();
            var fsmF = typeof(StarEco.Player.PlayerController).GetField("_machine", BindingFlags.NonPublic | BindingFlags.Instance);
            object fsm = fsmF != null ? fsmF.GetValue(pcTrace) : null;
            string stName = fsm == null ? "_machine=null" : (fsm.GetType().GetProperty("CurrentType")?.GetValue(fsm, null)?.ToString() ?? "?");
            if (stName != _lastState)
            {
                Log("状态机 " + _lastState + " -> " + stName + " " + tag);
                _lastState = stName;
            }
            // 注入后值存活追踪（注入后 10 帧内每帧记录）
            if (_traceFrames > 0 && combat != null)
            {
                var prT = typeof(PlayerCombat).GetProperty("AttackPressedThisFrame");
                Log("  存活 t#" + _traceFrames + " AttackPressed=" + prT.GetValue(combat, null));
                _traceFrames--;
            }

            // 三段动画衔接
            var info = animator.GetCurrentAnimatorStateInfo(0);
            int cur = 0;
            if (info.IsName("Attack")) cur = 1;
            else if (info.IsName("Attack02")) cur = 2;
            else if (info.IsName("Attack03")) cur = 3;
            else if (info.IsName("AttackAir")) cur = 9;
            if (cur >= 1 && cur <= 3 && cur != _seg)
            {
                _seg = cur;
                Log("进入第 " + _seg + " 段动画 " + tag);
            }

            // 开窗/关窗
            var hitCol = typeof(AttackHitbox).GetField("_hitbox", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hitbox) as Collider2D;
            bool open = hitCol != null && hitCol.enabled;
            if (open != _win)
            {
                _win = open;
                Log((open ? "开窗 HitOn " : "关窗 HitOff ") + tag + " 段=" + _seg);
            }

            // 命中扣血
            _hpField = _hpField ?? typeof(TestTarget).GetField("_hp", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_hpField != null)
            {
                int hp = (int)_hpField.GetValue(target);
                if (hp != _hp0)
                {
                    _hp0 = hp;
                    Log("命中 HP=" + hp + " 段=" + _seg + " " + tag);
                }
            }

            // 第三段击退
            if (!_knockLogged && _seg == 3)
            {
                var rb = target.GetComponent<Rigidbody2D>();
                if (rb != null && Mathf.Abs(rb.velocity.x) > 0.5f)
                {
                    _knockLogged = true;
                    Log("第三段击退 velocity.x=" + rb.velocity.x.ToString("F2") + " " + tag);
                }
            }
        }
    }
}
