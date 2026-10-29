# Unity 2D 横版动作游戏（环保主题）· 美术资源说明

画风：高清卡通描边（非马赛克像素），角色统一朝右、透明底、脚底基线对齐；特效为黑底转透明（Additive 友好）。
主角：环保小英雄（橙针织帽 / 绿马甲 / 蓝净水背包 / 银灰机械夹）。

## 目录结构（社区主流）

```
ArtAssets\
├─ 00_角色设定\        角色定妆图
├─ Art\
│  ├─ Player\  Sheets\(+raw 透明底留档)  Frames\<动作>\
│  ├─ Enemies\ Enemy01\ Enemy02\ Enemy03\   各含 Sheets\(+raw) 与 Frames\
│  ├─ Towers\  Tower01\
│  ├─ Boss\    Boss01\
│  ├─ NPC\     NPC_Ava\   各含 Sheets\(+raw) 与 Frames\
│  └─ FX\      Sheets\(+raw)  Frames\<特效>\
└─ _预览\       深绿底总览图（人看，不导入 Unity）

批量生成/补齐资源时：先读 `Tools/art-pipeline/美术资源生成流程.md`（操作手册，与管线脚本同目录，含流程/脚本/复用指令），本文件只作风格基准。
```
每个动作：`Sheets\<名>_sheet.png`（等大透明精灵表，Unity 按 cell 切）；`Frames\<名>\<名>_00.png…`（单帧）。

## 一、主角 Player（2026-10-26 依新定妆图 Player_Lockup_A.png 重生成，22 动作 / 130 帧）

**Cell 基准 1200 × 833（22 个动作画格完全一致，不再自适应）：旧 692 宽装不下冲刺/蹬墙/重击的横向幅度，实测最多需要 1183，切图会把胳膊和头削平，故统一放宽到 1200；基准身高取条内最高帧 760px，Pivot = Bottom Center，建议 PPU = 378（身高≈2 世界单位）。**

> 全部 22 个动作走同一套参数：固定画格 1200×833 + 固定身高 760 + 脚底锚点 + 去脚底投影 + 去白边/白洞 + 清孤立碎点。目的是「任何动作切换时体型与画布零跳变」。

| Clip | 帧数 | 网格 | Samples | 循环 | 时长 | 事件帧 |
|---|---|---|---|---|---|---|
| Player_Idle | 10 | 5×2 | 12 | 是 | 0.83s | — |
| Player_Run | 8 | 4×2 | 24 | 是 | 0.33s | 帧1 Footstep |
| Player_Jump | 3 | 3×1 | 24 | 否 | 0.13s | 帧1 JumpFx |
| Player_Fall | 4 | 2×2 | 12 | 是 | 0.33s | — |
| Player_Land | 3 | 3×1 | 24 | 否 | 0.13s | 帧1 LandFx |
| Player_Attack01 | 8 | 4×2 | 24 | 否 | 0.33s | 帧2 HitOn（投掷）；帧8 HitOff |
| Player_Interact | 6 | 3×2 | 18 | 否 | 0.33s | 帧3 夹取；帧5 拾取；帧6 End |
| Player_Hurt | 3 | 3×1 | 15 | 否 | 0.20s | 帧1 受击音+无敌起 |
| Player_Die | 8 | 4×2 | 18 | 否 | 0.44s | 帧1 死亡音；帧8 DieEnd |

### P1 扩展动作（13 条已补齐，画格与 P0 同为 1200×833）

| Clip | 帧数 | 网格 | Samples | 循环 | 事件帧 | 备注 |
|---|---|---|---|---|---|---|
| Player_Dash | 6 | 3×2 | 30 | 否 | 帧1 DashFx；帧1-6 无敌 | 闪避位移 |
| Player_Roll | 8 | 4×2 | 24 | 否 | 帧2-6 无敌窗口 | 团身帧自然变矮（391px），翻滚必须如此 |
| Player_Attack02 | 8 | 4×2 | 24 | 否 | 帧3 HitOn；帧7 HitOff | 连击二段 |
| Player_Attack03 | 9 | 3×3 | 24 | 否 | 帧1 HitOn（0.0375s）；帧8 HitOff（0.31875s） | 连击收尾（投掷蓝水桶） |
| Player_AttackAir | 6 | 3×2 | 24 | 否 | 帧3 HitOn；帧5 HitOff | 空中挥击 |
| Player_Victory | 8 | 4×2 | 15 | 是 | 帧1 胜利音（可选） | 过关结算 |
| Player_Turn | 1 | 1×1 | 24 | 否 | 无 | 转向过渡（可裁剪） |
| Player_HeavyAttack | 12 | 4×3 | 24 | 否 | 帧1-5 蓄力；帧7-9 HitOn | 重击 |
| Player_WallSlide | 3 | 3×1 | 12 | 是 | 无 | 贴墙下滑 |
| Player_WallJump | 3 | 3×1 | 24 | 否 | 帧1 JumpFx | 墙跳 |
| Player_LedgeGrab | 3 | 3×1 | 12 | 是 | 末帧 Hold | 抓边悬挂 |
| Player_Crouch | 2 | 2×1 | 15 | 否 | 帧2 Hold | 下蹲（蹲姿帧自然变矮 624px） |
| Player_Idle02 | 8 | 4×2 | 10 | 是 | 帧1 打哈欠音（可选） | 长待机 |

## 二、敌人 / 塔 / BOSS（P0 已完成）

每个角色内部动作尺寸统一；**跨角色体型差用 Unity `localScale` 体现**（见末列，PPU 统一 378）。

### Enemy01 污染污泥怪（关卡1·慢速）Cell 1312×872 / 22 帧　建议 localScale ≈ 0.64（约1.3单位）
| Clip | 帧 | 网格 | Samples | 循环 | 事件 |
|---|---|---|---|---|---|
| Enemy01_Idle | 4 | 2×2 | 12 | 是 | — |
| Enemy01_Move | 6 | 3×2 | 10 | 是 | 慢速蠕动 |
| Enemy01_Attack | 4 | 2×2 | 15 | 否 | 帧3 HitOn / 帧4 HitOff |
| Enemy01_Hurt | 2 | 2×1 | 15 | 否 | 帧1 |
| Enemy01_Die | 6 | 3×2 | 15 | 否 | 帧6 DieEnd（化油泥） |

### Enemy02 变异毒植物（关卡2·较快）Cell 1397×1247 / 28 帧　localScale ≈ 0.68（约1.6单位）
| Clip | 帧 | 网格 | Samples | 循环 | 事件 |
|---|---|---|---|---|---|
| Enemy02_Idle | 4 | 2×2 | 12 | 是 | — |
| Enemy02_Move | 8 | 4×2 | 15 | 是 | 弹跳匍匐 |
| Enemy02_Attack | 6 | 3×2 | 18 | 否 | 帧3 HitOn（咬/喷毒）/ 帧5 HitOff |
| Enemy02_Hurt | 2 | 2×1 | 20 | 否 | 帧1 |
| Enemy02_Die | 8 | 4×2 | 15 | 否 | 帧8 DieEnd（枯萎化毒水） |

### Enemy03 巡逻机器人（关卡3）Cell 993×1124 / 36 帧　localScale ≈ 0.72（约1.7单位）
| Clip | 帧 | 网格 | Samples | 循环 | 事件 |
|---|---|---|---|---|---|
| Enemy03_Spawn | 4 | 2×2 | 24 | 否 | 帧1 开机音（出场启动） |
| Enemy03_Idle | 4 | 2×2 | 12 | 是 | 扫描眼 |
| Enemy03_Chase | 8 | 4×2 | 18 | 是 | 滚轮高速 |
| Enemy03_Attack | 8 | 4×2 | 20 | 否 | 帧3 HitOn（电击）/ 帧5 HitOff |
| Enemy03_Hurt | 2 | 2×1 | 20 | 否 | 帧1 |
| Enemy03_Die | 10 | 5×2 | 15 | 否 | 帧10 DieEnd（坍塌冒烟） |

### Tower01 毒气防御塔（关卡3·固定）Cell 1039×979 / 19 帧　localScale ≈ 0.77（约1.8单位）
| Clip | 帧 | 网格 | Samples | 循环 | 事件 |
|---|---|---|---|---|---|
| Tower01_Idle | 4 | 2×2 | 12 | 是 | 警戒灯/风扇 |
| Tower01_Aim | 4 | 2×2 | 12 | 否 | 帧4 锁定音（攻击前摇） |
| Tower01_Fire | 6 | 3×2 | 12 | 否 | 帧3 HitOn（喷毒）/ 帧5 HitOff |
| Tower01_Disable | 5 | 3×2 | 12 | 否 | 帧5 停机（被净化变灰） |

> Tower01 四套**统一 1039×979**（画格与 Fire 同规格，塔体在格内的像素坐标四套完全一致，切换 Clip 时零位移，故不再按底座锚点自适应宽度）；高度 979、底留白不变。
> 定妆图 = `Tower01_Fire_00`（射击第一帧）。Tower01_Idle / Aim / Disable **不走生成式管线**：塔体逐像素取自 Fire_00，仅在背景区叠加效果 —— Idle：罐顶警戒灯辉光脉动 + 风扇轮缘风影弧；Aim：炮口蓄能绿气由弱到强；Disable：逐帧去饱和变灰 + 顶部升烟。派生脚本见 `Tools/art-pipeline/make_tower_sets_from_fire.py`（三套的第 1 帧均与 Fire_00 逐像素相同）。Disable 做 **5 帧**是刻意与 `Disable.anim` 的 5 个键（00..04、末键 t=1/3 s）对齐，使**末帧即「全灰停机」终态**而不是永不播放的第 6 帧。
> 四套 Clip 同一座塔：粗短炮管一律朝左上 + 圆顶橄榄绿锈蚀塔身 + 塔身左侧大红圆灯 + 顶部金框白色压力表 + 右侧红顶黄黑斜条纹罐 + 右下黑色辐条风扇轮 + 喇叭形铆钉底座。

### Boss01 工厂污染核心（关卡3 最终 BOSS）Cell 1216×798 / 85 帧　localScale ≈ 2.25（约4.5单位）
| Clip | 帧 | 网格 | Samples | 循环 | 事件 |
|---|---|---|---|---|---|
| Boss01_Idle | 6 | 3×2 | 10 | 是 | 烟囱冒烟/核心脉动 |
| Boss01_Move | 10 | 5×2 | 12 | 是 | 沉重推进 |
| Boss01_AttackA | 10 | 5×2 | 20 | 否 | 帧4 HitOn（砸地）/ 帧6 HitOff |
| Boss01_AttackB | 12 | 4×3 | 18 | 否 | 帧4 HitOn（喷毒）/ 帧8 HitOff |
| Boss01_Charge | 8 | 4×2 | 12 | 是 | 蓄力：核心鼓胀 → 绿雾外溢 → 回落，1.1s 循环 |
| Boss01_Special | 14 | 5×3 | 24 | 否 | 帧6 技能生效（熔渣喷吐）/ 帧14 End |
| Boss01_Enrage | 6 | 3×2 | 20 | 否 | 帧1 狂暴音；帧6 切换强化状态 |
| Boss01_Hurt | 3 | 3×1 | 15 | 否 | 帧1 |
| Boss01_Die | 16 | 4×4 | 15 | 否 | 帧16 DieEnd（坍塌废墟） |

> **Boss01 全套重制（2026-10-26，依新定妆图 `Boss01_Lockup_A.png`）**：9 条动作全部重生成（Die 由 24 帧改为 16 帧 / 4×4），画布由 `831×798` 改为 **`1216×798`**（9 条完全统一）。四项口径改动，以后改 Boss01 必须沿用：
> 1. **画格加宽到 1216**：旧 831 会把挥砍/倒地这类横向大动作切掉本体（用户报「切割到不该切的地方」）。实测各条「底座锚点 + 单侧最远内容」需求最大 1207px（AttackB 第 6 帧），故统一 1216。
> 2. **`anchor_final=True`**：底座锚点改为「先缩到最终像素尺寸、再量 74px 底座带」。旧做法在 raw 尺寸上量再乘 scale，等于把 raw 的 74px 映射成约 116px，与校验口径根本不是同一块身体，导致底座中心逐帧漂移（AttackB 实测极差 153.5px，且与画格宽无关，加宽画格完全无效）。改后 9 条水平偏移极差全部 **≤1.0px**。
> 3. **`white_unmix=True` + `white_holes=True` 两级白底清理**：gpt-image 只出 RGB（无 alpha），入口阈值抠图会在边缘留白色描边。`unmix_white_edge()` 按 `C观察=a·C机体+(1-a)·255` 反解覆盖率并反混合颜色；白底清理分两级：
>    - **raw 阶段 `kill_enclosed_bg_holes()`（主力）**：`matte_background()` 只去「与图片边缘连通」的背景，肢体缝隙里（腋下、手臂与躯干之间、烟柱与塔身之间）被整片包围的白底不是边缘连通的，会原样留下来 —— 这就是「Move 第 1 帧腋下白底」「Enrage 第 1 帧 6882px 白块」「Idle 有一帧白底」的共同根因。判据只有一条：**封闭 + 三通道都 ≥240**。raw 实测背景 248~255、文生图画上去的白烟/高光最高只到 ~232，两者不重叠，所以不会误伤美术（9 条素材里 224~232 的白烟实测全部保留）。最小面积必须设到 **6 个 raw 像素**：腋下那条白缝在 raw 上只有 31px，放大 1.7 倍后就是 85px 的白块，阈值设 60 就会漏。
>    - **成品阶段 `kill_bg_holes()`（兜底）**：只抓缩放重采样又插值出来的小岛，判据同为「封闭 + 三通道最小值中位数 ≥240」；封闭性用「3×3 闭运算 2 次再 `fill_holes`」判 —— 1~2px 宽的抗锯齿细缝会把内部白洞和外部背景连起来（Enrage 第 1 帧那块就只差 1px），不桥接就会漏判。
>    - 旧判据「面积≥600px + 均值≥244 + 240 占比≥60%」**已废弃**：它在成品尺寸上判，既漏掉 <600px 的腋下白块，又把背景白和画的烟糊在中间值里判不准。
> 4. **Die 用 `ref='first'`**：倒地帧的烟柱比站立帧还高，`ref='max'` 会把站立帧反缩成矮子；改用首帧（站立）主体身高做基准、整条统一比例，站立帧与 Idle 同高，倒地自然变矮。Die 同时把 `OnDieEnd` 事件按 85% 重算（旧时间 1.700s 超出新 clip 长度，事件不会触发 → Boss 死亡不推进目标链）；现 `Die.anim` 16 键 / 1.333s / 事件 1.0625s。
>
> 校验结果（`check_frames.py` 9 条全 PASS）：身高 756~760（首帧）、脚底线极差 0~2px、底留白 7px、水平偏移极差 0.5~1.0px、内容左右上下无裁切。白底复扫（82 帧，判据 medMin≥240 & 面积≥8）：体内封闭背景白 **0 处**，仅剩 12 处 8~23px 的轮廓边缘抗锯齿（距透明区 1~4px，属边缘描边而非体内白块，清它会啃轮廓，故保留）；画的白烟 224~232 全部在位。

> **Boss01_Charge（2026-10-23 新增）**：技能 B / 特殊招发射前的专用蓄力序列，与 Idle/AttackB 同一套「方箱炉体 + 双烟囱 + 大圆形红核 + 黄黑警示臂 + 履带」造型。管线条目 `cols=4 / rows=2 / n=8 / cell 831×798 / target 760`，并启用 `norm='frame'`（逐帧等高，消除生成图 693~760 的体量差）与 `anchor='base'`（按履带底座定位，抵消绿雾拉宽造成的 21px 漂移）。校验：8 帧身高均 760、脚底线极差 0px、底留白 7px、水平偏移极差 7.5px，`check_frames.py` PASS。Unity 侧 `Charge.anim` 为 9 键（8 帧 + 首帧回补）、循环、时长 1.183s，Boss01.asset 的 `AttackBWindupSeconds=1.10` / `SpecialWindupSeconds=1.40` 驱动其循环播放。

> **Boss01_Special / Enrage 重生成（2026-10-23）**：以 Idle 首帧 `Boss01_Idle_00` 为定妆基准重画，造型（方箱炉体 + 双烟囱 + 大圆红核 + 黄黑警示臂 + 双履带）与 Idle 统一，画布统一 `831×798`、PPU 378、pivot 居中，Unity 内 20 帧 `rect=(831,798)`、`ppu=378`、`pivot=(415.5,399)` 全部一致。管线参数：Special `cols=5/rows=3/n=14/target=788`、Enrage `cols=3/rows=2/n=6/target=780`，两条均 `parts=None`（帧内含分离的绿烟/白汽）+ `fixed_canvas=True`（防止缩放后画格被裁）+ `shadow_trim=True`。`shadow_trim` 负责三类清理：① 脚底浅灰投影（生成图自带，游戏里是一条白横带）；② 被履带围住而漏抠的白底（裆部）；③ 轮拱内闭合的白板（整条白板贯穿下半身）——③ 只清「顶边落在内容高度 50% 以下、面积 ≥600px 的近白连通域」，上半段的白色烟雾与发光核心（顶边在 0.34~0.44）保留。实测：内容底边 789~790（与 Idle 789 一致，不再悬浮），底带亮像素占比由 0.55~0.71 降到 0.00~0.04，下半身大块白板全部清除；身高 742~788（峰值帧是烟雾顶起，站定帧 ~757 与 Idle 758 对齐）。`Special.anim` 14 键 / 1.167s、`Enrage.anim` 6 键 / 0.5s 键位不变，资产原地覆盖故 clip 引用无需重绑。

> **Boss 烟柱顶部羽化（2026-10-23）**：文生图把烟柱画到格子上沿就截断，raw 顶部留白为 0，顶边是硬平口——缩放补不回丢失的像素，补白边也只会把硬口下移。改为输出时做**顶部渐隐**：`process_sheets.py` 新增 `FEATHER_ROWS=56` 与 `feather_top()`，把内容顶部 56 行 alpha 按 1→0 线性递减（只动 alpha，不动颜色）。Charge/Special/Enrage 在 SPECS 里直接挂 `feather_top=True`（Charge 同时补 `shadow_trim=True`）；Idle/Move/AttackA/AttackB/Hurt 不在 SPECS 表内，由 `Tools/art-pipeline/feather_smoke.py` 对单帧与 sheet 做一次性羽化（脚本内 `ROOT` 归一化为绝对路径，避免 Pillow 写回含 `..` 报 OSError 22）。Die 不羽化（后半段顶部是废墟轮廓，削了会缺角）。实测各帧顶行 alpha≈9、+10 行≈54、+28 行≈136、+56 行 255，硬平切口消失；`feather_top` 检测到顶部已很淡会跳过，重复跑不会越跑越淡。

> 待补（P1）清单已于 2026-10-15 全部补齐：主角 13 条扩展动作（82 帧）、Enemy03_Spawn（4 帧）、Tower01_Aim（4 帧）、Boss01_Special/Enrage（20 帧），共新增 **110 帧**；总览见 `_预览\contact_P1_new_clips.png`。

## 三、独立特效 FX（黑底转透明，36 帧）

| 特效 | 帧 | 网格 | Samples | 用途 / 挂载点 |
|---|---|---|---|---|
| fx_slash | 6 | 3×2 | 24 | 主角挥夹判定帧，叠在 Attack01 帧4-5 |
| fx_hit | 6 | 3×2 | 24 | 任意命中瞬间，Pivot 对齐受击点 |
| fx_purify | 8 | 4×2 | 20 | 净化敌人/收集道具，Interact 与敌人 Die 末段 |
| fx_dust_run | 4 | 2×2 | 15 | Run 脚步帧 / Jump / Land 脚下 |
| fx_gas | 6 | 3×2 | 15 | 毒植物、毒气塔、BOSS 喷毒判定处 |
| fx_explode | 6 | 4×2 | 24 | 机器人/BOSS 爆炸（浓烟阶段已并入火光 6 帧） |
| `Projectiles/proj_toxin_sting` | 1 | 单图 | — | 怪物远程弹体（Enemy02 毒刺 / Tower01 / BOSS 共用），Pivot Center、PPU 378、sortingOrder 20，运行时按飞行方向旋转 |

弹体是单帧精灵，放在 `Art\FX\Projectiles\`；拖尾材质为 `Assets\Prefabs\Projectiles\ProjectileTrail.mat`。

FX 用法：材质推荐 **Additive**（剑光/火花/净化/爆炸会自然发光，黑底完全透明）；毒气/扬尘用普通 Transparent 即可。Pivot = Center。

## 四、Unity 导入设置

1. 角色 `Sheets\*.png`：Sprite Mode **Multiple** → Sprite Editor → Slice **Grid By Cell Size**，填该角色上表 Cell 尺寸；Pivot **Bottom Center**；删末尾空格。
2. Filter **Bilinear(Soft)**；Compression **BC7**（PC）/ ASTC 6×6（移动）；**关 Mipmap**；角色 Max Size **8192**（个别敌人 sheet 略宽于 4096），FX 用 4096。
3. 全角色统一 **Pixels Per Unit = 378**；体型差用各角色 `localScale`（上表系数），不要改 PPU。
4. 朝左用 `SpriteRenderer.flipX`；位移由 Rigidbody2D 驱动（关 Root Motion）。
5. Idle/Run/Move/Chase/Fall 等循环动作勾 Loop Time，其余不循环。
6. Run 0.67s × moveSpeed = 单循环步长（moveSpeed=4 时约 2.67 单位），按此防打滑。
7. Animator 参数：Speed(float)、IsGrounded(bool)、VVelocity(float)、Attack/Hurt/Dash(Trigger)、IsDead(bool)；打断优先级 Die>Hurt>Attack/Interact>Dash>Jump>Run/Idle。

## 五、已知 AI 素材瑕疵（可手修，不影响使用）

- Player_Attack03 于 2026-10-23 按用户提供的新图集整条替换为 **9 帧「蓄力→举桶→投掷→跟随→收势」**（3×3、0.33s 未变，画格宽自适应 1539，底座锚点 + 逐帧身高归一，9 帧人物高 757–760px 与待机一致）。**图集中与角色分离的飞桶/光束/爆炸/水花未纳入帧**：它们浮在角色头顶上方 40–60px，角色归一放大到 760px 后需要超出 833 画格 71–236px，硬塞会被裁断；后续如需表现投掷物请按独立 FX/弹体制作。
- Player_Attack01 于 2026-10-23 按用户提供的新图集整条替换为 **8 帧「投掷机械爪 + 蓝光束回收」**（4×2、0.33s，画格宽自适应 2352，底座锚点对齐以抵消抛掷时光束拉宽造成的身体横移；单帧纹理 Max Size 提到 4096 以保住原生像素）。管线已内置 **`norm='frame'` 逐帧身高归一**（各帧先等比缩放到条内基准身高再统一缩放），8 帧人物高度统一为 758–760px，与 Idle 的 749–763px 一致，不再出现攻击时人物变矮/大小跳动。已知瑕疵：①单帧源分辨率仅约 200–240px 高，缩放 3.14 倍到 760 后画面偏软，清晰度明显低于其余动作；②张腿较大的帧（首末帧）脚部包围盒被拉宽，底座锚点仍有约 0.3 世界单位的水平残余漂移。Player_Die 第1帧表情笔触偏重。
- 敌人/BOSS 喷毒、电击等攻击帧的喷出物在格边缘略有截断，运行时由 fx_gas / fx_hit 覆盖。
- Player_Run 为"手持机械爪"构图（用户确认保留）；其余移动类动作机械夹挂背包/腰侧，攻击与互动才手持。

## 制作流程（已自动化，脚本在会话工作目录）

alpha-first 精灵图集管线：入口先归一化为**透明底 RGBA master**——源图已是透明底则直通（原生 alpha 最保真），
白底/黑底则先抠图变透明（阈值抠图质量天然低于原生 alpha，故对这类来源额外做 1px 收缩去白晕）
→ 分帧（网格切格 / 连通域自动分帧，可只留最大域去悬空配件）→ 条内身高中位数统一缩放 → 脚底基线对齐（留白 7px）
→ 重拼等大透明精灵表；坏帧用相邻帧替换。全程**不再经过白底中间图**，raw 与成品均为透明底 RGBA；
透明区做颜色外溢（alpha bleeding）避免缩放渗色出晕圈。FX 另走一条：黑底亮度转 alpha（Additive 友好）。

## 五、环境 Environment（M10 R1：Level01 先行）

风格沿用上文的「高清卡通描边」，色调偏暗紫 + 病态黄绿，呼应「污染星球」设定。

| 素材 | 尺寸 | PPU | 世界尺寸 | 用法 |
|---|---|---|---|---|
| `Environment\Tiles\ground_polluted.png` | 760×190 | **190** | **4×1 单位** | 横向无缝周期，Sprite **Tiled**，WrapMode=Repeat，Mesh Type=FullRect |
| `Environment\Background\bg_polluted_planet.png` | 1920×1080 (16:9) | 100 | 16×9 单位 | 跟随相机铺满视野，sortingOrder −100 |

**拼接基准（改相机参数时必须同步重算）**

- Level01 相机 `orthographicSize = 4.5`、`position.y = 2` → 视野高 9 单位，地面顶面（世界 y=0）占屏高 (2+4.5−0)/9 = **72.2%**。
- 背景图地平线已按此对齐在图内 **72.2%**（y=780/1080），使远景工厂剪影正好坐在地面线上。
- 背景图宽高比必须 = 视野宽高比（16:9），否则会被拉伸变形。

**Unity 导入要点**

1. 地面瓦片：`Sprite Mode = Single`、`Mesh Type = **FullRect**`（Tiled 必需）、`Wrap Mode = **Repeat**`、关 Mipmap、Bilinear。
2. **`SpriteRenderer.size` 在 Tiled 模式下是「世界单位」，不是像素**（Simple 模式才是像素）。传像素值会放大 PPU 倍铺满全屏——本项目曾踩过。
3. 灰盒 Ground 换素材时注意：`MakeWorldBox` 靠 `transform.scale`（原 1000×25）把 0.04u 的 WhiteSquare 撑成 40×1，碰撞体存的是被放大的局部单位。换真素材须先算 `collider.size × scale` 得世界尺寸，再把 scale 归一、碰撞体改回世界单位。
4. 背景**不要用 Sliced**：该图 `.meta` 的 border 为 0，Sliced 退化后会按 1920×1080 像素粒度切块，运行时斜铺出巨大色块。改用 `Simple + transform.scale` 拉伸。

**仍为灰盒、待补的占位符**（Level01）：无——`NPC_Ava`、两块提示牌、两个知识卡片、两个检查点、三个垃圾堆、挡路垃圾墙均已换真素材；`SpawnAnchorA/B` 是刷怪逻辑锚点，**刻意隐藏**（`SpriteRenderer.enabled = false`），仅在 Editor 中作位置句柄用。

## 六、道具 Props（M10 R2：Level01 占位符全部替换）

目录 `Assets\Art\Art\Environment\Props\`。白底生成图经 **flood-fill 容差抠白** 处理成透明底并 trim：
背景色取四边中位数，只吃**与画面边界连通**的浅色区，因此道具内部的白色高光（提示牌上的白色标志、卡片上的白色叶脉）不会被误抠。

| 素材 | 像素 | PPU | 世界尺寸 | 用法 | 语义对应 |
|---|---|---|---|---|---|
| `npc_ava.png` | 390×922 | 485 | 0.80×1.90 | 教学 NPC，落地后 scale 1.16 → **2.20 单位高**（与主角等高） | `ava_intro` |
| `sign_plastic_warning.png` | 288×1024 | 890 | 0.32×1.15 | 灰绿警示牌，白色警告三角 | `Signboard_Plastic` ← `signboard_plastic`（塑料污染警示） |
| `sign_recycle.png` | 260×926 | 805 | 0.32×1.15 | 蓝绿牌，白色回收三角 | `Signboard_Recycle` ← `signboard_recycle`（可回收物识别） |
| `knowledge_card.png` | 509×538 | 1076 | 0.47×0.50 | 琥珀晶体卡，离地 0.35 | `KnowledgeCard1/2` |
| `checkpoint.png` | 377×746 | 481 | 0.78×1.55 | 青蓝布旗检查点，触发区 1.2×1.2 | `Checkpoint1/2` |
| `garbage_pile.png` | 724×327 | 452 | 1.60×0.72 | 可清理垃圾堆（塑料瓶/纸箱/锡罐），碰撞体 1.60×0.90 | `Garbage1/2/3` |
| `garbage_wall.png` | 809×957 | 578 | 1.40×1.66 | 高瘦捆扎挡路墙（麻绳+木箱），碰撞体 1.40×2.20 | `GarbageWall1` |
| `platform_flat.png` | 1200×245 | 400 | 3.00×0.61 | **侧视平顶**木板平台（无透视、顶面为水平直线），场景内 scale 2/3 → 可见 2.00×0.41 | `PlatformL1/L2/R1/R2`（Level02 两级台阶） |
| `platform_stairs.png` | 1585×938 | **355.303** | 4.461×2.640 | **纯侧视金属楼梯**（5 级格栅踏板 + 黄黑警示条 + 锈蚀钢架），左岸直接摆、右岸 `flipX` 镜像 | `StairsL`（x −16.2~−11.0，顶 2.64）/ `StairsR`（x 11.0~16.2，顶 3.50）（Level03 两岸台阶） |

### 垃圾堆/墙的贴地约定（与 LevelSetup 的坑）

`GarbagePile` / `GarbageWall` 是 **prefab**（带 `GarbagePile` 逻辑组件），不走 LevelUISetup 那套。约定：

- prefab 根节点位于物件**中心**，`BoxCollider2D.offset = 0`，`localScale = 1`；
- 场景实例的 `position.y` 必须给「**视觉**高度 ÷ 2」才能让底边贴地：垃圾堆 0.36、垃圾墙 0.83。
  （曾按旧灰盒中心值 0.9/1.4 摆放 → 悬空；又曾用碰撞体半高 0.45/1.10 摆放 → 视觉与碰撞体错位。**一律以视觉半高为准**。）
- **碰撞体尺寸刻意沿用原灰盒值**（1.60×0.90 / 1.40×2.20），不随视觉高度改，避免改变关卡可通行性与手感；`LevelSetup.Run` 里的 y 值已同步为半高约定。
- 垃圾墙第一版生成成矮胖「一堆」，拉伸到 2.2 高会明显变形，已重新生成高瘦构图（宽高比 ≈ 1:1.17），与三个矮胖垃圾堆形成层次。

- **PPU 按「目标世界高度」反算**（`PPU = 像素高 / 目标单位高`），避免落地后靠 scale 硬凑把描边拉糊。
- 提示牌三张共用同一根木柱与边框锈斑（ImageGen 以 `sign_recycle` 为 image1 + `input_fidelity: high` 续生成），保证系列感。
- **`Signboard_Plastic` 配的是「警示」而非「堆肥」标**：查 `dialogues.json`，该牌文本讲塑料污染与「勿焚烧」，堆肥语义不符，故重做为警告三角。
- 脚本 `LevelSetup.MakeCheckpoint` / `LevelUISetup.CreateSignboard/CreateKnowledgeCard/CreateTriggerVisual` 均改为「优先真素材、缺失才退灰盒」，可重跑而不会冲掉美术。

### 平台素材的对齐约定（`platform_flat.png`）

- 旧 `platform_log.png` 是 3/4 俯视原木，顶面本身是斜的，角色站上去像踩斜坡，已停用（文件保留作备份）。
- 新图为**纯侧视**：木面顶边是一条水平直线，其上另有约 0.09u 的草叶/苔藓；站立面按**木面水平线**算，不按精灵包围盒顶算。
- 木面在精灵局部坐标 `y = +0.1735`（PPU 400、Pivot=Center、原生 3.00×0.6125），场景与 `Level02Setup.MakePlatform` 都以此为基准对齐碰撞体顶面。
- 碰撞体**世界厚度固定 0.56u**（可见厚度仅 0.41u，多出的部分向下延伸）：薄平台若严格按画面厚度做碰撞体，高速下落时容易单帧穿透。
- 背景抠除走本文档开头那套 **flood-fill 容差抠白 + 1px 收缩 + 颜色外溢**，生成图为白底 JPG，成品为透明底 PNG。

### 台阶素材的对齐约定（`platform_stairs.png`）

- 同样走 **flood-fill 抠白 + 1px 收缩 + 颜色外溢**；PPU 按目标世界高度反算 = 938 / 2.64 = **355.303**，原生 4.461×2.640（trim 后）。
- 造型为正交侧视的 **5 级**金属楼梯：贴图上有 5 条黄黑警示条、5 块格栅踏板。**避免凭肉眼估级数**——按黄色条带的行位置数最保险（本项目曾误判成 4 级，导致少一级碰撞体）。踏板顶面是进深约 0.47u 的平行四边形，带轻微 3/4 观感。
- 站立面取「每块踏面可见进深的中间高度」，在 Level03 用**总高等分 5 份**即近似成立：左岸 `StairsL` x −16.2~−11.0、总高 2.64（每级 0.528，踏面 0.528 / 1.056 / 1.584 / 2.112 / 2.640）；右岸 `StairsR` x 11.0~16.2、总高 3.50（每级 0.70，踏面 0.70 / 1.40 / 2.10 / 2.80 / 3.50），`flipX` 镜像让开口朝外侧。
- 右岸比左岸高 → `localScale.y` 1.326（左岸 1.0）、`localScale.x` 两岸同 1.166，属刻意非等比，换来「沿用原平台顶面高度」。
- 碰撞体是**每级踏板一块 0.30u 厚薄板**（每座 5 块，两岸共 10 块），不是整块实心：沿用旧 `platform_catwalk` 的「顶面薄板」语义，楼梯下方仍可站立/通过，出生点 (x=−12) 不会被台阶推出。
- 已替代 Level03 原先的 `PlatformL1/L2/R1/R2` 四块 `platform_catwalk`；`platform_catwalk.png` 仍保留给其它工业走道布景使用。

**踩坑：pivot 在本工程 Unity 构建中改不动**

`TextureImporter` 反射确认**不存在** `spriteAlignment` / `spriteMeshType`（只有无用的 `spritePivot`），写 `.meta` 的 `alignment: 1` 也不生效——`sprite.bounds.center.y` 始终是 `-高/2`，即**实际按 Center pivot 处理**。
因此所有道具统一用 Center 语义摆放：`position.y = 地面顶面(0) + 高度/2`（悬浮物再加 yOffset），`localScale` 归一为 1。
FullRect / pivot 一律在 `.meta` 文本层设置，不走 API。

### 可破坏道具（水草 / 石头，Level02）

| 素材 | 像素 | PPU | 世界尺寸 | 用法 |
|---|---|---|---|---|
| `breakable_reed.png` | 490×540 | 400 | 1.23×1.35 | 香蒲水草簇，Trigger 1.08×1.19，sortingOrder **6** |
| `breakable_rock.png` | 626×380 | 400 | 1.56×0.95 | 苔藓圆石，Trigger 1.38×0.84，sortingOrder **5** |
| `Debris/reed_00..05.png` | 134~259 宽 | 400 | 0.34~0.65 | 水草碎块（6 块） |
| `Debris/rock_00..05.png` | 180~261 宽 | 400 | 0.45~0.65 | 石头碎块（6 块） |

- 生成脚本：`Tools/art-pipeline/make_breakables.py`（输入 `art_gen/breakable_*_raw.jpg` 洋红底原图）。
- **洋红键**：`alpha = clamp((118 - (min(r,b) - g)) / 60)`，再按 `spill = min(r,b) - g` 压掉红/蓝通道的品红溢出；全程不经过白底中间图。
- trim 后按**目标世界高度**反算像素（水草 1.35u、石头 0.95u），PPU 统一 400（与 `platform_flat.png` 一致）；Pivot 仍按工程约定用 Center，落地时 `position.y = 视觉高度/2`。
- 碎块是**在不透明像素上做 Voronoi 划分**（6 个种子点 + 3 次 Lloyd 迭代）。每块尺寸天然等于它在原图中的占比，因此用同一 PPU 导入后，原地抛出即可还原碎裂前的轮廓尺度，不需要额外记录偏移。
- 场景摆放由 `BreakableSetup`（菜单 `StarEco → Setup → Breakables`）驱动，共 **17 个**：两岸沿路 + 水边，绕开笼子、垃圾堆、堵塞物落点与两个刷怪锚点；只有装饰树允许树基/树冠重叠（树 sortingOrder 10 在道具之上，看起来像长在树下）。
- 运行时：`BreakableProp` 实现 `IHittable`，一击即碎 → 播放 `BreakPlant` / `BreakStone`（±7% 随机音高）→ 抛出 6 块带重力、自旋、1.15s 淡出的碎块（`BreakableDebris`，不挂 Rigidbody2D，刻意不与玩家/怪物产生实体碰撞）。

### NPC 教学角色 Ava 的动画（待机 8 帧 / 金陵突击 8 帧）

目录 `Assets\Art\Art\NPC\NPC_Ava\`（新建的角色目录，与 Player/Enemies/Towers/Boss 同级）。

| 素材 | 画格 | 帧数 | PPU | 用途 |
|---|---|---|---|---|
| `Sheets\NPC_Ava_Idle_sheet.png` | 692×955 | 8（4×2） | 485 | 教学 NPC Ava 的**待机**动作（默认状态） |
| `Frames\NPC_Ava_Idle\NPC_Ava_Idle_00..07.png` | 692×955 | 8 | 485 | 逐帧序列（Reading 顺序：左→右、上→下） |
| `Sheets\NPC_Ava_JinlingAssault_sheet.png` | 692×955 | 8（4×2） | 485 | 教学 NPC Ava 的动作片段「金陵突击」 |
| `Frames\NPC_Ava_JinlingAssault\NPC_Ava_JinlingAssault_00..07.png` | 692×955 | 8 | 485 | 逐帧序列（Reading 顺序：左→右、上→下） |

- 两套源图均为 1536×1024 **纯白底**（4×2，每格 384×512），入库前放在 `Sheets\raw\` 由 `process_sheets.py` 入口抠白成透明；管线条目 `NPC_Ava_Idle` / `NPC_Ava_JinlingAssault`（cols 4 / rows 2 / n 8 / cell 692×955 / target 922 / `anchor='base'`）。
- **规格与现有 `npc_ava.png` 对齐**：PPU 485、主体身高 922px（世界 1.90u）、Pivot Center、底留白 7px。场景里现成的 `scale 1.16` / 世界高 2.20 单位**不需要改**，动画帧可直接替换静态图的表现。
- `anchor='base'`：Ava 抬手、举台账会把包围盒横向拉宽，按包围盒居中会让身体左右漂移（金陵突击实测极差 67.5px → 改脚底底座带后 3.5px；待机极差 1.5px）。
- 金陵突击源图帧内的黄色「强调线」是分离连通域，面积远小于主体，被入口的连通域清理（`KEEP_RATIO=0.12`）自动清除，不进角色帧。
- 校验：待机 `身高 851–922（自然呼吸/屈膝）、脚底线极差 0px、水平偏移极差 1.5px`；突击 `身高 897–922、脚底线 0px、偏移 3.5px`，两套均 **PASS**。
- **Unity 接入（已完成）**：`Assets\Animations\NPC\` 下 `NPC_Ava_Idle.anim`（8 键、12 fps、0.667s、循环）与 `NPC_Ava_JinlingAssault.anim`（8 键、12 fps、0.667s、循环），共用 controller `NPC_Ava_JinlingAssault.controller`：
  - 默认状态 **`NPC_Ava_Idle`**（常驻待机）；
  - 状态 `JinlingAssault` 仍在，`AnyState → JinlingAssault` 由 Trigger 参数 **`Assault`** 触发，播完（`exitTime=1`）自动回待机；
  - Level01 的 `NPC_Ava` 挂该 controller，`SpriteRenderer` 静态显示 `NPC_Ava_Idle_00`（PPU 485 / Center / `scale 1.16` / sortingOrder 4 均未改动）。
- 验证：`AnimationMode.SampleAnimationClip` 抽样 Idle t=0 / 0.25 / 0.50s 依次取到 `_00 / _03 / _06`；controller `states=2`、`params=1`（`Assault`）、`defaultState=NPC_Ava_Idle`；控制台无新增 error。

## 七、UI 素材（M11：主菜单）

目录 `Assets/Resources/UI/Menu/`（与 `UI/HUD/` 同级）。主菜单原先全靠运行时代码画纯色块，本次整体换成 AI 生成贴图。

| 素材 | 源尺寸 → 导入尺寸 | 用途 | 用法 |
|---|---|---|---|
| `menu_bg.png` | 2560×1440 → 1920×1080 | 全屏背景插画（左净水雨林 / 右污染工厂，中段刻意压暗留白） | 背景 Image，Type=**Simple**，Stretch |
| `menu_title_plate.png` | 2172×792 → 2048×747 | 标题牌匾，垫在「星际环保」之下 | RectTransform **700×255**，Type=Simple |
| `menu_button.png` | 2114×740 → 2048×717 | 按钮玻璃绿底板，6 个按钮共用一张 | **340×74**，Type=Simple |

- 加载入口：`StarEco.UI.MenuArt`（`Resources.Load<Sprite>("UI/Menu/…")`，缓存；取不到时自动回退原纯色表现）。
- 按钮皮肤会把 `Image.color` 置白并重设颜色渐变（常态白 / 悬停 ×1.25 / 按下 ×0.72 / 禁用灰）。

**生成与后处理约定**

- 含文字的贴图一律不生成文字（AI 中文字形不可靠）。中文标题仍由 `Text` 组件渲染，牌匾只做边框装饰。
- 抠底走**洋红键**而非白底：提示词要求主体置于纯洋红 `#FF00FF` 背景；脚本 `_tmp/menu/process.py` 以 `min(r-g, b-g)` 计算 alpha，并把与主体**不连通**的微弱光晕整体清零（首版未清，缩到 340px 后整屏浮一层绿雾），最后 despill 把残留洋红压回绿色域。
- `.meta` 以现有 HUD 帧贴图为模板生成（`spriteBorder` 归零，Simple 不需要九宫格 border）。
- 导入时 `maxTextureSize=2048` 会把 2114 / 2172 宽的图等比缩到 2048，观感无影响。

**踩坑：`Text` 大字号必须给够行框高度**

`UIUtility.CreateText` 的 `verticalOverflow` 固定为 `Truncate`。96 号字整行高约 **136px**，标题 RectTransform 只给 130px 时整行被丢弃（`cachedTextGenerator.lineCount == 0`），标题完全不显示且**不报任何错**；行框已给到 170px。

另：运行时 `AddComponent<Button>()` **不会**自动填 `targetGraphic`（`Selectable.Reset()` 仅在编辑器执行），需手动赋值，否则悬停/按下的颜色渐变不生效、换皮也会因 `targetGraphic` 为空而静默跳过。

### 7.1 弹窗面板皮肤（关卡选择 / 图鉴 / 设置三块共用）

| 素材 | 源尺寸 → 导入尺寸 | 用途 | 用法 |
|---|---|---|---|
| `menu_panel.png` | 1920×1920 → 1428×926 | 弹窗九宫格面板：深绿玻璃内腔 + 霓虹绿斜切角边框 | Image，Type=**Sliced**，`spriteBorder` 四边 **104** |
| `menu_close.png` | 1920×1920 → 256×256 | 右上角圆形 X 关闭按钮（深绿圆盘 + 霓虹绿描边 + 白 X） | **56×56**，Type=Simple，`preserveAspect` |

- 代码入口：`MenuArt.CreatePanel(parent, name, size, pos)` 建面板（返回的 RectTransform 直接当内容父节点）、`MenuArt.CreateCloseButton(panel, onClose)` 挂右上角关闭按钮（锚点 `(1,1)`，`anchoredPosition (-64,-64)`）。
- **内腔玻璃底色已烘焙进贴图**：`bake_panel_fill()` 把边框封闭的透明内腔填成 `RGBA(10,26,22,250)`。若让代码另叠一张矩形填充图，矩形直角会从边框的斜切角处露出来。
- 生成沿用洋红键，但改用 `key_magenta_unmix()`：① 只丢弃面积 < `min_area` 的孤立噪点（四角装饰是分离小块，不能用「只留最大连通域」）；② 边缘按背景色做 `F = M + (C-M)/a` 反解，去掉半透明边上的粉色描边；③ 本批调色板只有绿/灰/白，再把 R、B 夹到不高于 G，消除 JPEG 色度下采样渗进浅色高光带的紫色。
- 九宫格 border 取 104 是为了完整保留四角斜切装饰块；拉伸时只拉中段与四条边。

**踩坑：`Text` 宽度为 0 或行框高度不足，整段文字静默消失**

`LevelSelect` / `Codex` / `Settings` 三个面板的标题原本都写成 `sizeDelta = (0, 90)` 且锚点收在同一点 → 宽度 0，标题从来没渲染过；卡片名 32 号字塞在 44px 行框里（整行高 ≈45px）同样被 `Truncate` 整行丢弃。两处都不报错。修复：标题给 800 宽、96 高；卡片名行框放到 48。


