# -*- coding: utf-8 -*-
"""
数值表体检 + 反算工具（只依赖标准库）。

用法：
    python balance_check.py check [表目录]            # 默认 Docs/Balance
    python balance_check.py check Docs/Balance --strict  # 警告也判失败（CI 用）
    python balance_check.py init [表目录]              # 从本工具包 tables/ 复制示例表
    python balance_check.py ttk --hp 500 --dps 120 --def 40 --formula mul
    python balance_check.py curve --base 100 --step 1.12 --levels 30 --mode exp

检查内容（详见 BalanceHandbook.md）：
    combat_units.csv        ID 重复/空、负值、int32 溢出、float 精度、TTK 是否落在品类区间
    progression_curve.csv   等级单调性、每级倍率是否落在合理区间、exp 是否中途归零
    economy_flows.csv       产出/消耗覆盖率、单价可负担时长、负数

退出码：0 = 无 error（--strict 时无 warn）；1 = 有 error（或 --strict 下有 warn）。
"""

import argparse
import csv
import io
import os
import shutil
import sys

# ============================================================
# 阈值区（按项目口味调；改这里就是改验收标准）
# ============================================================

ARMOR_K = 100.0          # 乘法减伤的 K：减伤率 = def / (def + K)
DEFAULT_FORMULA = "mul"  # sub = 减法 / mul = 乘法 / hybrid = 混合
MIN_DAMAGE = 1.0         # 减法式的最低伤害，防止 0 伤害死锁

# TTK（击杀耗时）经验区间，单位秒。没有把握的 tier 直接不写，就不检查。
# 【本项目校准 2026-10-22】BiShe 是 2D 横版、1 伤害/段、敌人 3~5 HP，
# 理论 TTK 天然比割草类大，不能套用模板默认的 0.1~0.8s。
# 依据与反算过程见 docs/Balance/README.md；改数值后重新跑 check。
TTK_BANDS = {
    "trash": (1.0, 2.5),    # 杂兵：Enemy01~03 = 1.13 / 1.50 / 1.88s
    "elite": (2.5, 6.0),    # 精英：Tower01 = 3.0s
    "boss": (8.0, 30.0),    # Boss：Boss01 木桩 11.25s（实战含阶段/走位更长）
}

# 成长曲线：每级倍率合理区间（1.0 = 不涨，1.5 = 每 3 级翻倍，再高必然失控）
GROWTH_RATIO_MIN = 1.00
GROWTH_RATIO_MAX = 1.50
GROWTH_COLUMNS = ("hp", "atk", "def")

# 经济：消耗 / 产出 的覆盖率。长期 < 0.8 通胀，> 1.2 通缩。
SINK_COVERAGE_MIN = 0.8
SINK_COVERAGE_MAX = 1.2

# 可负担时长（小时）：目标商品价格 / 每小时总产出
AFFORD_HOURS_MIN = 0.02   # 低于 1 分钟 = 价格太便宜，没有决策感
AFFORD_HOURS_MAX = 40.0   # 高于 40 小时 = 单件太贵，前期挫败

INT32_MAX = 2147483647
INT64_MAX = 9223372036854775807
FLOAT_EXACT_INT = 16777216  # 2^24：超过它，float 无法精确表示每一个整数

LEVEL_ERROR = "ERROR"
LEVEL_WARN = "WARN"
LEVEL_INFO = "INFO"


def _fix_stdout():
    """统一 UTF-8 输出：表名/列名含中文，CI 与终端都按 UTF-8 读。"""
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


class Issue(object):
    def __init__(self, level, table, row, message):
        self.level = level
        self.table = table
        self.row = row
        self.message = message

    def __str__(self):
        loc = self.table
        if self.row:
            loc += " 第 %s 行" % self.row
        return "[%s] %s: %s" % (self.level, loc, self.message)


def num(text, default=None):
    """空值返回 default；否则转 float，转不动抛 ValueError。"""
    if text is None:
        return default
    text = text.strip()
    if text == "":
        return default
    return float(text)


def read_table(path, required, issues):
    """读 CSV，返回 [dict]。缺列直接记 ERROR 并返回空表。"""
    name = os.path.basename(path)
    if not os.path.exists(path):
        return None
    rows = []
    with io.open(path, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        headers = [h.strip() for h in (reader.fieldnames or [])]
        missing = [c for c in required if c not in headers]
        if missing:
            issues.append(Issue(LEVEL_ERROR, name, 1, "缺少必需列：%s" % ", ".join(missing)))
            return []
        for index, raw in enumerate(reader, start=2):
            row = {}
            for key, value in raw.items():
                if key is None:
                    continue
                row[key.strip()] = (value or "").strip()
            if not any(row.values()):
                continue  # 跳过空行
            row["_line"] = index
            rows.append(row)
    return rows


def parse_float_field(row, column, table, issues, required=False, default=None):
    raw = row.get(column, "")
    try:
        value = num(raw, None)
    except ValueError:
        issues.append(Issue(LEVEL_ERROR, table, row["_line"], "%s 不是数字：%r" % (column, raw)))
        return default
    if value is None:
        if required:
            issues.append(Issue(LEVEL_ERROR, table, row["_line"], "%s 为空" % column))
        return default
    return value


def fmt_num(value):
    """人类可读：整数不带小数点，小数保留 3 位，避免 3e+09 这种科学计数法。"""
    if value is None:
        return "-"
    if abs(value - round(value)) < 1e-9:
        return "{:,}".format(int(round(value)))
    return "{:,.3f}".format(value)


def check_magnitude(table, line, field, value, issues):
    """技术硬约束：Unity int32 / float 精度。"""
    if value is None:
        return
    abs_value = abs(value)
    if abs_value > INT64_MAX:
        issues.append(Issue(LEVEL_ERROR, table, line, "%s 超出 int64 范围" % field))
    elif abs_value > INT32_MAX:
        issues.append(Issue(LEVEL_WARN, table, line,
                            "%s=%s 超出 int32（%s），运行时必须用 long"
                            % (field, fmt_num(value), fmt_num(INT32_MAX))))
    elif abs_value > FLOAT_EXACT_INT:
        issues.append(Issue(LEVEL_WARN, table, line,
                            "%s=%s 超出 float 精确整数范围（%s），结算改用 long/int"
                            % (field, fmt_num(value), fmt_num(FLOAT_EXACT_INT))))


def mitigation(defense, armor_k):
    if armor_k <= 0:
        return 0.0
    return defense / (defense + armor_k)


def mitigated_dps(dps, defense, formula, armor_k, min_damage=MIN_DAMAGE):
    """把「玩家 DPS」按目标防御换算成有效 DPS。"""
    if formula == "sub":
        # 减法式里 dps 视为「每秒原始伤害」，直接扣防御后的比例无法逐帧还原，
        # 这里用等效假设：单次伤害 = dps / 1s，按减法后取比例。
        after = max(min_damage, dps - defense)
        return after
    m = mitigation(defense, armor_k)
    if formula == "hybrid":
        # 混合式：先扣一半防御，再吃乘法减伤
        after = max(min_damage, dps - defense * 0.5)
        return after * (1.0 - m)
    return dps * (1.0 - m)


# ============================================================
# 表级检查
# ============================================================

def check_combat(rows, issues, formula):
    table = "combat_units.csv"
    seen = {}
    for row in rows:
        line = row["_line"]
        unit_id = row.get("id", "")
        if not unit_id:
            issues.append(Issue(LEVEL_ERROR, table, line, "id 为空"))
        elif unit_id in seen:
            issues.append(Issue(LEVEL_ERROR, table, line,
                                "id 重复：%s（首次出现在第 %d 行）" % (unit_id, seen[unit_id])))
        else:
            seen[unit_id] = line

        hp = parse_float_field(row, "hp", table, issues, required=True)
        defense = parse_float_field(row, "def", table, issues, default=0.0)
        dps_ref = parse_float_field(row, "dps_ref", table, issues, default=0.0)
        crit_rate = parse_float_field(row, "crit_rate", table, issues, default=0.0)
        crit_mult = parse_float_field(row, "crit_mult", table, issues, default=1.0)

        for field, value in (("hp", hp), ("def", defense), ("dps_ref", dps_ref)):
            if value is not None and value < 0:
                issues.append(Issue(LEVEL_ERROR, table, line, "%s 不能为负：%g" % (field, value)))
            check_magnitude(table, line, field, value, issues)
        if hp is not None and hp <= 0:
            issues.append(Issue(LEVEL_ERROR, table, line, "hp 必须 > 0"))
        if row.get("dps_ref", "").strip() == "":
            issues.append(Issue(LEVEL_WARN, table, line,
                                "dps_ref 为空：无法核算 TTK（填玩家在该阶段的参考 DPS）"))
        if crit_rate is not None and not (0.0 <= crit_rate <= 1.0):
            issues.append(Issue(LEVEL_ERROR, table, line,
                                "crit_rate 应在 0~1 之间（当前 %g）" % crit_rate))
        if crit_mult is not None and crit_mult < 1.0:
            issues.append(Issue(LEVEL_WARN, table, line,
                                "crit_mult < 1（%g）：暴击比不暴击还低" % crit_mult))

        if hp and hp > 0 and dps_ref and dps_ref > 0:
            eff = mitigated_dps(dps_ref, defense or 0.0, formula, ARMOR_K)
            if eff <= 0:
                issues.append(Issue(LEVEL_ERROR, table, line, "有效 DPS 为 0，永远打不死"))
                continue
            ttk = hp / eff
            tier = row.get("tier", "").strip().lower()
            band = TTK_BANDS.get(tier)
            issues.append(Issue(LEVEL_INFO, table, line,
                                "%s TTK = %.2fs（有效 DPS %.1f）" % (unit_id or "?", ttk, eff)))
            if band and not (band[0] <= ttk <= band[1]):
                issues.append(Issue(LEVEL_WARN, table, line,
                                    "%s TTK %.2fs 不在 %s 区间 %.1f~%.1fs"
                                    % (unit_id, ttk, tier, band[0], band[1])))


def check_progression(rows, issues):
    table = "progression_curve.csv"
    levels = []
    for row in rows:
        line = row["_line"]
        level = parse_float_field(row, "level", table, issues, required=True)
        if level is None:
            continue
        if level != int(level):
            issues.append(Issue(LEVEL_WARN, table, line, "level 不是整数：%g" % level))
        levels.append((int(level), row, line))

    levels.sort(key=lambda item: item[0])
    if not levels:
        return

    seen_levels = {}
    for level, row, line in levels:
        if level in seen_levels:
            issues.append(Issue(LEVEL_ERROR, table, line,
                                "level 重复：%d（首次出现在第 %d 行）" % (level, seen_levels[level])))
        else:
            seen_levels[level] = line

    last_level = levels[-1][0]
    for column in GROWTH_COLUMNS + ("exp_to_next", "gold_reward"):
        values = []
        for level, row, line in levels:
            value = parse_float_field(row, column, table, issues, default=None)
            if value is not None:
                check_magnitude(table, line, column, value, issues)
                if value < 0:
                    issues.append(Issue(LEVEL_ERROR, table, line,
                                        "%s 不能为负：%g" % (column, value)))
                values.append((level, value, line))
        if len(values) < 2:
            continue

        for position in range(1, len(values)):
            prev_level, prev_value, _ = values[position - 1]
            level, value, line = values[position]
            if column == "exp_to_next" and value == 0 and level == last_level:
                continue  # 满级 exp 归零是正常写法
            if value < prev_value:
                issues.append(Issue(LEVEL_WARN, table, line,
                                    "%s 从 %g（Lv%d）降到 %g，曲线不单调"
                                    % (column, prev_value, prev_level, value)))
            elif prev_value > 0:
                ratio = value / prev_value
                if column in GROWTH_COLUMNS and ratio > GROWTH_RATIO_MAX:
                    issues.append(Issue(LEVEL_WARN, table, line,
                                        "%s 每级 ×%.2f 超过上限 %.2f（Lv%d），后期会爆炸"
                                        % (column, ratio, GROWTH_RATIO_MAX, level)))
                if column == "exp_to_next" and ratio < GROWTH_RATIO_MIN:
                    issues.append(Issue(LEVEL_WARN, table, line,
                                        "exp_to_next 每级 ×%.2f 未增长（Lv%d）"
                                        % (ratio, level)))


def check_economy(rows, issues):
    table = "economy_flows.csv"
    seen = {}
    per_currency = {}
    for row in rows:
        line = row["_line"]
        flow_id = row.get("id", "")
        if not flow_id:
            issues.append(Issue(LEVEL_ERROR, table, line, "id 为空"))
        elif flow_id in seen:
            issues.append(Issue(LEVEL_ERROR, table, line, "id 重复：%s" % flow_id))
        else:
            seen[flow_id] = line

        currency = row.get("currency", "").strip()
        direction = row.get("direction", "").strip().lower()
        if not currency:
            issues.append(Issue(LEVEL_ERROR, table, line, "currency 为空"))
            continue
        if direction not in ("faucet", "sink"):
            issues.append(Issue(LEVEL_ERROR, table, line,
                                "direction 只能是 faucet/sink（当前 %r）" % direction))
            continue

        amount = parse_float_field(row, "amount", table, issues, required=True)
        per_hour = parse_float_field(row, "per_hour", table, issues, default=1.0)
        price = parse_float_field(row, "price_anchor", table, issues, default=None)
        if amount is None:
            continue
        if amount < 0 or (per_hour is not None and per_hour < 0):
            issues.append(Issue(LEVEL_ERROR, table, line, "amount / per_hour 不能为负"))
        check_magnitude(table, line, "amount", amount, issues)
        if price is not None:
            check_magnitude(table, line, "price_anchor", price, issues)
            if price <= 0:
                issues.append(Issue(LEVEL_WARN, table, line,
                                    "price_anchor <= 0，忽略可负担时长"))

        bucket = per_currency.setdefault(currency, {"faucet": 0.0, "sink": 0.0, "price": []})
        bucket[direction] += amount * (per_hour if per_hour is not None else 1.0)
        if price is not None and price > 0 and direction == "sink":
            bucket["price"].append((price, line))

    for currency, bucket in sorted(per_currency.items()):
        faucet = bucket["faucet"]
        sink = bucket["sink"]
        if faucet <= 0:
            issues.append(Issue(LEVEL_WARN, table, 0,
                                "货币 %s 没有产出（faucet=0），但消耗为 %.1f/h" % (currency, sink)))
            continue
        coverage = sink / faucet
        issues.append(Issue(LEVEL_INFO, table, 0,
                            "货币 %s 产出 %.1f/h，消耗 %.1f/h，覆盖率 %.2f"
                            % (currency, faucet, sink, coverage)))
        if coverage < SINK_COVERAGE_MIN:
            issues.append(Issue(LEVEL_WARN, table, 0,
                                "货币 %s 覆盖率 %.2f < %.2f：钱越攒越多，会通胀"
                                % (currency, coverage, SINK_COVERAGE_MIN)))
        elif coverage > SINK_COVERAGE_MAX:
            issues.append(Issue(LEVEL_WARN, table, 0,
                                "货币 %s 覆盖率 %.2f > %.2f：消耗大于产出，玩家会破产"
                                % (currency, coverage, SINK_COVERAGE_MAX)))
        for price, line in bucket["price"]:
            hours = price / faucet
            if hours < AFFORD_HOURS_MIN:
                issues.append(Issue(LEVEL_WARN, table, line,
                                    "单价 %g 只需 %.2f 小时（< %.2fh），几乎无决策感"
                                    % (price, hours, AFFORD_HOURS_MIN)))
            elif hours > AFFORD_HOURS_MAX:
                issues.append(Issue(LEVEL_WARN, table, line,
                                    "单价 %g 需要 %.1f 小时（> %.0fh），前期太贵"
                                    % (price, hours, AFFORD_HOURS_MAX)))


# ============================================================
# 子命令
# ============================================================

def cmd_check(args):
    folder = args.folder
    issues = []
    if not os.path.isdir(folder):
        print("表目录不存在：%s" % folder)
        return 1

    print("== 数值表体检 ==")
    print("表目录：%s" % os.path.abspath(folder))

    found = False
    combat = read_table(os.path.join(folder, "combat_units.csv"),
                        ("id", "hp", "def", "dps_ref", "tier"), issues)
    if combat is not None:
        found = True
        print("combat_units.csv：%d 行" % len(combat))
        check_combat(combat, issues, args.formula)

    progression = read_table(os.path.join(folder, "progression_curve.csv"),
                             ("level",), issues)
    if progression is not None:
        found = True
        print("progression_curve.csv：%d 行" % len(progression))
        check_progression(progression, issues)

    economy = read_table(os.path.join(folder, "economy_flows.csv"),
                         ("id", "currency", "direction", "amount"), issues)
    if economy is not None:
        found = True
        print("economy_flows.csv：%d 行" % len(economy))
        check_economy(economy, issues)

    if not found:
        print("三张表一个都没找到。先跑：python balance_check.py init %s" % folder)
        return 1

    print("")
    for issue in issues:
        print("  " + str(issue))

    errors = sum(1 for i in issues if i.level == LEVEL_ERROR)
    warns = sum(1 for i in issues if i.level == LEVEL_WARN)
    infos = sum(1 for i in issues if i.level == LEVEL_INFO)
    print("")
    print("汇总：%d error / %d warn / %d info" % (errors, warns, infos))
    if errors:
        print("结论：不通过（有 error）")
        return 1
    if args.strict and warns:
        print("结论：不通过（--strict，warn 也算失败）")
        return 1
    print("结论：通过")
    return 0


def cmd_init(args):
    source = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tables")
    if not os.path.isdir(source):
        print("找不到模板表目录：%s" % source)
        return 1
    target = args.folder
    if not os.path.isdir(target):
        os.makedirs(target)
    copied = []
    for name in sorted(os.listdir(source)):
        if not name.lower().endswith(".csv"):
            continue
        dst = os.path.join(target, name)
        if os.path.exists(dst) and not args.force:
            print("已存在，跳过：%s（要覆盖加 --force）" % dst)
            continue
        shutil.copyfile(os.path.join(source, name), dst)
        copied.append(dst)
    for path in copied:
        print("已生成：%s" % path)
    if not copied:
        print("没有复制任何文件。")
    return 0


def cmd_ttk(args):
    eff = mitigated_dps(args.dps, args.defense, args.formula, args.armor_k)
    if eff <= 0:
        print("有效 DPS 为 0：这组数值永远打不死目标。")
        return 1
    ttk = args.hp / eff
    ehp = args.hp / (1.0 - mitigation(args.defense, args.armor_k)) if args.armor_k > 0 else args.hp
    print("目标 HP      : %.1f" % args.hp)
    print("目标防御     : %.1f（公式 %s，K=%.1f）" % (args.defense, args.formula, args.armor_k))
    print("玩家 DPS     : %.1f" % args.dps)
    print("减伤率       : %.1f%%" % (mitigation(args.defense, args.armor_k) * 100.0))
    print("有效 DPS     : %.1f" % eff)
    print("等效血量 EHP : %.1f" % ehp)
    print("击杀耗时 TTK : %.2f 秒" % ttk)
    if args.tier:
        band = TTK_BANDS.get(args.tier)
        if band:
            ok = band[0] <= ttk <= band[1]
            print("品类区间     : %s %.1f~%.1fs -> %s"
                  % (args.tier, band[0], band[1], "通过" if ok else "越界"))
            return 0 if ok else 1
    return 0


def cmd_curve(args):
    if args.levels <= 0:
        print("levels 必须 > 0")
        return 1
    if args.mode == "exp" and args.step <= 0:
        print("指数模式的 step 必须 > 0")
        return 1

    rows = []
    value = float(args.base)
    for index in range(args.levels):
        level = args.start + index
        if index > 0:
            if args.mode == "exp":
                value *= args.step
            else:
                value += args.step
        if args.cap is not None:
            value = min(value, args.cap)
        value = round(value, args.decimals)
        previous = rows[-1]["value"] if rows else None
        delta = (value / previous - 1.0) if previous else 0.0
        rows.append({"level": level, "value": value, "delta": delta})

    if args.csv:
        print("level,value,delta")
        for row in rows:
            print("%d,%s,%.4f" % (row["level"], row["value"], row["delta"]))
    else:
        print("| 等级 | 数值 | 较上一级 |")
        print("|---|---|---|")
        for row in rows:
            print("| %d | %s | %+.1f%% |"
                  % (row["level"], row["value"], row["delta"] * 100.0))
    return 0


def build_parser():
    parser = argparse.ArgumentParser(description="数值表体检与反算工具")
    sub = parser.add_subparsers(dest="command")

    p_check = sub.add_parser("check", help="检查 CSV 数值表")
    p_check.add_argument("folder", nargs="?", default="Docs/Balance")
    p_check.add_argument("--formula", default=DEFAULT_FORMULA, choices=("sub", "mul", "hybrid"))
    p_check.add_argument("--strict", action="store_true", help="warn 也判失败")
    p_check.set_defaults(func=cmd_check)

    p_init = sub.add_parser("init", help="生成示例表")
    p_init.add_argument("folder", nargs="?", default="Docs/Balance")
    p_init.add_argument("--force", action="store_true", help="覆盖已存在的文件")
    p_init.set_defaults(func=cmd_init)

    p_ttk = sub.add_parser("ttk", help="反算击杀耗时")
    p_ttk.add_argument("--hp", type=float, required=True)
    p_ttk.add_argument("--dps", type=float, required=True)
    p_ttk.add_argument("--def", dest="defense", type=float, default=0.0)
    p_ttk.add_argument("--formula", default=DEFAULT_FORMULA, choices=("sub", "mul", "hybrid"))
    p_ttk.add_argument("--armor-k", dest="armor_k", type=float, default=ARMOR_K)
    p_ttk.add_argument("--tier", default="", help="trash / elite / boss")
    p_ttk.set_defaults(func=cmd_ttk)

    p_curve = sub.add_parser("curve", help="生成成长曲线")
    p_curve.add_argument("--base", type=float, required=True)
    p_curve.add_argument("--step", type=float, required=True, help="线性=每级增量；指数=每级倍率")
    p_curve.add_argument("--levels", type=int, default=20)
    p_curve.add_argument("--start", type=int, default=1)
    p_curve.add_argument("--mode", default="exp", choices=("linear", "exp"))
    p_curve.add_argument("--cap", type=float, default=None)
    p_curve.add_argument("--decimals", type=int, default=1)
    p_curve.add_argument("--csv", action="store_true")
    p_curve.set_defaults(func=cmd_curve)

    return parser


def main():
    _fix_stdout()
    parser = build_parser()
    args = parser.parse_args()
    if not getattr(args, "command", None):
        parser.print_help()
        return 1
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
