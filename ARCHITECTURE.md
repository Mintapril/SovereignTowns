# 第三阶段：架构设计 — Sovereign Towns

> 版本：v0.1（第三阶段产出）
> 基础：`FEASIBILITY_REPORT.md`、`RESEARCH_FINDINGS.md`、`MOD_SURVEY.md`、`UNCERTAINTY_LOG.md`
> 命名空间根：`SovereignTowns.*`
> **本文档不写实现代码**，只描述每个模块的职责 / 依赖 / Tick 生命周期 / 数据流 / 存档行为。

---

## 0. 总体架构（5 层）

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Layer 5 — UI / 集成                                                         │
│   ├── DebugCommandSystem         (开发期调试命令)                            │
│   ├── MCMIntegration             (MCM 软依赖, MVP 5+)                       │
│   └── GameMenuIntegration        (城镇内菜单按钮)                            │
└────────────────────────────────────────────────────────────────────────────┘
                                    ▲
┌────────────────────────────────────────────────────────────────────────────┐
│ Layer 4 — LLM (MVP 5.5+, 可选)                                              │
│   ├── LLMProviderInterface       (Local / Remote / NoOp 抽象)               │
│   ├── LLMReasoningService        (调用 LLM + 异步)                          │
│   ├── LLMDecisionValidator       (校验 LLM 输出)                            │
│   ├── RuleBasedFallbackDecisionMaker (规则引擎兜底)                          │
│   └── DecisionAuditLogger        (审计 LLM 与规则的所有决策)                  │
└────────────────────────────────────────────────────────────────────────────┘
                                    ▲
┌────────────────────────────────────────────────────────────────────────────┐
│ Layer 3 — 业务 Manager                                                      │
│   ├── TownGarrisonManager        (功能一: 自动驻军规则执行)                   │
│   ├── RecruitmentManager         (功能二: 自动征兵队)                        │
│   ├── PatrolManager              (功能三: 巡逻 / 防御反应)                    │
│   ├── CastleSupportManager       (功能四: 城堡 ↔ 城镇间接关系)                │
│   ├── GarrisonTransferManager    (功能四: 调拨队执行)                         │
│   └── PartyLifecycleManager      (所有本 Mod 队伍的生命周期 / 数量上限)        │
└────────────────────────────────────────────────────────────────────────────┘
                                    ▲
┌────────────────────────────────────────────────────────────────────────────┐
│ Layer 2 — 评估器 / 服务（无状态计算）                                          │
│   ├── RiskAssessmentService      (settlement 风险评估)                       │
│   ├── TroopCompositionEvaluator  (兵种比例 / 文化 / 质量评估)                  │
│   └── SettlementDefenseDemandEvaluator (城镇是否需要派兵防御)                  │
└────────────────────────────────────────────────────────────────────────────┘
                                    ▲
┌────────────────────────────────────────────────────────────────────────────┐
│ Layer 1 — 基础设施                                                          │
│   ├── SovereignTownsSubModule    (Mod 入口, MBSubModuleBase)                │
│   ├── SovereignTownsBehavior     (主 CampaignBehavior — 事件分发中心)         │
│   ├── SaveDataManager            (SaveableTypeDefiner + 字段管理)            │
│   ├── ConfigurationManager       (JSON 配置读写 + 版本迁移 + 模板)            │
│   └── LoggingSystem              (分级日志 + 异步落盘)                        │
└────────────────────────────────────────────────────────────────────────────┘
```

**层间规则**：
- 上层依赖下层；**下层永远不知道上层存在**
- 同层之间：Manager 互调通过 `SovereignTownsBehavior` 中转，**避免直接耦合**
- 评估器（Layer 2）**无状态 + 无副作用**，可在任何线程调用

---

## 1. Layer 1 — 基础设施

### 1.1 `SovereignTownsSubModule`

📂 当前实现：`workspace/SovereignTowns/src/SovereignTownsSubModule.cs`（骨架）

| 维度 | 描述 |
|---|---|
| **职责** | Mod 入口；在游戏生命周期（OnSubModuleLoad / OnBeforeGameStart / OnGameStart）的对应时机里 (1) 互斥模块检测 (2) 全局服务初始化 (3) 注册 Behavior + Model |
| **依赖** | `TaleWorlds.MountAndBlade.MBSubModuleBase`、`TaleWorlds.ModuleManager.ModuleHelper`、`TaleWorlds.Library.Debug` |
| **Tick 生命周期** | 不直接订阅事件；只在 `OnGameStart(Game, IGameStarter)` 里把 `SovereignTownsBehavior` + 各 Manager 注入 `CampaignGameStarter` |
| **数据流** | 启动期单向：读取 `ModuleHelper.IsModuleActive` → 若激活互斥 Mod 则设 `_skipBehaviorRegistration = true`；否则按顺序 `AddBehavior / AddModel` |
| **存档行为** | 无（不持有任何需存档字段） |
| **MVP** | MVP 1（互斥检测落地）+ MVP 5（添加 MCM 探测） |
| **关键 API** | `MBSubModuleBase.OnGameStart(Game, IGameStarter)` 📂 `MBSubModuleBase.cs`；`CampaignGameStarter.AddBehavior` 📂 `CampaignGameStarter.cs` |

**启动校验链**：
```
OnSubModuleLoad
  └→ ModuleHelper.IsModuleActive("ImprovedGarrisons") || .IsModuleActive("GarrisonDoSomething")
        ├ true  → Debug.Print warning + _skipBehaviorRegistration = true
        └ false → 继续

OnGameStart(Game, IGameStarter starter)
  ├ if _skipBehaviorRegistration: return（保持 Mod 装载但不工作）
  ├ if game.GameType is not Campaign: return（自定义战斗等场景不接入）
  └ starter.AddBehavior(new SovereignTownsBehavior());
      starter.AddBehavior(new TownGarrisonManager());
      starter.AddBehavior(new RecruitmentManager());
      ... (按 MVP 顺序追加)
```

---

### 1.2 `SovereignTownsBehavior`

| 维度 | 描述 |
|---|---|
| **职责** | **主 CampaignBehavior 与事件分发中心**。订阅所有 `CampaignEvents`，分发给各 Manager 与 Evaluator。**自身不做业务**，只做"事件 → 关心此事件的 Manager"映射 |
| **依赖** | `CampaignBehaviorBase`、`CampaignEvents`、各 Manager 实例 |
| **Tick 生命周期** | 订阅：`DailyTickEvent` / `HourlyTickPartyEvent` / `HourlyTickSettlementEvent` / `OnSettlementOwnerChangedEvent` / `OnMobilePartyJoinedToSiegeEvent` / `OnMobilePartyLeftSiegeEvent` / `BattleStarted` / `MapEventStarted` / `OnTroopGivenToSettlementEvent` / `OnPartyDestroyedEvent` / `OnSessionLaunchedEvent` |
| **数据流** | 单向：事件总线 → 本类内部分发 → Manager。**Manager 之间不知道彼此** |
| **存档行为** | `SyncData(IDataStore)` 委托各 Manager 自己存（避免单点过大）；本类自身只存 `_isInitialized` 等小标记 |
| **MVP** | MVP 1（基础事件 + DailyTick）；MVP 2 起追加 HourlyTickPartyEvent 等 |
| **关键 API** | `CampaignEvents.AddNonSerializedListener(this, ...)` 📂 `CampaignEvents.cs`；`CampaignBehaviorBase` 📂 `CampaignBehaviorBase.cs` |

**事件订阅清单**（按 MVP 增量）：

| 事件 | MVP 接入 | 处理逻辑 |
|---|---|---|
| `OnSessionLaunchedEvent` | 1 | 调用各 Manager 的 `OnSessionLaunched()` 初始化 |
| `DailyTickEvent` | 1 | TownGarrisonManager 日复盘 + CastleSupportManager 调拨决策 |
| `OnSettlementOwnerChangedEvent` | 1 | 更新"玩家自有 Town"缓存 |
| `HourlyTickSettlementEvent` | 2 | RiskAssessmentService 重算 + RecruitmentManager 触发 + PatrolManager 切 Order |
| `HourlyTickPartyEvent` | 2 | **首行过滤**：`if (party.PartyComponent is not SovereignTownsComponent) return;` 然后分发到 PartyLifecycleManager |
| `OnMobilePartyJoinedToSiegeEvent` / `OnMobilePartyLeftSiegeEvent` | 2 | RiskAssessmentService 标记紧急状态 |
| `BattleStarted` / `MapEventStarted` | 4 | PatrolManager 接战决策 |
| `OnTroopGivenToSettlementEvent` | 2 | 审计日志记录"兵员入驻军" |
| `OnPartyDestroyedEvent` | 2 | PartyLifecycleManager 清理状态 |
| `CanMoveToSettlementEvent` | 4 | PatrolManager 否决某队伍前往危险 settlement |
| `MercenaryNumberChangedInTown` | 2 | RecruitmentManager 观察招募槽变化 |

---

### 1.3 `SaveDataManager` + `SovereignTownsTypeDefiner`

| 维度 | 描述 |
|---|---|
| **职责** | (1) 提供 `SovereignTownsTypeDefiner : SaveableTypeDefiner`，在 `DefineClassTypes / DefineEnumTypes / DefineGenericClassDefinitions / DefineContainerDefinitions` 注册本 Mod 的全部存档类型 (2) `saveBaseId = 100_000_000` 锁定（写入 README 公开） (3) 维护"已分配的 LocalSaveId 集合"以**避免人为复用**（编译期可加 Roslyn analyzer 或运行期 sanity check） |
| **依赖** | `TaleWorlds.SaveSystem.SaveableTypeDefiner`、`SaveableFieldAttribute`、`SaveablePropertyAttribute` |
| **Tick 生命周期** | 启动期一次性（vanilla 自动调用 TypeDefiner） |
| **数据流** | 仅在 `DefineXxxTypes()` 内单次执行；运行时不参与 |
| **存档行为** | 自身**是**存档系统的一部分，不存自己数据 |
| **MVP** | MVP 1 初始版（注册基础类）；每个新 MVP 追加注册类即可 |
| **关键 API** | `SaveableTypeDefiner.AddClassDefinition(Type, int)` 📂 `SaveableTypeDefiner.cs`（待 MVP 1 反编译确认 `AddClassDefinition` 是否就是这个名 — `protected internal virtual void DefineClassTypes()` 内部用） |

**类型注册分配（初版）**：

| LocalSaveId | 类型 | MVP |
|---|---|---|
| 1 | `RecruitingPartyComponent` | 2 |
| 2 | `TransferPartyComponent` | 3.5 |
| 3 | `TownGarrisonRule` | 1 |
| 4 | `GlobalConfig` | 1 |
| 5 | `TroopFilterRule` | 3 |
| 6 | `TransferRule` | 3.5 |
| 7 | `PatrolRule` | 4 |
| 8 | `TrainingTemplate` | 3 |
| 9 | `DecisionAuditEntry` | 5.5 |
| 10 | `LLMConfig` | 5.5 |

**字段 LocalSaveId 规约**（写到 CLAUDE.md）：
- 类内字段 ID 从 1 起，连续分配
- **永不复用、永不重排序**
- 删除字段：保留 ID + 加 `[Obsolete]` + 类型改 `object` 让 vanilla 跳过

---

### 1.4 `ConfigurationManager`

| 维度 | 描述 |
|---|---|
| **职责** | (1) 读写 `<游戏存档根>/Configs/SovereignTowns/*.json` (2) 三层覆盖：全局默认 → 模板 → 单城镇覆盖 (3) 版本迁移（链式 v1→v2→v3）(4) 配置校验 + 非法回退 |
| **依赖** | `System.IO.File`、`System.Text.Json`（或 Newtonsoft，待 MVP 1 决定）；不直接依赖 vanilla |
| **Tick 生命周期** | 启动期 `OnSessionLaunched` 时加载；运行时按需 `Reload()` |
| **数据流** | 持久化：`JSON 文件 ↔ POCO 配置对象` |
| **存档行为** | **配置不进游戏存档**（与 SaveData 分离）；优势：用户重玩 / 多档共享配置 |
| **MVP** | MVP 1（全局配置 + 单城镇配置）；MVP 3（模板）；MVP 5（MCM 接入 + 迁移 v1→v2） |
| **关键 API** | `System.Text.Json.JsonSerializer.Deserialize/Serialize`、`Path.Combine`、`Directory.CreateDirectory` |

**文件布局**：
```
<游戏存档根>/Configs/SovereignTowns/
├── global.json                      # 全局默认 + 模板列表
├── templates/
│   ├── default-empire-frontier.json
│   ├── default-aserai-trade-hub.json
│   └── (用户自建)
└── settlements/
    ├── town_es5.json                # 单城镇覆盖（按 stringId）
    └── ...
```

**JSON 顶层结构（伪)**：
```
{
  "configVersion": 1,
  "lastModified": "2026-05-12T05:30:00Z",
  "globalDefaults": { ... TownGarrisonRule ... },
  "templates": [ ... ],
  "enabledFeatures": {
    "autoGarrison": true,
    "autoRecruitment": true,
    "autoPatrol": true,
    "castleSupport": false,
    "llmReasoning": false,
    "llmAutoExecute": false
  }
}
```

---

### 1.5 `LoggingSystem`

| 维度 | 描述 |
|---|---|
| **职责** | 分级日志（Debug / Info / Warn / Error）；异步落盘；单文件 5 MB 自动轮转 |
| **依赖** | `System.IO`、`System.Threading.Channels`（写入队列）、`TaleWorlds.Library.Debug`（兜底输出到游戏 log） |
| **Tick 生命周期** | 独立写入线程，与 Tick 解耦 |
| **数据流** | Tick 回调 / Manager → 内存 ConcurrentQueue<LogEntry> → 后台 Task 取出 → 写盘 |
| **存档行为** | 无 |
| **MVP** | MVP 1 |
| **关键 API** | `Channel<T>` 或 `ConcurrentQueue<T>` + `Task.Run` |

**日志文件**：`<游戏存档根>/Logs/SovereignTowns/SovereignTowns_<yyyy-MM-dd_HH-mm-ss>.log`

---

## 2. Layer 2 — 评估器 / 服务（无状态）

### 2.1 `RiskAssessmentService`

| 维度 | 描述 |
|---|---|
| **职责** | 对任意 `Settlement` 给出"风险等级"：Safe / Low / Medium / High / Critical。综合：`NearbyLandThreatIntensity`、`NearbyLandAllyIntensity`、`IsUnderSiege`、是否有敌军 MapEvent 在 N 公里内 |
| **依赖** | `Settlement` 字段（全部 vanilla 已计算好） |
| **Tick 生命周期** | **无状态**，调用方按需调用；不主动订阅事件 |
| **数据流** | 输入：Settlement 引用；输出：`RiskLevel` 枚举 + 数值分 |
| **存档行为** | 无 |
| **MVP** | MVP 1（初版返回 Safe / Low / High）；MVP 4（增加 Critical + 防御反应阈值） |
| **关键 API** | `Settlement.NearbyLandThreatIntensity / NearbyLandAllyIntensity` 📂 `Settlement.cs`；`Settlement.IsUnderSiege` |

**风险公式（初版）**：
```
threatRatio = NearbyLandThreatIntensity / (NearbyLandAllyIntensity + 1f)
if (IsUnderSiege) return Critical
if (threatRatio > 3.0f) return High
if (threatRatio > 1.5f) return Medium
if (threatRatio > 0.5f) return Low
return Safe
```

---

### 2.2 `TroopCompositionEvaluator`

| 维度 | 描述 |
|---|---|
| **职责** | 对任意 `TroopRoster` 给出兵种分类统计（骑/步/弓/弩/投掷/特殊/贵族） + 文化分布 + Tier 分布 + 与配置的差距（缺多少骑兵、缺多少 Tier 4+ 等） |
| **依赖** | `TroopRoster`、`CharacterObject`、`BasicCharacterObject`、`FormationClass` 枚举（待 MVP 3 反编译 `FormationClass` 确认值列表） |
| **Tick 生命周期** | 无状态 |
| **数据流** | 输入：`(TroopRoster current, TownGarrisonRule target)` → 输出：`CompositionGap`（含每种类的差额 + 优先级建议） |
| **存档行为** | 无 |
| **MVP** | MVP 1（基础统计）→ MVP 3（完整过滤 + 差距评分） |
| **关键 API** | `BasicCharacterObject.IsMounted / IsRanged / DefaultFormationClass` 📂 `BasicCharacterObject.cs`；`CharacterObject.Tier / IsHero / IsRegular / IsBasicTroop / Culture` 📂 `CharacterObject.cs` |

**兵种类型识别（伪逻辑）**：
```
GetTroopType(CharacterObject ch):
    if (ch.IsHero) return Hero
    if (ch.Tier >= 5 && IsNobleTier(ch.Culture, ch.Tier)) return Noble  // 文化相关判定 MVP 3 反编译时锁定
    if (ch.IsMounted && ch.IsRanged) return HorseArcher
    if (ch.IsMounted) return Cavalry
    if (ch.IsRanged):
        eq = ch.FirstBattleEquipment
        if (HasThrowingWeapon(eq)) return Thrower  // CharacterObject.HasThrowingWeapon()
        if (HasCrossbow(eq)) return Crossbow
        return Archer
    return Infantry
```

---

### 2.3 `SettlementDefenseDemandEvaluator`

| 维度 | 描述 |
|---|---|
| **职责** | 对某玩家自有 Town 判断："是否需要派兵防御 + 派多少 + 派什么质量"。综合 RiskAssessmentService 结果、当前驻军强度、配置中的"战时驻军倍率"等 |
| **依赖** | `RiskAssessmentService`、当前 `Town.GarrisonParty.MemberRoster`、`TownGarrisonRule` 配置 |
| **Tick 生命周期** | 无状态 |
| **数据流** | 输入：`Town` → 输出：`DefenseDemand`（含 needed troops / urgency） |
| **存档行为** | 无 |
| **MVP** | MVP 4 |
| **关键 API** | 见上 |

---

## 3. Layer 3 — 业务 Manager

### 3.1 `TownGarrisonManager`

| 维度 | 描述 |
|---|---|
| **职责** | 核心功能一。每日复盘每个玩家自有 Town 的驻军状态，对照 `TownGarrisonRule` 决定：(a) 是否需要发起招募 (b) 是否需要内部升级 (c) 是否需要裁撤超额兵种 (d) 是否需要调拨。**自己不创建队伍**，只发出"需求"，由 RecruitmentManager / GarrisonTransferManager 执行 |
| **依赖** | `TroopCompositionEvaluator`、`RiskAssessmentService`、`ConfigurationManager`、`Town`、`MobileParty.AllGarrisonParties` |
| **Tick 生命周期** | `DailyTickEvent`：全量复盘；`HourlyTickSettlementEvent`：仅紧急情况复盘（围城开始等） |
| **数据流** | 复盘结果 → 写入 `GarrisonDemand` 表 → 各执行 Manager 订阅此表 |
| **存档行为** | `_settlementDemands: Dictionary<Settlement, GarrisonDemand>`（标 `[SaveableField(1)]`） |
| **MVP** | MVP 1（仅计算 + 输出日志）→ MVP 3（完整配置驱动） |
| **关键 API** | `Town.AllTowns` 📂 `Town.cs`；`Settlement.OwnerClan == Clan.PlayerClan` |

**`TownGarrisonRule` 字段（配置）**：
| 字段 | 默认 | 用户原则对应 |
|---|---|---|
| `TargetTotalCount` | 80 | 目标驻军总人数 |
| `CavalryRatio` | 0.20 | 骑兵比例 |
| `InfantryRatio` | 0.50 | 步兵比例 |
| `ArcherRatio` | 0.25 | 弓兵比例 |
| `CrossbowRatio` | 0.05 | 弩兵比例 |
| `ThrowerRatio` | 0.0 | 投掷兵比例 |
| `MinTier` | 2 | 兵种质量下限 |
| `MaxTier` | 5 | 兵种质量上限 |
| `RestrictToFactionCultures` | true | 限制阵营兵种 |
| `AllowedCultureIds` | [本城阵营] | 可接受文化（空数组 = 全部） |
| `PriorityTroopIds` | [] | 优先兵种 stringId 列表（MVP 3 实现，但需文档警告"硬编码 stringId 不兼容 RBM 改兵种"） |
| `BannedTroopIds` | [] | 禁止兵种 |
| `AllowLowTierFiller` | true | 低阶兵临时补位 |
| `AllowNobleTroops` | true | 贵族兵 |
| `AllowPrisonerConversion` | true | 俘虏转化 |
| `AllowAutoUpgrade` | true | 内部升级 |
| `PreserveExisting` | true | 保留现有驻军 |
| `AutoDisbandExcess` | false | 自动裁撤超额（默认关，避免误伤） |
| `MinimumDefenders` | 30 | 最低防御人数 |
| `WartimeMultiplier` | 1.5 | 战时倍率 |
| `PeacetimeMultiplier` | 1.0 | 和平时期倍率 |
| `BudgetLimit` | 5000 | 预算限制（每月） |
| `FoodSafetyThreshold` | -2.0 | 粮食安全阈值（低于此值停止扩军） |

---

### 3.2 `RecruitmentManager`

| 维度 | 描述 |
|---|---|
| **职责** | 核心功能二。订阅 TownGarrisonManager 的 `GarrisonDemand`，决定：(1) 为哪些 Town 创建征兵队 (2) 调度征兵队前往合适村庄 (3) 监控征兵进度 (4) 完成 / 失败时回城转兵 / 解散 |
| **依赖** | `TownGarrisonManager`（需求源）、`RiskAssessmentService`、`PartyLifecycleManager`、`Settlement`、`MobileParty`、`PartyTemplateObject`、`MobilePartyAi` |
| **Tick 生命周期** | `HourlyTickPartyEvent`：监控自己创建的征兵队；`DailyTickEvent`：根据需求新建征兵队 |
| **数据流** | 输入：GarrisonDemand → 输出：MobileParty（RecruitingPartyComponent） |
| **存档行为** | `_recruitingParties: Dictionary<Settlement, MobileParty>`（`[SaveableField(1)]`，MobileParty 引用由 vanilla 存档系统自动序列化） |
| **MVP** | MVP 2 |
| **关键 API** | `CustomPartyComponent.InitializationArgs` 📂 `CustomPartyComponent.cs`；`MobileParty.SetTargetSettlement` 📂 `MobileParty.cs`；vanilla `RecruitmentCampaignBehavior.HourlyTickParty` 自动驱动招募 📂 `RecruitmentCampaignBehavior.cs` |

**子组件**：

```
RecruitmentManager
├── RecruitingPartyComponent : CustomPartyComponent   # 队伍 component
├── RecruitmentPlanner                                # 决定派去哪个村庄
│     输入: TownGarrisonRule + 周边 villages + 风险 + Notable.VolunteerTypes
│     输出: 目标 Village 列表（按优先级）
└── RecruitmentMonitor                                # 监控进度
      输入: MobileParty + CampaignTime.DepartureTime
      输出: 触发回城 / 解散 / 重规划
```

**生命周期状态机**：
```
[Idle] ──demand→ [Spawning] ──party_created→ [Traveling] ──reached_village→ [Recruiting]
                                                                    ↓ vanilla auto fills MemberRoster
                                                          ──member_count_reached_OR_danger→ [Returning]
                                                          ──timeout_OR_no_recruits→ [Failing]
[Returning] ──reached_home→ [Transferring] ──troops_given→ [Disbanding] → [Idle]
[Failing]   ──reached_home_OR_3_hours→ [Disbanding] → [Idle]
```

---

### 3.3 `PatrolManager`

| 维度 | 描述 |
|---|---|
| **职责** | 核心功能三。管理每个玩家自有 Town 的巡逻队。决定：(a) 是否创建 (b) Order 切换（5 种）(c) 回城补给 (d) 接战决策 |
| **依赖** | `RiskAssessmentService`、`SettlementDefenseDemandEvaluator`、`PatrolPartyComponent`（vanilla）、`MobilePartyAi` |
| **Tick 生命周期** | `HourlyTickSettlementEvent`：切 Order；`HourlyTickPartyEvent`：监控本 Mod 调度的巡逻队（vanilla 创建的不动）；`CanMoveToSettlementEvent`：拦截前往危险 settlement |
| **数据流** | 配置 + 风险 → Order 状态 → MobileParty 调度命令 |
| **存档行为** | `_patrolOrders: Dictionary<Settlement, PatrolOrder>`（`[SaveableField(1)]`） |
| **MVP** | MVP 4 |
| **关键 API** | `PatrolPartyComponent.CreatePatrolParty` 📂 `PatrolPartyComponent.cs`；`Settlement.PatrolParty`（一对一锁定）📂 `Settlement.cs`；`MobilePartyAi.SetInitiative` / `DisableForHours` 📂 `MobilePartyAi.cs` |

**Order 状态机（5 种）**：
| Order | 触发条件 | 行为 |
|---|---|---|
| Defense | RiskLevel >= High 且配置启用 | `SetTargetSettlement(home, false)` + `SetInitiative(attack=0.3, avoid=0.7)` |
| Escort | 配置指定护送 + 商队 / 村庄队伍在范围内 | `SetTargetParty(targetParty)` |
| MergeGarrison | MemberRoster.Count < 20% target | `SetTargetSettlement(home, false)` + 到达后转兵 + 解散 |
| Patrol | 默认 | `SetMoveGoToSettlement(...)` 在配置半径内 settlement 间巡游 |
| StopIfPlayerTarget | `ShortTermTargetParty == MobileParty.MainParty` | `DisableForHours(4)` 让玩家先动 |

---

### 3.4 `CastleSupportManager`

| 维度 | 描述 |
|---|---|
| **职责** | 核心功能四的"语义层"。决定：(a) 玩家自有城堡是否需要从某玩家自有城镇调兵补充 (b) 玩家自有城镇驻军不足时是否从某玩家自有城堡抽调富余 |
| **依赖** | `TownGarrisonManager`（城镇侧需求）、自建的 `CastleNeedEvaluator`、`Settlement.IsCastle` |
| **Tick 生命周期** | `DailyTickEvent` |
| **数据流** | 城堡 / 城镇当前 GarrisonParty 状态 → 比对配置 → 产出"调拨任务"投给 `GarrisonTransferManager` |
| **存档行为** | `_pendingTransferTasks: List<TransferTask>`（`[SaveableField(1)]`） |
| **MVP** | MVP 3.5 |
| **关键 API** | `Settlement.IsCastle` 📂 `Settlement.cs`；`MobileParty.AllGarrisonParties` |

**用户原则强约束**：
- 城堡 = 仅作为来源 / 需求方
- 不管理城堡的具体兵种 / 兵种比例 — 只判断"驻军总人数是否合格"
- 村庄禁止驻军，禁止当仓库

**`TransferRule` 配置字段**：
| 字段 | 用户原则对应 |
|---|---|
| `SourceSettlement` | 源（城镇/城堡） |
| `DestinationSettlement` | 目的（城镇/城堡） |
| `Priority` | 优先级 |
| `MinReserveAtSource` | 最低保留 |
| `MaxTroopsPerTransfer` | 单次最大 |
| `MinTierFilter` | 兵种质量过滤 |
| `MaxTierFilter` | 同上 |
| `CultureFilter` | 文化过滤 |
| `FormationFilter` | Cavalry / Infantry / Ranged |
| `ProhibitWhenEnemyNearby` | 敌军接近禁止 |
| `ProhibitWhenSiege` | 围城禁止 |
| `ProhibitWhenLowFood` | 粮食不足禁止 |
| `MaxRouteDistance` | 路线最远 |

---

### 3.5 `GarrisonTransferManager`

| 维度 | 描述 |
|---|---|
| **职责** | 接收 `CastleSupportManager` 产出的调拨任务并**真实执行**：(1) 从源驻军扣兵 → 创建 `TransferPartyComponent` (2) 派往目的地 (3) 到达后入目的地驻军 |
| **依赖** | `CastleSupportManager`、`RiskAssessmentService`、`PartyLifecycleManager`、`CustomPartyComponent`、`TransferTroopsAction`（待 MVP 3.5 反编译 `Actions` 命名空间确认） |
| **Tick 生命周期** | `HourlyTickPartyEvent`（监控调拨队） |
| **数据流** | TransferTask 队列 → MobileParty 调度 → 到达后 transfer + 解散 |
| **存档行为** | `_activeTransfers: List<MobileParty>` + 每个 `TransferPartyComponent` 上的 `[SaveableProperty]` 字段：Source / Destination / DepartureTime |
| **MVP** | MVP 3.5 |
| **关键 API** | `CustomPartyComponent` 📂 `CustomPartyComponent.cs`；`MobileParty.SetTargetSettlement` |

---

### 3.6 `PartyLifecycleManager`

| 维度 | 描述 |
|---|---|
| **职责** | 管理本 Mod 创建的**所有** MobileParty 的生命周期：(a) 注册上限（征兵队 / 调拨队 / 巡逻队各类）(b) 空闲检测 24 小时强制解散 (c) 路径阻塞检测 + 解套 (d) 解散统一走 vanilla Action |
| **依赖** | `MobileParty.AllCustomParties` + `AllPatrolParties`、`MobilePartyAi`、`Settlement` |
| **Tick 生命周期** | `HourlyTickPartyEvent`：每个本 Mod 队伍单次检查；`OnPartyDestroyedEvent`：清理 |
| **数据流** | 查询每队的 `StationaryStartTime` + 当前 ShortTermBehavior → 决定是否干预 |
| **存档行为** | `_partyStateMap: Dictionary<MobileParty, PartyLifeState>`（`[SaveableField(1)]`） |
| **MVP** | MVP 2（基础上限 + 空闲检测）→ MVP 4（完整路径检测） |
| **关键 API** | `MobilePartyAi.CheckIfThereIsAnyHugeObstacleBetweenPartyAndTarget`（反射 — internal） / `DisableForHours` / `RethinkAtNextHourlyTick` 📂 `MobilePartyAi.cs`；`DestroyPartyAction.Apply`（待反编译确认） |

---

## 4. Layer 4 — LLM（MVP 5.5+）

### 4.0 ⚠️ 架构硬约束：LLM **禁用于即时路径**

📌 用户 2026-05-12 明确指示：LLM 因为需要时间（典型 1–10 秒）输出结果，**不适用于任何即时任务**。即时任务必须由规则引擎在数十毫秒内出决策。

| 路径 | 是否允许 LLM | 理由 |
|---|---|---|
| `HourlyTickPartyEvent` / `HourlyTickSettlementEvent` 内的决策 | ❌ 禁止 | 每小时数百个 settlement / party，LLM 延迟会拖崩 Tick |
| `RiskAssessmentService` 的实时评估 | ❌ 禁止 | 评估器无状态、需要瞬时返回 |
| `PatrolManager` 的 Order 切换 / 接战决策 | ❌ 禁止 | 战斗反应时间窗以秒计 |
| `PartyLifecycleManager` 的路径阻塞解套 / 空闲检测 | ❌ 禁止 | 队伍卡死的解套必须立即 |
| `SettlementDefenseDemandEvaluator` 的防御应急 | ❌ 禁止 | 敌军已逼近，无等待空间 |
| 所有 `CanMoveToSettlementEvent` / 围城紧急 / 战时调整 | ❌ 禁止 | 同上 |
| `DailyTickEvent` / `WeeklyTickEvent` 长期复盘 | ✅ 允许（仅建议） | 一日一次，可承受网络延迟 |
| 用户主动点击"参谋建议" 菜单 | ✅ 允许 | 用户在等待，可显式等几秒 |
| 新游戏 / 新城镇接管的初始规则草案 | ✅ 允许 | 离线场景 |
| 多战线兵力分配的离线评估 | ✅ 允许 | 战略级，非战术级 |
| 配置模板自然语言生成（用户描述 → 模板） | ✅ 允许 | 用户显式触发 |

**强制规则**：
1. `LLMReasoningService` **不允许**被 `HourlyTick*` 系列订阅。`SovereignTownsBehavior` 在事件分发时硬性拒绝向 LLM 路径分发 Hourly 事件
2. 即便启用了 LLM，**所有即时路径上的决策一律由 `RuleBasedFallbackDecisionMaker` 直接产出**
3. LLM 的产出只能作为"下一轮 Daily Tick 的配置参考"或"用户主动确认后的模板"
4. 这与"LLM 必须异步 + 超时"是**不同层**的规则：异步超时是技术兜底，**禁用于即时路径是架构约束**

### 4.1 `LLMProviderInterface`

| 维度 | 描述 |
|---|---|
| **职责** | 抽象"调用 LLM" 这件事，提供三种实现：`LocalOllamaProvider` / `RemoteOpenAICompatibleProvider` / `NoOpProvider`（默认） |
| **依赖** | `System.Net.Http.HttpClient`、`System.Text.Json`；不依赖 vanilla |
| **Tick 生命周期** | 非 Tick，按需调用；调用方走 `async/await` |
| **数据流** | 输入：`LLMPrompt`（局势摘要 JSON 字符串）→ 输出：`LLMResponse`（JSON 字符串） |
| **存档行为** | 无 |
| **MVP** | MVP 5.5 |
| **关键 API** | `HttpClient.PostAsync` + 10s `CancellationTokenSource.CancelAfter(...)` |

**接口签名**：
```
public interface ILLMProvider {
    Task<LLMResponse> CompleteAsync(LLMPrompt prompt, CancellationToken ct);
    bool IsAvailable { get; }
    string ProviderName { get; }
}
```

---

### 4.2 `LLMReasoningService`

| 维度 | 描述 |
|---|---|
| **职责** | 整合调用流程：(1) 收集"局势摘要" (2) 构造 Prompt (3) 调用 ILLMProvider (4) 解析 + 校验响应 (5) 失败时回退到 `RuleBasedFallbackDecisionMaker` |
| **依赖** | `LLMProviderInterface`、`LLMDecisionValidator`、`RuleBasedFallbackDecisionMaker`、`DecisionAuditLogger` |
| **Tick 生命周期** | 异步触发，不占 Tick 主线程 |
| **数据流** | Manager 提问 → 本服务 → LLMProvider → Validator → 通过则返回结构化决策；不通过则 fallback |
| **存档行为** | 无 |
| **MVP** | MVP 5.5（建议模式） / MVP 6（自动执行模式由配置开关切换） |
| **关键 API** | `Task.Run` + `Campaign.Current.PostCampaignEvent`（确认 vanilla 是否有此 API；如无则用 `MBSubModuleBase.OnApplicationTick` 内的"主线程队列"） |

**Prompt 模板（伪)**：
```
SYSTEM:
你是 Bannerlord Mod 的策略推理助手。你将收到一个局势摘要 JSON，输出一个决策 JSON。
决策必须严格符合本 Mod 的安全规则；不输出任何自然语言；输出仅一个 JSON 对象。

USER:
{
  "context": "garrison_planning",
  "settlement": { "id": "town_es5", "owner": "player", "garrisonCount": 45, "target": 80, ... },
  "nearbyThreats": [...],
  "playerClan": { "gold": 50000, ... }
}

RESPONSE (LLM):
{ "action": "send_recruiting_party", "sourceSettlement": "town_es5", "targetVillages": ["v_es5_1"], "maxTroops": 35, "riskLevel": "low", "reason": "..." }
```

---

### 4.3 `LLMDecisionValidator`

| 维度 | 描述 |
|---|---|
| **职责** | 对 LLM 输出做三重校验：(a) JSON Schema 校验 (b) 本地规则校验（动作合法、目标可达、不超配置上限）(c) 安全限制校验（不抽空驻军、不绕过经济） |
| **依赖** | `JsonSchema`（如 `JsonSchema.Net`）、`ConfigurationManager`、各 Manager 的"安全规则" API |
| **Tick 生命周期** | 同步调用，O(1) |
| **数据流** | LLMResponse → ValidationResult{IsValid, Reasons[]} |
| **存档行为** | 无 |
| **MVP** | MVP 5.5 |
| **关键 API** | `System.Text.Json.JsonDocument.Parse` + 自定义 Validator |

---

### 4.4 `RuleBasedFallbackDecisionMaker`

| 维度 | 描述 |
|---|---|
| **职责** | 在 LLM 不可用 / 超时 / 输出非法时给出**确定性**决策。是 LLM 的功能下限保障 |
| **依赖** | 所有评估器（RiskAssessmentService、TroopCompositionEvaluator、SettlementDefenseDemandEvaluator）；`ConfigurationManager` |
| **Tick 生命周期** | 同步调用 |
| **数据流** | 同 LLMReasoningService 的输入，但用 if-else 规则直接产出决策 |
| **存档行为** | 无 |
| **MVP** | **MVP 1 起就要有** — 因为没 LLM 时这就是唯一决策器 |
| **关键 API** | 全部基础设施 |

**关键性质**：本 Mod **默认走规则引擎**。LLM 是增强功能。

---

### 4.5 `DecisionAuditLogger`

| 维度 | 描述 |
|---|---|
| **职责** | 审计每次决策：来源（规则 vs LLM）、输入摘要、决策结果、是否采纳、拒绝原因 |
| **依赖** | `LoggingSystem` |
| **Tick 生命周期** | 即时落盘到 audit log 文件（独立文件，不与一般日志混） |
| **数据流** | Manager / LLM → `LogDecision(entry)` → 文件 |
| **存档行为** | **审计记录写文件，不写存档**（避免存档膨胀） |
| **MVP** | MVP 1（基础规则决策审计）→ MVP 5.5（增 LLM 字段） |
| **关键 API** | 同 LoggingSystem |

**审计条目结构**：
```
{
  "timestamp": "2026-05-12T03:14:00Z",
  "decisionType": "send_recruiting_party",
  "source": "rule" | "llm",
  "inputSummary": "town_es5 garrison 45/80 cavalry-short risk:low",
  "decision": { "action": "...", "params": {...} },
  "accepted": true,
  "rejectionReason": null
}
```

---

## 5. Layer 5 — UI / 集成

### 5.1 `MCMIntegration`（MVP 5+，软依赖）

| 维度 | 描述 |
|---|---|
| **职责** | 通过反射检测 MCM 是否可用；可用时把全局 / 单城镇配置映射到 MCM 的 `AttributeGlobalSettings<T>` |
| **依赖** | 反射 + 假设 MCM API（`MCM.Abstractions.Settings.Base.Global.AttributeGlobalSettings`<T>） |
| **Tick 生命周期** | 启动期注册 |
| **数据流** | MCM UI 修改 → 触发回调 → 同步到 `ConfigurationManager` |
| **存档行为** | 无（MCM 自管） |
| **MVP** | MVP 5 |
| **关键 API** | `Type.GetType("...MCMv5", false)` |

---

### 5.2 `DebugCommandSystem`（开发期）

| 维度 | 描述 |
|---|---|
| **职责** | 注册 cheat 风格的控制台命令：`campaign.sovereign_towns.dump_state`、`...force_recruit town_es5`、`...set_risk town_es5 high` 等。**仅 Debug 构建启用** |
| **依赖** | `TaleWorlds.Library.CommandLineFunctionalityAttribute`（或类似，待 MVP 1 反编译 cheat 系统时确认精确属性） |
| **Tick 生命周期** | 启动期注册 |
| **存档行为** | 无 |
| **MVP** | MVP 1 |

---

### 5.3 GameMenu 集成

| 维度 | 描述 |
|---|---|
| **职责** | 在 vanilla 城镇菜单添加"Sovereign Towns" 入口，弹出诊断 / 配置 / 控制面板 |
| **依赖** | `CampaignGameStarter.AddGameMenu / AddGameMenuOption / AddWaitGameMenu` |
| **Tick 生命周期** | 启动期注册菜单 |
| **存档行为** | 无 |
| **MVP** | MVP 1（仅诊断面板）→ MVP 5（完整配置编辑） |
| **关键 API** | 📂 `CampaignGameStarter.cs` |

---

## 6. 数据流总图（MVP 完整态）

```
[配置文件] ──ConfigurationManager.Load()──▶ GlobalConfig + Templates + per-town Rules
                                                            │
                                                            ▼
[Daily Tick] ───▶ TownGarrisonManager.Evaluate(rule, currentGarrison)
                              │
                              ├──▶ TroopCompositionEvaluator
                              ├──▶ RiskAssessmentService
                              └──▶ 产出 GarrisonDemand
                                            │
                              ┌─────────────┼─────────────┐
                              ▼             ▼             ▼
                  RecruitmentManager  CastleSupportManager  (内部 upgrade)
                              │             │
                              ▼             ▼
                  RecruitingParty   GarrisonTransferManager
                  (在地图上跑)         (在地图上跑)
                              │             │
                              ▼             ▼
                  vanilla Recruitment  TransferTroopsAction
                              │             │
                              └─────┬───────┘
                                    ▼
                          Town.GarrisonParty.MemberRoster 更新

[Hourly Tick — Settlement] ──▶ PatrolManager.UpdateOrder(town)
                                          │
                                          ▼
                                  PatrolParty (vanilla 类型)
                                  调度 / 接战 / 回援

[即时路径 — 全部走规则引擎，无 LLM 介入]
HourlyTick*Event / 战斗反应 / 路径解套 / 围城紧急
  └→ RuleBasedFallbackDecisionMaker.Decide(summary)
        └→ DecisionAuditLogger.Log(decision)

[非即时路径 — LLM 可选，仅用作建议 / 模板生成]
DailyTickEvent 长期复盘 / 用户主动点"参谋建议"
  ├ if config.enableLLMReasoning:
  │     LLMReasoningService.AskAsync(summary)  // 异步、不阻塞
  │       ├ async LLMProvider.CompleteAsync (10s 超时)
  │       └ LLMDecisionValidator.Validate
  │   结果落地：
  │     - 用户主动询问 → 显示给用户 / 用户确认采纳后写入下一轮配置
  │     - Daily 复盘 → 写入 "建议待审" 队列；下次 Daily Tick 前用户可在城镇菜单确认
  ├ else / on fail / on timeout:
  │     RuleBasedFallbackDecisionMaker.Decide(summary)
  └ DecisionAuditLogger.Log(decision)
```

---

## 7. Tick 风险预算

| Tick 事件 | 单次预算 | 监控指标 |
|---|---|---|
| `DailyTickEvent` | < 50 ms total | TownGarrisonManager 评估所有自有 Town |
| `HourlyTickSettlementEvent`（每 settlement）| < 1 ms | 仅过滤后处理玩家自有 |
| `HourlyTickPartyEvent`（每 party）| < 0.1 ms | 首行 PartyComponent 过滤 |
| 自有 MobileParty < 100 | 总 hourly tick 开销 < 10 ms | PartyLifecycleManager 监控 |

**性能监控钩子**（MVP 1 就做）：每个事件订阅点用 `Stopwatch` 包裹，超阈值 log warning。

---

## 8. 模块 vs MVP 接入矩阵

| 模块 | MVP1 | MVP2 | MVP3 | MVP3.5 | MVP4 | MVP5 | MVP5.5 | MVP6 |
|---|---|---|---|---|---|---|---|---|
| SovereignTownsSubModule | ★ | + | + | + | + | + | + | + |
| SovereignTownsBehavior | ★ | + | + | + | + | + | + | + |
| SaveDataManager | ★ | + | + | + | + | + | + | + |
| ConfigurationManager | ★ | + | + | + | + | + | + | + |
| LoggingSystem | ★ | + | + | + | + | + | + | + |
| RiskAssessmentService | ★ | + | + | + | + | + | + | + |
| TroopCompositionEvaluator | ★ | + | ★ | + | + | + | + | + |
| SettlementDefenseDemandEvaluator | — | — | — | — | ★ | + | + | + |
| TownGarrisonManager | ★(规划/不执行) | + | ★ | + | + | + | + | + |
| RecruitmentManager | — | ★ | + | + | + | + | + | + |
| PartyLifecycleManager | — | ★ | + | + | + | + | + | + |
| PatrolManager | — | — | — | — | ★ | + | + | + |
| CastleSupportManager | — | — | — | ★ | + | + | + | + |
| GarrisonTransferManager | — | — | — | ★ | + | + | + | + |
| MCMIntegration | — | — | — | — | — | ★(软依赖) | + | + |
| DebugCommandSystem | ★ | + | + | + | + | + | + | + |
| GameMenu 集成 | ★(诊断) | + | + | + | + | ★(配置) | + | + |
| RuleBasedFallbackDecisionMaker | ★ | + | + | + | + | + | + | + |
| DecisionAuditLogger | ★ | + | + | + | + | + | + | + |
| LLMProviderInterface | — | — | — | — | — | — | ★ | + |
| LLMReasoningService | — | — | — | — | — | — | ★(建议) | ★(自动) |
| LLMDecisionValidator | — | — | — | — | — | — | ★ | + |

★ = 首次接入；+ = 已存在并继续完善

---

## 9. 第三阶段交付物清单

| 文件 | 状态 |
|---|---|
| `workspace/ARCHITECTURE.md` | ✅ 本文档 |
| `workspace/FEASIBILITY_REPORT.md` | ✅ 第二阶段 |
| 第一阶段 4 份文档 | ✅ |
| `workspace/SovereignTowns/` 骨架 | ✅ |
| 反编译证据库 | ✅ 40+ 文件 |

---

## 10. 进入 MVP 1 编码的入口条件

MVP 1 第一次编码需要补的 vanilla 类反编译（不阻塞架构，但写代码前要做）：

1. `TaleWorlds.SaveSystem` 的 `DefinitionContext.AddClassDefinition` 等具体注册 API（确认参数顺序）
2. `Hero` 类（特别是 `VolunteerTypes` 属性签名 — 在 MVP 2 才需要，但 MVP 1 写 SaveableTypeDefiner 时可能要 reference）
3. `TaleWorlds.Library.InformationManager` + `InformationMessage`（用于 OnSubModuleLoad 弹消息）
4. `Campaign` 类（确认 `Campaign.Current` 在哪些时机非空）
5. cheat 命令属性（`CommandLineFunctionality` 或类似）

**所有上述项 MVP 1 编码时增量反编译即可，不阻塞架构定型。**

---

## 11. 准备进入 MVP 1 编码

待用户确认架构后，进入第四阶段：**分阶段实现（MVP 1）**。

按用户原则"小步可验证"：

**MVP 1 第一步**（最小切片）：
1. 在 `SovereignTownsSubModule.OnSubModuleLoad` 加入互斥检测（已有骨架，1 行代码）
2. 启动游戏验证：启用 IG/GDS 时本 Mod 退化（不报错也不工作）
3. 用户截图 / log 反馈

**MVP 1 第二步**：
1. 添加 `LoggingSystem`（异步落盘）
2. 添加 `SovereignTownsBehavior`（订阅 OnSessionLaunched + DailyTickEvent）
3. 启动战役，观察 `<游戏存档根>/Logs/SovereignTowns/*.log` 出现 + 每日有"玩家自有 Town 列表"输出

**MVP 1 第三步**：
1. 添加 `ConfigurationManager`（JSON 读写 + 默认配置）
2. 添加 `TownGarrisonManager` 的"规划模式"（只评估，不创建队伍）
3. 验证日志中输出每城驻军差距分析

完成 MVP 1 三步后，**第一阶段闭环**：Mod 安全装载、能识别玩家城镇、能输出诊断。**仍未创建任何队伍**，零存档风险。

请确认是否进入 MVP 1。
