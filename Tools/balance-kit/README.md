# balance-kit — 数值设计

把「游戏数值」拆成三层：技术硬约束（通用）、方法论与验收标准（通用）、具体数值（每个项目自己推导）。
本目录给前两层，并提供三张表模板与一个可进 CI 的体检脚本。

位置：`<工程根>/Tools/balance-kit/`（与 `Tools/touch-kit/`、`Tools/game-database/` 同级；
**不纳入工程 git**，由本工作区（可复用资源库）统一版本化）

## 目录

| 文件 | 作用 | 复制到 |
|---|---|---|
| `BalanceHandbook.md` | **主手册**：三层分工、TTK/EHP/DPS、公式三族、成长曲线、经济、随机、可读性、调参流程 | — |
| `balance_check.py` | 表体检 + TTK 反算 + 曲线生成（标准库，退出码 0/1） | — |
| `tables/*.csv` | 三张示例表：战斗单位 / 成长曲线 / 经济收支 | 复制成 `Docs/Balance/` |
| `templates/CsvTable.cs` | CSV 解析（引号、逗号、BOM） | `Assets/Scripts/Core/` |
| `templates/DamageMath.cs` | 减法 / 乘法 / 混合三族公式 + DPS/EHP/TTK | `Assets/Scripts/Core/` |
| `templates/NumericSafety.cs` | 货币溢出保护 + 1.2K/3.4M 紧凑格式 | `Assets/Scripts/Core/` |

## 快速开始

```bash
# 1) 生成项目数值表
python Tools/balance-kit/balance_check.py init Docs/Balance

# 2) 改表后体检
python Tools/balance-kit/balance_check.py check Docs/Balance

# 3) 反算某个敌人的 TTK
python Tools/balance-kit/balance_check.py ttk --hp 500 --dps 120 --def 40 --tier elite
```

## 核心结论

1. **没有通用数值表，但有通用数值工程**：具体数字必须按项目推导，公式/验收标准/精度约束可以复用。
2. **先定体验再反推数字**：TTK → DPS → HP，不要反过来凭手感填表。
3. **长线项目用乘法减伤**：`def/(def+K)` 永远小于 100%，天然抗膨胀。
4. **货币一律 `long`**，且必须有 sink；只有产出的经济一定通胀。
5. **关键随机必须保底**：1% 概率要 299 次才有 95% 把握触发，纯随机就是事故。
