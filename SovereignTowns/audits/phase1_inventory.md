# Phase 1 — 盘点报告

> 日期：2026-05-18
> 范围：`SovereignTowns/` 整个 mod 项目（不含 `_research/` 反编译）
> 阶段约束：**只读**，本报告不修改任何代码。所有结论需附文件:行号或 git/grep 证据。

---

## 0. 占位符确认与假设

`<starting_instruction>` 只要求确认 `<context>` 占位符。`<context>` 三个值均已填充（项目根目录 / 主文档路径 / 主语言）。但 prompt 其他位置引用了未填的占位符，本报告基于以下**工程证据型推定**继续，请你在 Phase 2 前裁决：

| 占位符 | 推定值 | 证据 |
| --- | --- | --- |
| `<重构待办>` | 主文档 §20「重构待办」（2 项） | doc:1338–1344；标题字面 "重构待办" 与 `REFACTOR_TODO` 同义 |
| `<测试命令>` | `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug` | [CLAUDE.md](CLAUDE.md) 明确"There are no unit tests"；构建即编译验证 |
| `<静态检查命令>` | `pwsh SovereignTowns\tests\static-regression.ps1` | [tests/static-regression.ps1](SovereignTowns/tests/static-regression.ps1) 含 22 条 Assert，是项目内唯一静态校验 |
| `<不允许触碰>` | `SovereignTowns/_research/`，以及任何 `obj/` `bin/` 输出目录 | [CLAUDE.md](CLAUDE.md) "Local-only reference, not tracked in git"；obj 是构建产物 |

**已知盲点：** 上轮 `b1-hygiene-backlog.md`（已删除，git HEAD 仍可读）内有 8 项 P1/P2/P3 hygiene 项尚未处理。当前不确定它们是否要纳入本轮 `<重构待办>`，详见 §9。

---

## 1. 工作树基线（这是后续所有阶段的"起点 state"）

⚠️ **关键事实**：工作树**不干净**。最近一次提交是 `09f69a0`（B17.4 T9 closeout），但之后有大规模 WIP 未提交。

### 1.1 未提交修改概览

```
36 files changed, 763 insertions(+), 459 deletions(-)
```

分布：
- **新增（untracked，3 项）**：
  - `SovereignTowns/docs/mod-behavior-guide.zh-CN.md`（48.9K，本轮主文档源头）
  - `SovereignTowns/src/Common/PartyEconomyHelper.cs`（264 行，§20 TODO #1 的 WIP 产物）
  - `SovereignTowns/tests/static-regression.ps1`（66 行，22 条 Assert）
- **删除（unstaged，3 项）**：上一轮 audit 工件
  - `SovereignTowns/audits/b1-hygiene-backlog.md`（68 行，8 项 hygiene 待办）
  - `SovereignTowns/audits/round-1-initial.md`（58 行）
  - `SovereignTowns/audits/round-2-final.md`（57 行）
- **修改（33 项）**：见下表（按改动行数排序）

### 1.2 修改文件清单

| 文件 | 改动行数 | 推测改动主题 |
| --- | --- | --- |
| [WebUI/index.html](SovereignTowns/SovereignTowns/WebUI/index.html) | +300 | 面板二级分类、阈值字段联动 |
| [ConfigurationManager.cs](SovereignTowns/src/Configuration/ConfigurationManager.cs) | +66 | 设置覆盖合并、ratio 校验 |
| [GlobalConfig.cs](SovereignTowns/src/Configuration/GlobalConfig.cs) | +67 | 新增 Threshold/Feature 字段 |
| [PartyComponent.cs](SovereignTowns/src/Parties/StPartyComponent.cs) | +51 | 共享 economy 字段（_teamFunds 等） |
| [SallyDispatcher.cs](SovereignTowns/SallyForth/SallyDispatcher.cs) | +53 | 经济迁移到 helper |
| [CapitalLogisticsManager.cs](SovereignTowns/src/Managers/CapitalLogisticsManager.cs) | +43 | — |
| [RecruitmentDispatcher.cs](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs) | +41 | — |
| [PartyLifecycleManager.cs](SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs) | +37 | — |
| [WebConfigGameThreadSync.cs](SovereignTowns/src/WebConfig/WebConfigGameThreadSync.cs) | +35 | — |
| [TroopTransferHelper.cs](SovereignTowns/src/Common/TroopTransferHelper.cs) | +35 | — |
| [TroopUpgradeService.cs](SovereignTowns/src/Upgrades/TroopUpgradeService.cs) | +30 | — |
| [PrisonerRecruitmentManager.cs](SovereignTowns/src/Recruitment/PrisonerRecruitmentManager.cs) | +11 | — |
| [BaseSettlementVisitScheduler.cs](SovereignTowns/src/Coordination/BaseSettlementVisitScheduler.cs) | +26 | — |
| [CapitalRegistry.cs](SovereignTowns/src/Capital/CapitalRegistry.cs) | +25 | — |
| [PatrolDispatcher.cs](SovereignTowns/src/Patrol/PatrolDispatcher.cs) | +24 | 巡逻经济迁移 |
| [STPartySizeLimitModel.cs](SovereignTowns/src/Models/STPartySizeLimitModel.cs) | +23 | — |
| [WebConfigServer.cs](SovereignTowns/src/WebConfig/WebConfigServer.cs) | +21 | — |
| [StPatrolPartyComponent.cs](SovereignTowns/src/Parties/StPatrolPartyComponent.cs) | +18 | — |
| [BattleLootHandler.cs](SovereignTowns/src/Battle/BattleLootHandler.cs) | +15 | — |
| [BattleLootManager.cs](SovereignTowns/src/Battle/BattleLootManager.cs) | +14 | — |
| [StSallyPartyComponent.cs](SovereignTowns/src/Parties/StSallyPartyComponent.cs) | +15 | — |
| [StTransferPartyComponent.cs](SovereignTowns/src/Parties/StTransferPartyComponent.cs) | +11 | — |
| [DecisionAuditLogger.cs](SovereignTowns/src/Audit/DecisionAuditLogger.cs) | +11 | — |
| [Logger.cs](SovereignTowns/src/Logging/Logger.cs) | +11 | — |
| [ClanPatrolScheduler.cs](SovereignTowns/src/Patrol/ClanPatrolScheduler.cs) | +10 | — |
| [SovereignTownsCampaignBehavior.cs](SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs) | +9 | — |
| [STPartyDialogRegistration.cs](SovereignTowns/src/Ui/STPartyDialogRegistration.cs) | +8 | — |
| [TownGarrisonRule.cs](SovereignTowns/src/Configuration/TownGarrisonRule.cs) | +6 | — |
| [STPartySpeedModel.cs](SovereignTowns/src/Models/STPartySpeedModel.cs) | +6 | — |
| [STPartyWageModel.cs](SovereignTowns/src/Models/STPartyWageModel.cs) | +6 | — |
| [ModExpenseLedger.cs](SovereignTowns/src/Economy/ModExpenseLedger.cs) | +5 | — |
| [StRecruiterPartyComponent.cs](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs) | +4 | — |
| [GarrisonXpInjector.cs](SovereignTowns/src/Upgrades/GarrisonXpInjector.cs) | +2 | — |

### 1.3 工作树状态的语义

`PartyEconomyHelper.cs` 的注释明确写道（[L17–L26](SovereignTowns/src/Common/PartyEconomyHelper.cs:17)）：

> 设计原则（与 CLAUDE.md「非作弊基调」对齐）：
>   - Sally / Transfer：短命任务，凭空塞 2-3 天食物，简化复杂度，无队伍资金。
>   - Patrol：终身户外巡逻，从首府所有者扣 2000 第纳尔作启动资金；用资金买食物 + 战利品卖掉补充资金；
>     销毁时余款还首府所有者（自负盈亏）。

这与 doc §20 #1「**统一队伍粮食与自资金逻辑**：[…] 计划提取巡逻队的资金 / 粮食逻辑为可复用组件，由所有 ST 队伍共享」是同一方向，但**实际设计已经分叉**：helper 把 Sally/Transfer 留在"凭空塞食物"路径，只统一了 Patrol 的资金闭环。

→ **这意味着 §20 #1 的"由所有 ST 队伍共享"目标并未完全达成。** Phase 2 必须把这点列为 drift 候选项。

---

## 2. 目录树（深度 ≤ 3）

```
SovereignTowns/                              ← 项目根（mod 工程）
├── .gitignore                28B
├── Directory.Build.props     1.0K
├── SubModule.xml             1.7K
├── _research/                ← 反编译（不允许触碰）
│   ├── GarrisonDoSomething/
│   └── ImprovedGarrisons/
├── docs/                     ← 主文档（untracked）
│   └── mod-behavior-guide.zh-CN.md   48.9K
├── tests/                    ← 静态测试（untracked）
│   └── static-regression.ps1
├── audits/                   ← 本目录刚重建（旧文件被删）
│   └── phase1_inventory.md   ← 本报告
├── SovereignTowns/           ← deploy 镜像目录（git 内合法路径，对应 deploy-to-game 的 layout）
│   └── WebUI/
│       └── index.html        84.9K
└── src/
    ├── SovereignTownsSubModule.cs
    ├── obj/                  ← 构建产物（不允许触碰）
    ├── Audit/                  AuditHelpers / DailyActivityCounters / DecisionAuditLogger / PerSettlementActivityRing
    ├── Battle/                 BattleLootHandler / BattleLootManager
    ├── Campaign/               SovereignTownsCampaignBehavior
    ├── Capital/                CapitalManager / CapitalRegistry
    ├── Common/                 PartyEconomyHelper(new) / PartyNameFormatter / PartyReturnConditionChecker / SafeMoveHelper / TroopTransferHelper
    ├── Configuration/          AiCulturePresets / ConfigurationManager / FoodGuard / GarrisonThresholdMath / GlobalConfig / TownGarrisonRule
    ├── Coordination/           BaseSettlementVisitScheduler
    ├── Economy/                ModExpenseLedger / ModTreasury
    ├── Evaluators/             GenericTroopMatcher / RiskAssessmentService / TroopClassifier / TroopCompositionEvaluator / TroopTemplateMatcher
    ├── Lifecycle/              PartyLifecycleManager / PartyMergeService
    ├── Logging/                Logger
    ├── Managers/               CapitalLogisticsManager
    ├── Models/                 STPartySizeLimitModel / STPartySpeedModel / STPartyWageModel
    ├── Parties/                StPartyComponent(abstract) / StPatrolPartyComponent / StRecruiterPartyComponent / StSallyPartyComponent / StTransferPartyComponent
    ├── Patrol/                 ClanPatrolScheduler / PatrolDispatcher
    ├── Recruitment/            CapitalInPlaceRecruiter / ClanRecruiterScheduler / PrisonerRecruitmentManager / RecruitmentCooldown / RecruitmentDispatcher / RecruitmentPlanner
    ├── SallyForth/             SallyDispatcher
    ├── SaveSystem/             SovereignTownsTypeDefiner
    ├── Settlement/             VanillaSuppressionManager
    ├── Templates/              TroopTemplateModeService
    ├── Transfer/               TransferDispatcher / TransferTask
    ├── Ui/                     DiagnosticGameMenu / STPartyDialogRegistration
    ├── Upgrades/               GarrisonXpInjector / TroopUpgradeService
    └── WebConfig/              SettlementsSnapshot / TroopDumper / WebConfigAuth / WebConfigEndpoints / WebConfigGameThreadSync / WebConfigServer
```

**67 个生产 .cs 文件，共 14,815 行**（不含 obj 自动生成）。最大五个：
1. `ConfigurationManager.cs` — 957 行
2. `PartyLifecycleManager.cs` — 687 行
3. `StRecruiterPartyComponent.cs` — 665 行
4. `SovereignTownsCampaignBehavior.cs` — 564 行
5. `CapitalLogisticsManager.cs` — 557 行

---

## 3. 文档断言表（§0–§19，按文档结构分组）

仅列出"行为/数值/前提条件"类原子断言。"实现位置在哪个文件"的引用不算断言（已在 doc:5–15 给出，本节只针对行为内容）。**Phase 2 将逐条 ✅/⚠️/❌ 核对。**

### §0 基本概念（doc:17–39）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A0.1 | doc:21 | "首府"硬条件 `settlement.IsTown == true`；城堡不能作为首府 |
| A0.2 | doc:23 | 无首府时每日后勤、首府招募、外派征兵、巡逻派遣等首府制行为都会自动停住 |
| A0.3 | doc:27 | "驻军"指 `Town.GarrisonParty.MemberRoster.TotalManCount`，不含民兵 |
| A0.4 | doc:33 | 游戏载入战役后初始化配置/首府/队伍/Web 面板 |
| A0.5 | doc:34 | 每日 tick 做首府级后勤评估（招募/升级/调拨） |
| A0.6 | doc:35 | 每个定居点小时 tick 尝试创建巡逻队 / 主动出击队 |
| A0.7 | doc:36 | 每个定居点每日 tick 兜底四条路径：XP 注入 / 俘虏转化 / 巡逻派遣 / 出击派遣（XP 与俘虏只在首府跑） |
| A0.8 | doc:37 | 每个队伍小时 tick 推进 Mod 自建队伍状态机 |
| A0.9 | doc:38 | 战斗结束：处理巡逻/出击战利品并触发回家判断 |
| A0.10 | doc:39 | 城镇易主/宣战/玩家换氏族：触发迁移/撤退/重建管理器 |

### §1 启动与兼容（doc:41–67）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A1.1 | doc:45–48 | 检测 `ImprovedGarrisons` / `GarrisonDoSomething` → 退化模式（不注册 CampaignBehavior + 主菜单红色警告） |
| A1.2 | doc:52–54 | 注册三个 GameModel：容量 / 速度 / 工资（速度 +20%，工资 0） |
| A1.3 | doc:58 | Web 面板只绑 `127.0.0.1` |
| A1.4 | doc:59 | 默认端口 41763，占用时向后尝试最多 50 个 |
| A1.5 | doc:60–61 | API 与静态页都需 token；token 不写聊天/日志，只在 URL/`auth.txt` |
| A1.6 | doc:65 | 配置写到 `Documents\Mount and Blade II Bannerlord\Configs\SovereignTowns\` |
| A1.7 | doc:67 | 主配置文件名 `global.json` |

### §2 首府系统（doc:69–118）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A2.1 | doc:73 | 玩家氏族永远受管；AI 仅在"AI 城镇纳入 ST 规则"开启时受管 |
| A2.2 | doc:75–80 | 首府初始化顺序：存档残值 → 沿用 → 扫城市 → 随机选 → 否则空 |
| A2.3 | doc:82 | 扫描城市，不含城堡 |
| A2.4 | doc:86 | 玩家可在自家城市菜单看到"主权城镇：设为首府"；城堡菜单不显示 |
| A2.5 | doc:89–93 | 手动设首府 5 个前提（存在 / 是城市 / 属玩家氏族 / Manager 存在 / 不是当前生效首府） |
| A2.6 | doc:96 | 切换成功后，所有该氏族在外 ST 队伍被迁移/解散（非英雄兵并入新首府驻军） |
| A2.7 | doc:100–105 | 首府失守：扫余下城市 → 随机选新首府 → 否则空；在途队伍并入新首府或解散 |
| A2.8 | doc:107–112 | 非首府失守：以失守 settlement 为 home/dest 的队伍处理；调拨/征兵/出击返首府；巡逻不返家，选下一站 |
| A2.9 | doc:114–116 | 无首府氏族获得城市 → 自动成为首府；城堡不会 |

### §3 配置与 Web 面板（doc:120–219）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A3.1 | doc:127–138 | 12 个全局开关及默认值（详见 doc 表，需逐项核） |
| A3.2 | doc:144–164 | 17 个全局驻军规则字段及默认值（详见 doc 表） |
| A3.3 | doc:166–172 | 通用匹配的兵种分类规则（4 类） |
| A3.4 | doc:174–178 | 比例校验：4 类总和 0.9–1.1；模板总和 0.9–1.1；Tier 1–6 且 max≥min |
| A3.5 | doc:182–187 | 两个"首府专属开关"（俘虏转化 / 自动升级），只读首府规则、对城堡同名字段无效 |
| A3.6 | doc:191–198 | 单领地覆盖只覆盖 6 字段（目标驻军/最少防守/招募预算/威胁乘数×2/食物阈值） |
| A3.7 | doc:200 | 兵种模板/Tier/文化/优先/禁用 不被单领地覆盖 |
| A3.8 | doc:205–210 | Web UI 合并"数量预算"与"资源调度"为单 tab，含 5 个二级分类 |
| A3.9 | doc:212 | `PUT /api/config` 不直接碰游戏对象，排队到主线程 tick |
| A3.10 | doc:214–219 | 配置变化后的副作用：AI 接管同步 / vanilla 抑制重应用 / 在途征兵切 Dispatching |

### §4 风险评估（doc:221–244）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A4.1 | doc:225–235 | 风险判定 8 步流程（null/非激活/围攻/ratio 阶梯） |
| A4.2 | doc:237 | 非围攻最高只到 High；Critical 只来自围城 |
| A4.3 | doc:241 | High/Critical 用 WartimeMultiplier |
| A4.4 | doc:242 | 调拨源 High 排除 |
| A4.5 | doc:243 | 征兵队路上目标 High 放弃重选 |
| A4.6 | doc:244 | 调拨目的地 Critical 改返源 |

### §5 每日首府后勤（doc:246–312）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A5.1 | doc:250–256 | 跳过氏族的 4 个条件 |
| A5.2 | doc:259–266 | 流程 8 步固定顺序 |
| A5.3 | doc:272 | `DesiredTarget = round(TargetTotalCount × multiplier)`，最低 1 |
| A5.4 | doc:281–284 | 在途兵员计算口径（outbound/inbound/源回返） |
| A5.5 | doc:288 | `ProjectedMen = CurrentMen + Inbound`，不扣 outbound |
| A5.6 | doc:294 | `Demand = max(0, DesiredTarget - ProjectedMen)` |
| A5.7 | doc:298–304 | CriticalThreshold/CriticalDemand 公式；默认 `TransferCriticalProjectedRatio=0.24`，比例>0 时阈值至少 1 |
| A5.8 | doc:308 | `TransferCapacity = max(0, CurrentMen - DesiredTarget)` |
| A5.9 | doc:312 | `Priority = (Critical?1000:0) + RiskLevel×100 + Demand + CriticalDemand×2` |

### §6 首府原地招募（doc:314–361）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A6.1 | doc:316 | 不创建队伍，直接从首府 notable 志愿兵槽招进驻军 |
| A6.2 | doc:319–330 | 11 个触发条件（首府/缺口/开关/城市/受管/不围攻/Garrison/未满/食物/Leader/VolunteerModel） |
| A6.3 | doc:334–339 | 目标库存公式：`首府目标 + min(branchDemand, TargetTotalCount)` |
| A6.4 | doc:341–352 | 逐 notable 扫描 10 步逻辑（含玩家 5 第纳尔扣费、AI 免费） |
| A6.5 | doc:345 | 原地招募只扫 vanilla 允许槽位，不做 2 倍扩展 |
| A6.6 | doc:354–361 | Role 配额 = round(ratio × cap)；已满则跳该 role |

### §7 外派征兵队（doc:363–559）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A7.1 | doc:367–371 | 派单条件公式（remainingDemand / threshold / criticalDemand） |
| A7.2 | doc:373 | 默认 `RecruitmentMinDemandRatio = 0.07` |
| A7.3 | doc:388–402 | 派出 13 个前提条件 |
| A7.4 | doc:406–409 | 征兵队上限 = settlement_garrison 等级 + 1；找不到建筑取 1；0 级=1，3 级=4 |
| A7.5 | doc:417 | 护卫 = `round(首府驻军 × RecruiterEscortRatio)`，默认 0.10 |
| A7.6 | doc:422–426 | 抽兵规则（首府/低 Tier 优先/不抽英雄/护卫 0 不派/抽不到不派） |
| A7.7 | doc:429 | 玩家氏族扣 `RecruiterSeedGold` 默认 1000；AI 不扣 |
| A7.8 | doc:431 | 扣费后队伍创建失败 → 兵+金全退 |
| A7.9 | doc:433–441 | 创建后属性：名称/攻击性 0/避战/禁 vanilla AI/记出发兵数/3 天食物/进 LifecycleManager |
| A7.10 | doc:445 | 候选数量默认 `RecruitmentCandidateBatchSize = 8`，无距离要求 |
| A7.11 | doc:447–451 | 候选来源三类（首府附属村 / 同氏族城市村 / 友军/中立第三方村） |
| A7.12 | doc:454–462 | 候选必须满足 8 条 |
| A7.13 | doc:464 | 村庄冷却默认 72 小时，无论是否实际招到都打 |
| A7.14 | doc:468 | `priority = 10×min(slots,6) - 0.5×distance - 5×threat` |
| A7.15 | doc:476–479 | 4 个状态：Dispatching / Travelling / AtVillage / Returning |
| A7.16 | doc:486–489 | Dispatching 逻辑 4 步 |
| A7.17 | doc:492–497 | Travelling 逻辑 5 项触发 |
| A7.18 | doc:494 | `RecruiterReturnRecruitedCount = 50` |
| A7.19 | doc:500–504 | 村庄失效 4 条件 |
| A7.20 | doc:506–516 | AtVillage 逻辑 9 步 |
| A7.21 | doc:520 | Returning：基类回 home 解散 |
| A7.22 | doc:524–546 | 村庄实际招募 7 大块、外派槽位 2 倍扩展、单兵 5 第纳尔 |
| A7.23 | doc:548–557 | 通用 vs 模板模式的评分规则 |

### §8 兵力调拨队（doc:561–667）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A8.1 | doc:566–570 | 目的地必须 Demand>0 且不围攻；按 Priority/距离排序 |
| A8.2 | doc:580–589 | 调拨源必须满足 8 条 |
| A8.3 | doc:594 | `score = distance - min(capacity, maxPerTask) × TransferCapacityWeight`，默认 0.05 |
| A8.4 | doc:598–601 | branch-to-branch 减分（默认 25），首府-to-branch 加分（默认 10） |
| A8.5 | doc:604 | ⚠️ 文档自承"字段名叫 BranchToBranchPenalty 但代码是减分"——这是一个已记录的命名/语义反差 |
| A8.6 | doc:609–612 | 单次人数公式：min(maxPerTask, demand, capacity, byRatio)；默认 0.67 / 0.30 |
| A8.7 | doc:616–618 | 小额调拨阈值默认 0.13；非危急时 amount < 阈值则放弃 |
| A8.8 | doc:622–636 | 创建 13 个前提条件；每源城调拨队上限固定 2 支 |
| A8.9 | doc:642–644 | 抽兵规则（源/低 Tier/不抽英雄） |
| A8.10 | doc:648–653 | 创建后属性（名称/攻击性 0/避战/禁 vanilla AI/3 天食物/不走战后返航规则） |
| A8.11 | doc:658–667 | 在路上 5 项检查（停目的地并入 / 目的地易主走 fallback / Critical 改返源） |

### §9 巡逻队（doc:669–830）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A9.1 | doc:671 | 只从首府创建，但巡逻范围是整个氏族 settlement |
| A9.2 | doc:675–688 | 创建 13 个前提条件 |
| A9.3 | doc:692–696 | 上限 = settlement_garrison + 1，0 级=1，3 级=4 |
| A9.4 | doc:700 | `batchSize = round(首府驻军 × PatrolTroopBatchRatio)`，默认 0.10，最低 1 |
| A9.5 | doc:706–710 | reserveAfterCreation 默认 0.80，`首府驻军 - batchSize ≥ reserveAfterCreation` |
| A9.6 | doc:719 | 启动资金固定 2000 第纳尔（不在面板，§18 §19 同步） |
| A9.7 | doc:721–730 | 玩家先检查再扣，AI 按可用金额扣 |
| A9.8 | doc:731–736 | 创建后立即买约 3 天粮（最便宜食物 → SellItemsAction → 扣资金） |
| A9.9 | doc:738–742 | 之后停在 settlement 时：卖非食物 → 资金 → 食物<1天再买 3 天 |
| A9.10 | doc:744–746 | 食物天数：FoodChange≥0 视为无风险；否则 `Food/-FoodChange` |
| A9.11 | doc:753–759 | 候选必须 5 条（属该氏族 / 不围攻 / 村庄未洗劫 / 未预占 / 不小于回访间隔） |
| A9.12 | doc:763 | `score = -hoursSinceVisit + DistanceWeight × distance`，越小越优先 |
| A9.13 | doc:767 | 未访问视为 1,000,000 小时（强优先） |
| A9.14 | doc:771 | `bookHours = max(0.5, ETA + EtaBufferHours)`，默认 1 小时 |
| A9.15 | doc:775 | 创建时找不到非 home 候选 → 兵还首府 + 解散 |
| A9.16 | doc:781–790 | 每小时 8 步检查 |
| A9.17 | doc:796–797 | 首府被围攻：兵并入首府驻军并解散 |
| A9.18 | doc:801–805 | 非首府被围攻：最近被围 → SetMoveDefendSettlement + AI initiative 0.3/0.7/4h |
| A9.19 | doc:807–811 | 村庄被劫掠：最近 → vanilla 支援 + initiative 同上 |
| A9.20 | doc:815–819 | 支援出击 ETA 阈值默认 2 小时 |
| A9.21 | doc:823–830 | 卡死：12h 算卡死；移动>1.0 恢复；24h 瞬移回 GatePosition |

### §10 主动出击队（doc:832–952）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A10.1 | doc:834 | 从任意受管城/堡创建，但氏族必须有可用首府 |
| A10.2 | doc:840–853 | 创建 14 个前提 |
| A10.3 | doc:855 | 出击队上限固定每城 1 支 |
| A10.4 | doc:859 | 默认 `SallyDetectionRadius = 50`；被劫掠村庄不设半径 |
| A10.5 | doc:861–868 | 候选敌军 4 条件；优先支援被劫村 → 选健康兵力最少的敌方队伍 |
| A10.6 | doc:872 | `healthy ≈ TotalManCount - TotalWounded` |
| A10.7 | doc:876–887 | 持续可见计数（默认 3）+ 冷却（默认 24h） |
| A10.8 | doc:894–898 | 出击人数公式（minDef/extractable/byGarrisonRatio/byTarget/sallySize） |
| A10.9 | doc:900–907 | 默认值与 `sallySize < SallyCreateMinPartyCount` 则不创建 |
| A10.10 | doc:910–913 | 抽兵：本城 / 高 Tier 优先 / 不抽英雄 |
| A10.11 | doc:915 | `SallySeedGold = 100`，玩家扣失败不派；创建失败/抽兵不足退款 |
| A10.12 | doc:919–924 | 创建后属性：攻击 0 / 不避战 / 禁加入玩家战斗 / 3 天免食 / `SetMoveEngageParty` |
| A10.13 | doc:927–947 | Engaging/Returning 两阶段；战斗结束直接 Returning |
| A10.14 | doc:949–952 | 被销毁时残兵救援回 home → 首府 → SallyDispatcher 冷却 |

### §11 战利品、俘虏、金币（doc:954–1012）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A11.1 | doc:956 | 战利品处理只针对 ST 巡逻队和 ST 出击队 |
| A11.2 | doc:960–961 | 触发时机：战斗结束 + 回家解散前兜底 |
| A11.3 | doc:965–968 | 符合条件 3 条 |
| A11.4 | doc:971–982 | 匹配俘虏招入首府 10 步逻辑 |
| A11.5 | doc:986–991 | 非匹配俘虏卖最近本氏族城市（只扫城市） |
| A11.6 | doc:996–1001 | 装备物品卖最近本氏族城市 + fallback |
| A11.7 | doc:1003 | 巡逻队普通战利品出售另一套（资金路线，非玩家钱包） |
| A11.8 | doc:1007–1011 | 队伍金币回流：首府 Clan Leader → MainHero → 清零 |

### §12 首府每日俘虏转化（doc:1013–1049）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A12.1 | doc:1015–1021 | 只在首府运行；首府只能是城市；城堡不走 |
| A12.2 | doc:1023–1032 | 前提 8 条 |
| A12.3 | doc:1034–1039 | conformity = (level+1)×5；level 钳制 0–3；找不到取 5 |
| A12.4 | doc:1041–1049 | 遍历俘虏 7 步 |

### §13 驻军 XP 与自动升级（doc:1051–1109）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A13.1 | doc:1053 | XP 注入只在首府；首府只能是城市，城堡不走 |
| A13.2 | doc:1055–1063 | XP 注入前提 7 条 |
| A13.3 | doc:1065–1070 | baseXp = (level+1)×10；level 钳制 0–3；找不到取 10 |
| A13.4 | doc:1072–1077 | 额外 XP：townBonus = round(baseXp × multiplier)；按首府所有者城/堡数额外乘算（城 1.5、堡 0.5） |
| A13.5 | doc:1079–1086 | 对每个驻军元素 5 步：跳空/英雄/<=0/Tier>Max/否则注入 |
| A13.6 | doc:1090–1107 | TroopUpgradeService 升级 11 步规则 |
| A13.7 | doc:1093 | `AutoUpgradeMinTierRatio = 0.30` |
| A13.8 | doc:1094 | `升级预算 = max(BudgetLimit/4, AutoUpgradeMinBudget)`，默认最低 500 |
| A13.9 | doc:1095 | `AutoUpgradeMaxPerCall = 20` |

### §14 所有 ST 队伍的通用行为（doc:1111–1190）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A14.1 | doc:1120–1135 | 通用小时 tick 12 项前置检查 |
| A14.2 | doc:1137 | 到家只看 `CurrentSettlement == home`，不再用 LastVisitedSettlement |
| A14.3 | doc:1145–1148 | 战后返航：兵力<0.5×出发 或 伤兵比>0.3 |
| A14.4 | doc:1150 | 调拨队不走战后返航规则 |
| A14.5 | doc:1156–1158 | 进展定义：TargetSettlement 变 / 成员数变 |
| A14.6 | doc:1163 | MapEvent / BesiegedSettlement 跳过空闲检测 |
| A14.7 | doc:1167–1168 | Idle 24h 强制回家；36h 解散 |
| A14.8 | doc:1174 | `PartyPrisonerCap = 30`；0 关闭 |
| A14.9 | doc:1182 | 速度：vanilla 最终速度 +20% |
| A14.10 | doc:1183 | 工资：0，不扣家族军饷 |
| A14.11 | doc:1187–1190 | 4 种队伍各自的容量公式 |

### §15 vanilla 自动招募抑制（doc:1192–1214）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A15.1 | doc:1194–1200 | 抑制满足 5 条件 |
| A15.2 | doc:1203–1207 | 4 项副作用（不抓志愿 / 不升级 / notable 刷新不受影响 / 民兵不受影响） |
| A15.3 | doc:1209 | 关闭抑制或关闭自动招募 → 恢复 vanilla flag 为 true |
| A15.4 | doc:1213–1214 | 易主：进入禁用 / 离开恢复 |

### §16 AI 氏族接管（doc:1216–1233）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A16.1 | doc:1218 | 默认关闭 |
| A16.2 | doc:1221–1226 | 开启后 5 项行为 |
| A16.3 | doc:1228–1233 | 关闭时 4 步降级（合并 → 任意自有 → 解散 → 移除 manager） |

### §17 队伍交互（doc:1235–1243）

| # | 文档位置 | 断言 |
| --- | --- | --- |
| A17.1 | doc:1237–1242 | 4 种 ST 队伍都被对话拦截，玩家只有"祝你顺利"选项 |
| A17.2 | doc:1243 | vanilla 自动刷的巡逻不走拦截 |

### §18 主要可配置字段索引（doc:1245–1307）

3 张大表共 ~30 字段。**默认值在 Phase 2 应全部核对**：仅当 default 与 `GlobalConfig.cs` 不一致才算 drift。

### §19 行为边界（doc:1325–1336）

8 条已知边界声明，**应作为 Phase 2 验证项**：
- A19.1 doc:1329 首府只能是城市
- A19.2 doc:1330 城堡参与后勤但不当首府
- A19.3 doc:1331 XP/俘虏只在首府
- A19.4 doc:1332 巡逻 home 不当普通站
- A19.5 doc:1333 调拨 ProjectedMen 不扣 outbound
- A19.6 doc:1334 巡逻 2000 启动资金固定值不在面板
- A19.7 doc:1335 征兵/调拨避战；出击/巡逻不避战
- A19.8 doc:1336 vanilla 抑制不影响民兵和 notable 刷新

---

## 4. REFACTOR_TODO 表（§20，doc:1338–1344）

| TODO | 项 | 描述 | 当前状态 | 验收标准（推定） |
| --- | --- | --- | --- | --- |
| T1 | 统一队伍粮食与自资金逻辑 | 提取巡逻队"启动资金 + 卖战利品 + 买粮"闭环为可复用组件，**由所有 ST 队伍共享** | **WIP**：[PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs) 已创建，但内部注释明确说明 Sally/Transfer 仍走"凭空塞食物"，**未真正共享** | 需明确：① 所有 4 类 ST 队伍调用同一组 helper 方法；② 不再有"凭空塞"路径，全部走资金—购入；③ 文档 §11 改为"已废弃" |
| T2 | 战利品集中处理逻辑废弃 | §11 集中战利品处理是过渡方案；T1 完成后**所有 ST 队伍**走 patrol 风格 → §11 流程移除 | **依赖 T1**；当前 [BattleLootHandler.cs](SovereignTowns/src/Battle/BattleLootHandler.cs)（478 行）、[BattleLootManager.cs](SovereignTowns/src/Battle/BattleLootManager.cs)（105 行）仍在工作 | T1 完成后删除 BattleLoot* 文件 + 相关订阅；§11 整章从 doc 移除（或改"deprecated"） |

⚠️ **T1 验收标准是我的推定**，doc 未明写。Phase 2 前请你裁决：
- 选项 A：严格按 doc §20 的字面"由所有 ST 队伍共享" → 当前 helper 设计**不达标**（保留 Sally/Transfer 凭空塞路径）
- 选项 B：当前 helper 的"巡逻自资金 + 其他凭空"设计是新决策（因 helper 注释已明确这是"非作弊基调"），doc §20 需要被修订
- 选项 C：再问一遍 — 我不确定 ① 还是 ②

---

## 5. 旧 backlog（已删除 `b1-hygiene-backlog.md`，8 项 P1/P2/P3）

来自 git HEAD 仍可读的内容，按是否仍未处理列出：

| ID | 优先级 | 项 | 文件:行 | 状态推测 |
| --- | --- | --- | --- | --- |
| B1.1 | P1 | PatrolDispatcher `garrison!` null-forgiving 加显式保护 | [PatrolDispatcher.cs:182](SovereignTowns/src/Patrol/PatrolDispatcher.cs:182), :189 | **待 Phase 2 核** |
| B1.2 | P1 | 玩家氏族巡逻退款 home==null 容错（推荐加 `_seedChargedToPlayer` SaveableField） | [StPatrolPartyComponent.cs:192](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:192) | **待 Phase 2 核** |
| B1.3 | P2 | 招募经济常量暴露面板（`VolunteerMul=2.0` / `CostDiscount=0.5` / `DefaultGoldPerRecruit=10`） | [StRecruiterPartyComponent.cs:40-43](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:40) | **待核** |
| B1.4 | P2 | 巡逻经济常量暴露面板（patrolSeedGold=2000、买粮天数 3、补粮触发 1、initiative 0.3/0.7、卡死距离 1.0） | [PatrolDispatcher.cs:174,202](SovereignTowns/src/Patrol/PatrolDispatcher.cs:174) [StPatrolPartyComponent.cs:39,51,274](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:39) | **待核** |
| B1.5 | P2 | 出击队 `MaxSallyHours=12` 与 SallyDispatcher 免费粮 `3f` | [StSallyPartyComponent.cs:30](SovereignTowns/src/Parties/StSallyPartyComponent.cs:30) [SallyDispatcher.cs:303](SovereignTowns/src/SallyForth/SallyDispatcher.cs:303) | **待核** |
| B1.6 | P3 | `ConfigsAreEqual` JSON diff 行为（保守返回 false） | [ConfigurationManager.cs:213](SovereignTowns/src/Configuration/ConfigurationManager.cs:213) | 设计意图，仅记录 |
| B1.7 | P3 | `OnConfigChanged` 字段级 diff 决定 recruiter 重置粒度 | OnConfigChangedHandler | 改进方向，工作量中等 |
| B1.8 | P3 | `RecruitmentCooldown` 不入存档 | — | 产品取舍，非 bug |

❓ **裁决问题**：本轮 audit 是否要把这 8 项纳入 `<重构待办>`？建议：B1.1/B1.2（P1）必须纳入；B1.3–1.5（P2 硬编码）按"如果 T1/T2 路径上恰好碰到就顺手做"；B1.6–1.8（P3）排除。

---

## 6. 测试与静态检查基础设施

### 6.1 编译验证

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

**期望**：编译通过、DeployToGame 目标把 DLL/PDB + WebUI 拷贝到 Bannerlord 安装目录（`Directory.Build.props` 默认 `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`）。

### 6.2 静态回归测试

```powershell
pwsh SovereignTowns\tests\static-regression.ps1
```

含 **22 条 Assert**，验证以下不变量：

| 检测项 | 位置 |
| --- | --- |
| `AutoGarrison` 已从 EnabledFeatures 移除 | GlobalConfig / Logistics / Behavior / GarrisonXp / WebUI 5 处 |
| `AutoGarrison` 已被 `AutoRecruitment` 取代（GarrisonXp） | GarrisonXpInjector |
| WebUI 不再有"无巡逻队"措辞 / 出击不依赖 KindPatrol | webUi / sally |
| 出击目标丢失返家而非释放 vanilla AI | sallyComponent "target lost, returning home" |
| 巡逻退款用 `ActualClan ?? Home.OwnerClan` | patrolComponent |
| 战利品只处理 Winner 一侧 | battleLoot "mapEvent.Winner" / 不出现 ProcessSide(Attacker/Defender) |
| 单领地 override 通过 `BuildBaseRuleFor` + `ApplySettlementOverrideFields` | configurationManager |
| Transfer 不用 `LastVisitedSettlement == dest/fallback` | transferComponent |
| Exact 模板 ratio 总和校验 | configurationManager |
| WebUI 合并"数量预算 / 资源调度"为 `activeSettingsGroup` + `settingsGroups` | webUi |
| `AvoidRaidedVillages` 在面板 | webUi |

### 6.3 运行时验证

按 [CLAUDE.md](CLAUDE.md) "There are no unit tests. Verification = launch the game"。Phase 5 需要标注"运行时验证未做，待你在游戏中手测"。

---

## 7. 代码定位映射（按 doc 主目录区域）

按 doc:5–15 给出的 mapping，结合 §1 文件清单：

| 主题 | 文档位置 | 主要文件 | LOC |
| --- | --- | --- | --- |
| 入口 / 事件分发 | §0–§1 | [SovereignTownsSubModule.cs](SovereignTowns/src/SovereignTownsSubModule.cs)（196）, [SovereignTownsCampaignBehavior.cs](SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs)（564） | 760 |
| 首府 | §2 | [CapitalManager.cs](SovereignTowns/src/Capital/CapitalManager.cs)（348）, [CapitalRegistry.cs](SovereignTowns/src/Capital/CapitalRegistry.cs)（440） | 788 |
| 每日后勤 | §5 §6 §8 | [CapitalLogisticsManager.cs](SovereignTowns/src/Managers/CapitalLogisticsManager.cs)（557）, [CapitalInPlaceRecruiter.cs](SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs)（216） | 773 |
| 征兵 | §7 | [StRecruiterPartyComponent.cs](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs)（665）, [RecruitmentDispatcher.cs](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs)（265）, [RecruitmentPlanner.cs](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs)（293）, [ClanRecruiterScheduler.cs](SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs)（77）, [RecruitmentCooldown.cs](SovereignTowns/src/Recruitment/RecruitmentCooldown.cs)（61）, [PrisonerRecruitmentManager.cs](SovereignTowns/src/Recruitment/PrisonerRecruitmentManager.cs)（282） | 1643 |
| 调拨 | §8 | [TransferDispatcher.cs](SovereignTowns/src/Transfer/TransferDispatcher.cs)（128）, [TransferTask.cs](SovereignTowns/src/Transfer/TransferTask.cs)（34）, [StTransferPartyComponent.cs](SovereignTowns/src/Parties/StTransferPartyComponent.cs)（223） | 385 |
| 巡逻 | §9 | [PatrolDispatcher.cs](SovereignTowns/src/Patrol/PatrolDispatcher.cs)（345）, [ClanPatrolScheduler.cs](SovereignTowns/src/Patrol/ClanPatrolScheduler.cs)（150）, [StPatrolPartyComponent.cs](SovereignTowns/src/Parties/StPatrolPartyComponent.cs)（510） | 1005 |
| 主动出击 | §10 | [SallyDispatcher.cs](SovereignTowns/src/SallyForth/SallyDispatcher.cs)（332）, [StSallyPartyComponent.cs](SovereignTowns/src/Parties/StSallyPartyComponent.cs)（256） | 588 |
| 战利品/俘虏 | §11 §12 | [BattleLootHandler.cs](SovereignTowns/src/Battle/BattleLootHandler.cs)（478）, [BattleLootManager.cs](SovereignTowns/src/Battle/BattleLootManager.cs)（105）, [PrisonerRecruitmentManager.cs](SovereignTowns/src/Recruitment/PrisonerRecruitmentManager.cs)（282） | 865 |
| XP/升级 | §13 | [GarrisonXpInjector.cs](SovereignTowns/src/Upgrades/GarrisonXpInjector.cs)（215）, [TroopUpgradeService.cs](SovereignTowns/src/Upgrades/TroopUpgradeService.cs)（285） | 500 |
| 通用基类/生命周期 | §14 | [StPartyComponent.cs](SovereignTowns/src/Parties/StPartyComponent.cs)（402）, [PartyLifecycleManager.cs](SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs)（687）, [PartyMergeService.cs](SovereignTowns/src/Lifecycle/PartyMergeService.cs)（198） | 1287 |
| vanilla 抑制 | §15 | [VanillaSuppressionManager.cs](SovereignTowns/src/Settlement/VanillaSuppressionManager.cs)（288） | 288 |
| 配置/Web 面板 | §3 | [GlobalConfig.cs](SovereignTowns/src/Configuration/GlobalConfig.cs)（295）, [ConfigurationManager.cs](SovereignTowns/src/Configuration/ConfigurationManager.cs)（957）, [TownGarrisonRule.cs](SovereignTowns/src/Configuration/TownGarrisonRule.cs)（118）, [WebConfigServer.cs](SovereignTowns/src/WebConfig/WebConfigServer.cs)（407）, [WebConfigEndpoints.cs](SovereignTowns/src/WebConfig/WebConfigEndpoints.cs)（301）, [WebConfigGameThreadSync.cs](SovereignTowns/src/WebConfig/WebConfigGameThreadSync.cs)（58）, [WebConfigAuth.cs](SovereignTowns/src/WebConfig/WebConfigAuth.cs)（84）, [TroopDumper.cs](SovereignTowns/src/WebConfig/TroopDumper.cs)（218）, [SettlementsSnapshot.cs](SovereignTowns/src/WebConfig/SettlementsSnapshot.cs)（100）, [WebUI/index.html](SovereignTowns/SovereignTowns/WebUI/index.html)（84.9K） | 2538 + html |
| 经济（hero/clan 扣费/退款） | §6 §7 §9 §10 §13 | [ModExpenseLedger.cs](SovereignTowns/src/Economy/ModExpenseLedger.cs)（191）, [ModTreasury.cs](SovereignTowns/src/Economy/ModTreasury.cs)（160）, [PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs)（264 NEW） | 615 |
| 共享工具 | 多处 | [TroopTransferHelper.cs](SovereignTowns/src/Common/TroopTransferHelper.cs)（159）, [SafeMoveHelper.cs](SovereignTowns/src/Common/SafeMoveHelper.cs)（41）, [PartyNameFormatter.cs](SovereignTowns/src/Common/PartyNameFormatter.cs)（32）, [PartyReturnConditionChecker.cs](SovereignTowns/src/Common/PartyReturnConditionChecker.cs)（81）, [FoodGuard.cs](SovereignTowns/src/Configuration/FoodGuard.cs)（54）, [AuditHelpers.cs](SovereignTowns/src/Audit/AuditHelpers.cs)（17） | 384 |
| Audit/日志 | — | [Logger.cs](SovereignTowns/src/Logging/Logger.cs)（151）, [DecisionAuditLogger.cs](SovereignTowns/src/Audit/DecisionAuditLogger.cs)（302）, [DailyActivityCounters.cs](SovereignTowns/src/Audit/DailyActivityCounters.cs)（43）, [PerSettlementActivityRing.cs](SovereignTowns/src/Audit/PerSettlementActivityRing.cs)（59） | 555 |
| 风险评估 | §4 | [RiskAssessmentService.cs](SovereignTowns/src/Evaluators/RiskAssessmentService.cs)（103） | 103 |
| 通用匹配/模板 | §3 §6 §7 | [TroopClassifier.cs](SovereignTowns/src/Evaluators/TroopClassifier.cs)（63）, [GenericTroopMatcher.cs](SovereignTowns/src/Evaluators/GenericTroopMatcher.cs)（289）, [TroopTemplateMatcher.cs](SovereignTowns/src/Evaluators/TroopTemplateMatcher.cs)（274）, [TroopCompositionEvaluator.cs](SovereignTowns/src/Evaluators/TroopCompositionEvaluator.cs)（194）, [TroopTemplateModeService.cs](SovereignTowns/src/Templates/TroopTemplateModeService.cs)（121） | 941 |
| Save 系统 | — | [SovereignTownsTypeDefiner.cs](SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs)（59） | 59 |
| UI 菜单/对话 | §2 §17 | [DiagnosticGameMenu.cs](SovereignTowns/src/Ui/DiagnosticGameMenu.cs)（300）, [STPartyDialogRegistration.cs](SovereignTowns/src/Ui/STPartyDialogRegistration.cs)（125） | 425 |
| GameModel | §1 §14 | [STPartySizeLimitModel.cs](SovereignTowns/src/Models/STPartySizeLimitModel.cs)（128）, [STPartySpeedModel.cs](SovereignTowns/src/Models/STPartySpeedModel.cs)（59）, [STPartyWageModel.cs](SovereignTowns/src/Models/STPartyWageModel.cs)（59） | 246 |
| Coordination | §9 | [BaseSettlementVisitScheduler.cs](SovereignTowns/src/Coordination/BaseSettlementVisitScheduler.cs)（243） | 243 |
| AI 文化预设 | §16 | [AiCulturePresets.cs](SovereignTowns/src/Configuration/AiCulturePresets.cs)（71） | 71 |

---

## 8. 健壮性维度初查（Phase 5 才会展开）

仅记录三条需 Phase 5 重点关注的现象，不是结论：

1. **异常处理风格不统一**：`PartyEconomyHelper.cs` 大量 `try { … } catch { swallow }`，与 CLAUDE.md 硬约束 #5「Every event handler entry point wraps its body in try/catch」并不冲突，但内部辅助函数也广泛 swallow 异常（如 [L68](SovereignTowns/src/Common/PartyEconomyHelper.cs:68) `try abs = Math.Abs(party.FoodChange); catch { abs = 0f; }`）属于"防御过度"嫌疑。Phase 5 看是否值得收紧。
2. **WIP 状态下编译性未验**：本阶段不动代码，但应在 Phase 2 前先跑一次 `dotnet build` 与 `static-regression.ps1`，否则 Phase 3 "改之前测之后必须都跑通"无 baseline。
3. **save/serialization 风险**：[StPartyComponent.cs](SovereignTowns/src/Parties/StPartyComponent.cs) 改了 +51 行，且 PartyEconomyHelper 引入了 `_teamFunds` 概念，可能涉及新的 SaveableField。CLAUDE.md §3 invariant 明确「LocalSaveId 不可重排」。Phase 2 需重点核 ID 是否安全。

---

## 9. 需要你裁决的问题

| Q | 问题 | 选项 |
| --- | --- | --- |
| Q1 | `<重构待办>` 的范围 | (a) 仅 doc §20 两项；(b) §20 + b1-hygiene-backlog P1（共 4 项）；(c) §20 + 整个 backlog（10 项） |
| Q2 | T1（统一粮食/资金）的验收标准 | (a) 严格按 doc §20 文字"由所有 ST 队伍共享"，要求 helper 真正统一所有 4 类；(b) 接受当前 helper 的"Patrol 自资金+其他凭空"分叉设计，并把 doc §20 改写为该分叉口径；(c) 其他 |
| Q3 | 工作树 36 个未提交修改是否视为 baseline | (a) 是，从这里开始 Phase 2；(b) 否，先 `git restore .` 回到 09f69a0 再开始；(c) 让我先看 diff 再决定 |
| Q4 | 测试/静态检查命令 | 确认采用：① `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`；② `pwsh SovereignTowns\tests\static-regression.ps1` |
| Q5 | 不允许触碰路径 | 确认排除：① `SovereignTowns/_research/`；② 任意 `obj/` `bin/`；其他要加吗（如 `SubModule.xml`、`Directory.Build.props`）？ |
| Q6 | 是否在 Phase 2 之前先跑 build + static-regression，确立"已知红/绿" baseline | (a) 是，跑了再开始 Phase 2；(b) 跳过，直接进 Phase 2 文档逐条核对 |
| Q7 | audits/ 目录已重建，phase1_inventory.md 已写入 — 是否提交到 git？ | (a) 不提交，本轮全部完成后一并 commit；(b) 现在就 commit；(c) 加入 .gitignore |

---

## 10. 下一步建议

**建议你裁决 Q1–Q7 后**，我执行 Phase 2 时按如下顺序：

1. 先按 Q4 跑 build + static-regression 把红绿 baseline 锁定（如 Q6 选 a）。
2. 按 doc §0 → §19 顺序对照源码，对每条 A* 断言打 ✅/⚠️/❌/❓ 标签。
3. 对 ⚠️/❌ 项给出文件:行号证据 + 建议方向（改 doc / 改代码）。
4. 对 ❓ 项（含本轮 Q1/Q2 的剩余歧义）列入 phase2_drift.md 末尾的"等你裁决"。
5. **不会修改任何文件**。

预期 phase2_drift.md 体量：50–80 个 ⚠️/❌ 项（基于 doc 与 helper 的设计分叉、§18/§19 的硬编码声明，以及 b1-hygiene-backlog 残留项）。

— Phase 1 报告完
