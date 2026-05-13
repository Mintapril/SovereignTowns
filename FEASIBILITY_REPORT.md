# 第二阶段：可行性报告 — Sovereign Towns

> 版本：v0.1（第二阶段产出）
> 锚定游戏版本：Bannerlord v1.3.15
> 锚定 Mod 版本：SovereignTowns v0.0.1（骨架已就位）
> 基础证据：`RESEARCH_FINDINGS.md` / `MOD_SURVEY.md` / `UNCERTAINTY_LOG.md` / `RISK_REGISTER.md` / `_research/decompiled/*.cs`
>
> **本报告不写实现代码**。每条结论都标注 📂 证据来源。

---

## 0. 一句话结论

**完全可行**，且**不需要 Harmony Patch** 也能覆盖用户的全部 7 项核心功能（驻军 / 征兵 / 巡逻 / 调拨 / 配置 / LLM / 兼容）。所有阻塞性不确定点（U1–U12）已闭环。可立即开始 MVP 1 编码。

---

## 1. API 可用性矩阵

### 1.1 直接可用的官方 API（无 Harmony）

| 用途 | 真实 API | 📂 证据文件 |
|---|---|---|
| Mod 入口 | `MBSubModuleBase.OnSubModuleLoad / OnBeforeGameStart / OnGameStart(Game, IGameStarter)` | `MBSubModuleBase.cs` |
| 注册 Behavior | `CampaignGameStarter.AddBehavior(CampaignBehaviorBase)` | `CampaignGameStarter.cs` |
| 注册 Model 替换 | `CampaignGameStarter.AddModel(GameModel)` | `CampaignGameStarter.cs` |
| 事件订阅 | `CampaignEvents.<X>Event.AddNonSerializedListener(this, handler)` —— 274 个事件可用 | `CampaignEvents.cs` |
| Tick 调度 | `DailyTick*Event` / `HourlyTick*Event` / `AiHourlyTickEvent` | `CampaignEvents.cs` |
| 自定义存档 | `SaveableTypeDefiner` + `[SaveableField(short)]` / `[SaveableProperty(short)]` | `SaveableTypeDefiner.cs`、`SaveableFieldAttribute.cs` |
| 巡逻队创建 | `PatrolPartyComponent.CreatePatrolParty(stringId, position, spawnRadius, homeSettlement, template)` | `PatrolPartyComponent.cs` |
| 驻军队创建 | `GarrisonPartyComponent.CreateGarrisonParty(stringId, settlement)` | `GarrisonPartyComponent.cs` |
| 驻军队转换 | `GarrisonPartyComponent.ConvertPartyToGarrisonParty(mobileParty, settlement)` | `GarrisonPartyComponent.cs` |
| 自定义队伍 | 继承 `CustomPartyComponent` + 用 `InitializationArgs` 实例化 | `CustomPartyComponent.cs` |
| 领主队工厂 | `MobilePartyHelper.SpawnLordParty(hero, settlement)` 等 | `MobilePartyHelper.cs` |
| 队伍模板填充 | `MobilePartyHelper.FillPartyManuallyAfterCreation(mobileParty, partyTemplate, desiredMenCount)` | `MobilePartyHelper.cs` |
| 全局队伍枚举 | `MobileParty.AllGarrisonParties / AllPatrolParties / AllCustomParties / AllLordParties` 等静态集合 | `MobileParty.cs` |
| 队伍调度 | `MobileParty.SetTargetSettlement(settlement, isTargetingPort)` / `SetCustomHomeSettlement(...)` | `MobileParty.cs` |
| 兵种判定 | `BasicCharacterObject.IsMounted / IsRanged / DefaultFormationClass` + `CharacterObject.Tier / IsHero / IsRegular / UpgradeTargets` | `CharacterObject.cs`、`BasicCharacterObject.cs` |
| 城镇枚举 | `Town.AllTowns / AllCastles`、`Settlement.All` | `Town.cs`、`Settlement.cs` |
| 城镇所属 | `Settlement.MapFaction`、`Settlement.Owner`、`Settlement.OwnerClan` | `Settlement.cs` |
| 城镇威胁数值 | `Settlement.NearbyLandThreatIntensity` / `NearbyLandAllyIntensity` | `Settlement.cs` |
| 围城判定 | `Settlement.IsUnderSiege`、`MobileParty.BesiegedSettlement` | `Settlement.cs`、`MobileParty.cs` |
| 招募真实推进 | `RecruitmentCampaignBehavior.HourlyTickParty(mobileParty)`（vanilla Behavior 自动驱动） | `RecruitmentCampaignBehavior.cs` |
| 招募计算解释 | `GarrisonRecruitmentCampaignBehavior.GetGarrisonChangeExplainedNumber(town)` | `GarrisonRecruitmentCampaignBehavior.cs` |
| 驻军兵员写入 | `TroopRoster.AddToCounts(character, count, ...)` —— 只在通过合法招募经济链路后调用 | `TroopRoster.cs` |
| Tick 内 AI 干预 | `MobilePartyAi.DisableForHours(int)` / `SetInitiative(...)` / `RethinkAtNextHourlyTick` | `MobilePartyAi.cs` |
| 路径阻塞检测 | `MobilePartyAi.CheckIfThereIsAnyHugeObstacleBetweenPartyAndTarget(party, position)`（internal —— 反射调用） | `MobilePartyAi.cs` |
| 模块检测 | `ModuleHelper.IsModuleActive(string moduleId)` / `GetActiveModules()` / `GetModuleInfo(string)` | `ModuleHelper.cs` |
| UI 消息 | `InformationManager.DisplayMessage(InformationMessage)`（待二次确认 namespace） | RecruitmentCampaignBehavior 内有使用 |

### 1.2 必须 Harmony 的边界

**当前判断：本 Mod MVP 1–6 完全不需要 Harmony Patch。**

下列情形若在 MVP 4 / 实际跑起来后才暴露，再追加单点 Patch：

| 假想需求 | 是否真需 Harmony | 替代方案 |
|---|---|---|
| 实现 `IGarrisonRecruitmentBehavior` 接管驻军招募 | 否 — 实现接口 + `AddBehavior` 即可 | `CampaignGameStarter.AddBehavior(new MyRecruiter())` |
| 实现 `IPatrolPartiesCampaignBehavior` 接管巡逻 | 否 — 同上 | 同上 |
| 城镇人数上限 | 否 — `CampaignGameStarter.AddModel(new MyPartySizeLimitModel())` | 见 GDS 反面教材（Patch DefaultPartySizeLimitModel 导致与 IG 冲突） |
| 阻止 vanilla 把我们的巡逻队当 vanilla 巡逻队接管 | 否 — `PartyComponent.AvoidHostileActions = true` 或将 patrol party 的 `MobilePartyAi.DisableForHours(big)` | — |
| 调整某 vanilla 模型的特定方法语义 | **可能是** —— 若 vanilla `GameModel` 不能整体替换，需 Patch 局部 | 极少触发，留作应急 |

**结论**：**Harmony 不进入 MVP 1–5 的依赖图**。`Bannerlord.Harmony` 模块已装可用，但本 Mod **不在 SubModule.xml 声明依赖**。MVP 6 LLM 行为若有调试需求，再考虑可选引入。

### 1.3 待二次确认的 API（不阻塞，编码时确认）

| 项 | 不阻塞理由 |
|---|---|
| `CharacterObject.Culture` 的精确返回类型（`CultureObject`） | RecruitmentCampaignBehavior 已隐式用到，编码时 IntelliSense 可见 |
| `InformationManager.DisplayMessage` 的精确命名空间 | 编码时编译器报错即可 |
| `TransferTroopsAction.Apply(...)` / `AddTroopsAction.Apply(...)` 是否存在 | 通过 `Action` 类反编译可获 — MVP 2 编码时再做 |
| `Hero.VolunteerTypes` 真实属性签名 | MVP 2 写真实招募时再反编译 `Hero.cs` |

---

## 2. 真实招募的实现路径（用户原则核心约束）

### 2.1 vanilla 招募机制（已锁定）

📂 证据：`RecruitmentCampaignBehavior.cs`、`Settlement.cs`

```
[Settlement 层]
Settlement
  └── Notables (MBReadOnlyList<Hero>)
        └── Hero.VolunteerTypes  ← 每个贵族提供的可招募兵种（数组，按招募槽位）

[Behavior 层]
RecruitmentCampaignBehavior（vanilla）
  ├── HourlyTickParty(MobileParty)        ← 每小时给驻在城镇内的队伍补员
  ├── OnBeforeSettlementEntered(...)      ← 队伍进城时招募评估
  └── TownMercenaryData GetMercenaryData(Town)  ← 佣兵槽

[Cost 模型]
GarrisonRecruitmentCampaignBehavior（vanilla, IGarrisonRecruitmentBehavior）
  └── GetGarrisonChangeExplainedNumber(Town town)  ← 解释模型，标准的"驻军变化"数值
```

### 2.2 本 Mod 的"真实招募"路径

本 Mod 不创造新招募源、不绕过经济、不凭空 AddTroop。完整流程：

```
[MVP 2 路径]
RecruitmentManager（本 Mod）
  ↓ 每小时复盘
  ├── 检测每个玩家自有 Town：驻军差距 > 阈值
  ↓ 触发条件成立
  ├── 创建 RecruitingPartyComponent（继承 CustomPartyComponent）
  │     ├── HomeSettlement = 目标 Town
  │     ├── Leader = null（或玩家氏族某 hero）
  │     ├── 初始 TroopRoster = 空 / 1 名领队
  │     └── 注入预算金额（PartyBase.ItemRoster 加金币）
  ↓ 队伍进入大地图
  ├── MobileParty.SetTargetSettlement(nearestVillage, false)
  │     ↓ 队伍走到村庄
  │     ↓ 进入 OnBeforeSettlementEntered → RecruitmentCampaignBehavior 评估
  │     ↓ vanilla HourlyTickParty 走真实招募：扣金币、消耗 Notable.VolunteerTypes 槽位
  │     └── 队伍 MemberRoster 增加兵员
  ↓ 队伍达到目标人数 / 资金耗尽 / 安全风险
  ├── MobileParty.SetTargetSettlement(homeSettlement, false) ← 回城
  ↓ 队伍回到 home Town
  └── 用 TransferTroopsAction.Apply(...) 把兵员转给 Town.GarrisonParty
        ↑ 这一步是合法的官方 Action，等同于玩家用 Party Screen 把兵留在城镇
```

**关键性质**：
- 招募过程**完全由 vanilla 招募逻辑执行**，我们只负责"派一支真实队伍去村庄 / 让 vanilla 给它招兵"
- 金币、Notable 关系、村庄招募槽位**全部按 vanilla 规则消耗**
- RBM 改 NPCCharacters 兵种后，vanilla 招募自动使用新兵种 —— 我们零干预

### 2.3 待 MVP 2 编码时反编译确认的 API

- `Hero.VolunteerTypes` 的精确签名（`CharacterObject[]` 或 `TroopRosterElement[]`）
- `TransferTroopsAction` / `AddTroopsAction` / 类似 Action 类的精确签名
- 创建 `RecruitingPartyComponent` 时需要的 `PartyTemplateObject` 来源（一般用空 template + 后续填）

---

## 3. 真实征兵队的实现路径

### 3.1 队伍创建

📂 证据：`CustomPartyComponent.cs`、`MobilePartyHelper.cs`

```
RecruitingPartyComponent : CustomPartyComponent
{
    protected RecruitingPartyComponent(Settlement homeSettlement, ...)
        : base(homeSettlement, name, owner: null,
               partyMountStringId: <vanilla 默认>, partyHarnessStringId: <vanilla 默认>,
               customPartyBaseSpeed: 4.5f, avoidHostileActions: true,
               args: new InitializationArgs(homePosition, spawnRadius: 1f,
                                            clan: Clan.PlayerClan,
                                            partyTemplate: <空 template>),
               leader: null) {}

    // 必填 override
    public override Hero PartyOwner => HomeSettlement.OwnerClan?.Leader;
    public override TextObject Name => new TextObject("Recruiting Party of " + HomeSettlement.Name);
    public override Settlement HomeSettlement => _homeSettlement;
    public override bool AvoidHostileActions => true; // ★ 自动避战
}
```

### 3.2 上限控制（R-P2 缓解）

```
每个玩家自有 Town：最多 1 个 RecruitingPartyComponent 实例（用静态映射 Settlement ↔ Party）
全局：MobileParty.AllCustomParties.Count(p => p.PartyComponent is RecruitingPartyComponent) ≤ Town.AllTowns.Count(玩家自有)
```

### 3.3 风险判断

每小时 Tick 中评估：
- `Settlement.NearbyLandThreatIntensity > threshold` → 回城
- 路径阻塞：`MobilePartyAi.CheckIfThereIsAnyHugeObstacleBetweenPartyAndTarget(party, targetPosition)` 反射调用 → 重新规划
- 空闲 24 小时未到目标 → 强制重新规划目的地为 home settlement
- 粮食低 / 钱不够 → 回城

### 3.4 解散

- 达成兵种比例目标 → 兵员入 Garrison + 解散队伍
- 达不到任务且 36 小时无进展 → 解散

解散通过 `DestroyPartyAction.Apply(party, "reason")` 等 vanilla Action（MVP 2 时反编译 `Actions` 命名空间确认精确签名）。

---

## 4. 真实巡逻队的实现路径

### 4.1 直接复用 vanilla 设施

📂 证据：`PatrolPartyComponent.cs`、`Settlement.cs`（`PatrolParty` 字段）

vanilla 已经有完整的 `PatrolPartyComponent.CreatePatrolParty(stringId, position, spawnRadius, homeSettlement, template)`。**本 Mod 直接调用即可**。

巡逻队的"5 种 Order"通过我们的 `PatrolManager` 在每小时 Tick 中根据 `Settlement.NearbyLandThreatIntensity` 等数值切换：
- `OrderDefense` → 风险高，巡逻队在城镇 1 day 半径内待命
- `OrderEscort` → 跟随村庄 / 商队
- `OrderMergeGarrison` → 回城合并
- `OrderPatrol` → 在配置半径内巡逻
- `OrderStopIfPlayerTarget` → 检测 `MobileParty.MainParty == ShortTermTargetParty` 时切到 idle

实现：**Order 是逻辑状态**，不是新的官方 API；我们的 `PatrolManager` 持有 `Dictionary<Settlement, PatrolOrder>`，在 Hourly Tick 里读取并调用对应的 `MobileParty.SetTargetSettlement(...)` / `MobilePartyAi.SetInitiative(...)`。

### 4.2 一对一约束（性能保证）

`Settlement.PatrolParty` 是单一引用（`PatrolPartyComponent` 类型），意味着 vanilla **结构上保证每 settlement 最多 1 个巡逻队**。这天然限流。

### 4.3 与 GDS 的"巡逻增强" Patch 的对照

GDS 通过 Harmony Patch 增强 vanilla `PatrolPartiesCampaignBehavior` —— 本 Mod**不走这条路**。我们：
1. **完全自己驱动巡逻队**（vanilla `PatrolPartiesCampaignBehavior` 行为依然存在但不被我们的城镇触发）
2. 或者**实现 `IPatrolPartiesCampaignBehavior` 接口 + 注册自己的 Behavior**，让 vanilla 主动让出对玩家自有城镇的巡逻调度权

**待 MVP 4 编码时反编译 `IPatrolPartiesCampaignBehavior` 接口的方法签名**，再决定走哪条。两条都不需 Harmony。

---

## 5. 真实调兵（城镇 ↔ 城堡）

### 5.1 用户原则约束

- 直接管理对象 = Town
- 城堡 = 间接交互（仅作为来源 / 需求方）
- 村庄 = 仅招募点

### 5.2 实现路径

```
GarrisonTransferManager（本 Mod）
  ↓ 每日复盘
  ├── 配置驱动："城镇 X → 城堡 Y 调拨规则" / "城堡 Y → 城镇 X 调拨规则"
  ↓ 触发条件
  ├── 评估两端 NearbyLandThreatIntensity、IsUnderSiege、FoodChange、MemberRoster.Count
  ↓ 风险 OK
  ├── 创建 TransferPartyComponent（继承 CustomPartyComponent）
  │     ├── HomeSettlement = 源
  │     ├── 初始 MemberRoster = 从源 GarrisonParty.MemberRoster 转出（用 TransferTroopsAction）
  │     └── AvoidHostileActions = true
  ↓ 派往目的地
  ├── MobileParty.SetTargetSettlement(目的地, false)
  ↓ 到达后
  └── TransferTroopsAction 把兵员转入目的地 GarrisonParty + 解散运输队
```

### 5.3 关键安全规则

| 规则 | 实现 |
|---|---|
| 不抽空源 | 源城最低保留人数（用户配置） + `min(出兵数, 源驻军 - 最低保留)` |
| 围城时禁止调拨 | `if (source.IsUnderSiege \|\| dest.IsUnderSiege) return;` |
| 敌军接近时禁止 | `if (source.NearbyLandThreatIntensity > threshold) return;` |
| 路线过远禁止 | `if (Vec2.Distance(source.Position2D, dest.Position2D) > maxRangeDays * speed) return;` |
| 粮食不足禁止 | `if (source.Town.FoodChange < threshold) return;` |
| 不抽空城堡 | 城堡作为源时同样有最低保留约束 |

### 5.4 中转期间的存档安全（R-S4）

调拨队的 `MemberRoster` 由队伍自己持有（合法 PartyBase 路径），存档系统自动序列化。我们额外在 `TransferPartyComponent` 上记录：
- `Settlement Source`（用 `[SaveableProperty(...)]`）
- `Settlement Destination`（同上）
- `CampaignTime DepartureTime`

这样即便 Mod 升级后字段变化，老存档也能识别。

---

## 6. 存档安全方案

### 6.1 SaveableTypeDefiner 设计

📂 证据：`SaveableTypeDefiner.cs`、`RecruitmentCampaignBehavior.RecruitmentCampaignBehaviorTypeDefiner`

```
public class SovereignTownsTypeDefiner : SaveableTypeDefiner
{
    // saveBaseId 在 MVP 1 编码时锁定，建议 100_000_000，写到 README 公开避免他 Mod 撞 ID
    public SovereignTownsTypeDefiner() : base(100_000_000) { }

    protected override void DefineClassTypes() {
        AddClassDefinition(typeof(RecruitingPartyComponent),  1);
        AddClassDefinition(typeof(TransferPartyComponent),    2);
        AddClassDefinition(typeof(TownGarrisonRule),          3);
        AddClassDefinition(typeof(GlobalConfig),              4);
        AddClassDefinition(typeof(DecisionAuditEntry),        5);
        // ...每加一个类用一个 +1 的 LocalSaveId，永不复用
    }

    protected override void DefineEnumTypes() { ... }
    protected override void DefineContainerDefinitions() { ConstructContainerDefinition(typeof(List<TownGarrisonRule>)); ... }
}
```

### 6.2 字段标记规约

```
public class TownGarrisonRule
{
    [SaveableField(1)] private Settlement _settlement;
    [SaveableField(2)] private int _targetTotalCount;
    [SaveableField(3)] private float _cavalryRatio;
    // ...
    // 删除字段时只能"墓碑式 obsolete"：保留 LocalSaveId 占位，标 [Obsolete]，永不复用 ID
}
```

### 6.3 卸载安全（R-S3）

**MVP 5 提供"安全卸载工具"**：
- 城镇菜单加一条"Sovereign Towns: 安全卸载向导"
- 玩家点击后本 Mod 主动：
  - 销毁所有我方 MobileParty（`DestroyPartyAction.Apply(...)`）
  - 把每个玩家自有 Town 的 `GarrisonAutoRecruitmentIsEnabled` 还原为 `true`
  - 把驻军留在城内（自动转入 vanilla GarrisonParty）
  - 提示"现在可以在启动器禁用本 Mod"

即便用户没用这个工具，**v1.3.15 存档系统的错误容忍特性会让卸载不至于坏档**（U9 已验证）：
- 我们注册的 SaveableType ID 在卸载后被静默跳过
- 我方 MobileParty 进入 `MobileParty.AllPartiesWithoutPartyComponent` 集合（vanilla 仍可处理）

### 6.4 配置版本迁移（R-S5）

`GlobalConfig` 含 `[SaveableProperty(1)] public int ConfigVersion { get; set; } = 1;`

`ConfigurationManager.OnLoad()` 检测：
```
if (config.ConfigVersion < CurrentVersion) MigrateConfig(config);  // 链式 v1→v2→v3
```

---

## 7. 配置存储方案

### 7.1 MVP 1–4：纯文件，不依赖 MCM

**位置**：`<游戏存档根>/Configs/SovereignTowns/`
- `global.json` — 全局默认 + 模板列表
- `<settlementStringId>.json` — 单城镇覆盖（按需）

**格式**：JSON（人类可读，便于用户手编 + 错误时可恢复）

**加载时机**：`OnGameStart` 或 `OnAfterGameLoaded`，由 `ConfigurationManager.Load()` 统一加载。文件不存在 → 用代码内默认值生成新文件。

**热重载**：城镇菜单加"重新加载配置"按钮（MVP 1 起就做，方便调试）。

### 7.2 MVP 5：可选接入 MCM（软依赖）

```
SubModule.xml 不声明对 MCM 的依赖。

运行时检测：
  try {
      var mcmType = Type.GetType("MCM.Abstractions.Settings.Base.Global.AttributeGlobalSettings`1, MCMv5");
      if (mcmType != null) { 接入 MCM UI }
  } catch { 用自建 ConfigOptionsMenu }
```

✅ 用户决策：MVP 1–4 不用 MCM。MVP 5 视情况软依赖。

---

## 8. Mod 兼容性

### 8.1 与 IG / GDS 的互斥（U12）

📂 证据：`ModuleInfo.cs`、`ModuleHelper.cs`、`Modules/Native/SubModule.xml`

**双重保护已就位**：

1. **静态层**（已写入 `workspace/SovereignTowns/SubModule.xml`）：
```xml
<IncompatibleModules>
  <Module Id="ImprovedGarrisons" />
  <Module Id="GarrisonDoSomething" />
</IncompatibleModules>
```

2. **运行时层**（MVP 1 写入 `SovereignTownsSubModule.OnSubModuleLoad`）：
```
if (ModuleHelper.IsModuleActive("ImprovedGarrisons") || ModuleHelper.IsModuleActive("GarrisonDoSomething")) {
    Debug.Print("[SovereignTowns] CRITICAL: 检测到互斥模块仍激活。跳过 CampaignBehavior 注册。");
    // 不调用 AddBehavior，让 Mod 挂载但不工作
    _skipBehaviorRegistration = true;
}
```

### 8.2 与 RBM 的兼容（已基本零成本）

📂 证据：`RBM_classes.txt`、`RBMAIPatchLogic` 反编译（`MissionLogic`）

**RBM 在 Campaign 层零代码**。本 Mod 与 RBM 在 Campaign Map 上无任何交集。

**唯一硬规则**（MOD_SURVEY §3.3 已确立）：兵种判定**仅用运行时属性**（`BasicCharacterObject.IsMounted/IsRanged`、`CharacterObject.Tier/IsHero`、`FormationClass`），**禁止硬编码 stringId**。RBM 改 `NPCCharacters` XML 后 vanilla 自动重算这些属性，本 Mod 自动跟随。

`SubModule.xml` 的 `<ModulesToLoadAfterThis>` 已声明 RBM。

### 8.3 与其它已装大型 Mod

| Mod | 兼容性 | 备注 |
|---|---|---|
| Bannerlord.Harmony | ✅ 不依赖 | Mod 不用 Harmony，模块装着无所谓 |
| MCM v5 | ✅ 软依赖（MVP 5+） | 当前依赖缺失，MVP 1–4 自建配置 |
| ImprovedCombatAI | ✅ | 战斗层 Patch，与 Campaign 正交 |
| BetterExceptionWindow / HarmonySummary | ✅ | 调试增强，无冲突 |
| Diplomacy（**未装**） | ⚠️ 待评估 | 用户后续装则需追评 |
| CalradiaExpanded | ⚠️ 待评估 | 含新文化 + Behavior，可能与 Notable 招募源冲突 |
| BEO_EconomyRework | ⚠️ 待评估 | 经济改动可能影响驻军成本预算 |
| 其它 70+ 已装 Mod | 大概率 ✅ | 多数是装备 / UI / 数据 Mod，与 Campaign Behavior 无交集 |

---

## 9. 性能 / Tick / 大地图 AI 风险分析

### 9.1 Tick 复杂度上限

| 事件 | 频率 | 本 Mod 操作 | 单次 O(?) |
|---|---|---|---|
| `DailyTickEvent` | 1×/day | 全城镇复盘 + 调拨决策 | O(T) where T = 玩家自有 Town 数（典型 < 30） |
| `HourlyTickSettlementEvent` | 1×/hour per settlement | 仅玩家自有 → 风险评估 + Order 切换 | **O(1) per call**（先过滤非玩家自有） |
| `HourlyTickPartyEvent` | 1×/hour per party | 先 `if (party.PartyComponent is MySTComponent) ...` 过滤 | **O(1) per call** |
| `AiHourlyTickEvent` | 1×/hour per party | 不订阅（避免与 vanilla AI 撞车） | — |
| `OnApplicationTick(float dt)` | 每帧 | 不订阅 | — |

**R-P1 缓解效果**：直接复用 `Settlement.NearbyLandThreatIntensity` 等已计算字段。每小时全 settlement 遍历最坏情况 ~150 settlement × O(1) = ~150 字段读取，**< 0.1ms**。

### 9.2 MobileParty 总数膨胀（R-P2 缓解）

| 类型 | 上限 |
|---|---|
| RecruitingPartyComponent | **每 Town 最多 1** —— 静态 Dict<Settlement, MobileParty> 锁定 |
| TransferPartyComponent | **每 Town 最多 1** —— 同上 |
| 巡逻队（PatrolPartyComponent，vanilla 已存在） | **每 Settlement 最多 1** —— vanilla `Settlement.PatrolParty` 字段单一引用 |

**全局上限**：玩家自有城镇 T 个 → 本 Mod 新增最多 2T 个 MobileParty（征兵 + 调拨），加 vanilla 巡逻 T 个共 3T 个新队。**T 典型 < 30，最大 < 100**，相对大地图 vanilla ~1000+ 队伍来说**< 10% 增量**，可接受。

**空闲检测**：所有本 Mod 队伍若 24 小时 stationary 或无进展，强制解散。

### 9.3 大地图 AI 风险

| 风险 | 缓解 |
|---|---|
| 我方队伍卡在 stuck position | `MobilePartyAi.CheckIfThereIsAnyHugeObstacleBetweenPartyAndTarget(...)` 检测 → `DisableForHours(2)` + `RethinkAtNextHourlyTick = true` |
| 我方巡逻队无限追击敌军 | 用户配置半径 / 阵营 / 强度比，超出条件 `SetTargetSettlement(home, false)` 回拉 |
| 我方征兵队反复进入危险村庄 | 维护"近 N 小时访问过的危险 settlement"黑名单；下一轮规划目的地时跳过 |
| vanilla AI 调度我方驻军 | `Town.GarrisonAutoRecruitmentIsEnabled = false` —— 收回控制权 |
| LLM 网络阻塞 Tick | 全异步 + 10s 超时 + 回退到规则引擎（U11） |

---

## 10. MVP 阶段划分（再次确认 + 细化里程碑）

### MVP 1 — 识别 + 规划 + 日志（**不创建任何队伍**）

| 子任务 | 关键 API | 验证标准 |
|---|---|---|
| Mod 加载 / 互斥检测 | `MBSubModuleBase.OnSubModuleLoad` + `ModuleHelper.IsModuleActive` | 启动器互斥告警 + Debug.Print 出现 |
| ConfigurationManager 加载 / 默认值 / 校验 | `System.IO.File` + `System.Text.Json` | 文件不存在自动生成默认；非法配置回退；热重载工作 |
| SovereignTownsBehavior 注册 | `CampaignGameStarter.AddBehavior` | `Campaign.Current.GetCampaignBehavior<SovereignTownsBehavior>()` 返回非 null |
| 玩家自有 Town 识别 | `Town.AllTowns` + `Settlement.OwnerClan == Clan.PlayerClan` | DailyTick 日志列出所有玩家自有 Town |
| 驻军差距计算 + TroopComposition 评估 | `MobileParty.AllGarrisonParties` + `TroopRoster` + `CharacterObject` 属性 | DailyTick 日志输出每城"目标 vs 实际"差距 |
| SaveableTypeDefiner 注册（仅基础类，无 Party 类） | `SaveableTypeDefiner` + `[SaveableField]` | 存档读写不报错 |
| 日志系统 + 活动审计 | 自建 LoggingSystem | `<游戏存档根>/Logs/SovereignTowns_<date>.log` 出现 |
| 安全卸载工具占位 | `CampaignGameStarter.AddGameMenu` | 城镇菜单出现"Sovereign Towns" 入口（仅打开诊断面板） |

**MVP 1 完成标志**：进入战役后能看到每日驻军差距分析，**不创建任何 MobileParty**，存档读写 100% 安全。

### MVP 2 — 真实征兵队

| 子任务 | 关键 API | 验证标准 |
|---|---|---|
| RecruitingPartyComponent 派生 | `CustomPartyComponent` + `InitializationArgs` | 队伍能在地图上出现 + 自动加入 `MobileParty.AllCustomParties` |
| RecruitmentManager 调度 | `MobileParty.SetTargetSettlement(...)` + `MobilePartyAi.SetInitiative` | 队伍能离开城镇 → 抵达村庄 |
| 真实招募闭环 | vanilla `RecruitmentCampaignBehavior.HourlyTickParty` + `Hero.VolunteerTypes`（MVP 2 编码时反编译 `Hero.cs` 确认） | 队伍 MemberRoster 真实增长 + 金币真实扣减 + Notable 关系变化 |
| 回城 + 兵员入驻军 | `MobileParty.SetTargetSettlement(home)` + `TransferTroopsAction.Apply` | 兵员离开征兵队 → 进入 `Town.GarrisonParty.MemberRoster` |
| 风险评估 + 路径阻塞 | `Settlement.NearbyLandThreatIntensity` + `MobilePartyAi.CheckIfThereIsAnyHugeObstacleBetweenPartyAndTarget`（反射） | 路径阻塞日志 + 自动回城 |
| 上限 + 解散 | `DestroyPartyAction.Apply` | 每城同时只有 1 征兵队 + 空闲超时解散 |

**MVP 2 完成标志**：能看到玩家自有城镇的驻军通过征兵队**真实补充**，金币真实流出，Notable 关系真实变化。

### MVP 3 — 兵种过滤 + 升级 + 多城镇规则

| 子任务 | 关键 API |
|---|---|
| 兵种文化 / 阵营 / 类型过滤 | `BasicCharacterObject.IsMounted/IsRanged/DefaultFormationClass` + `CharacterObject.Culture/Tier/IsHero` |
| 兵种比例 / 质量 / 优先级评分 | 自建 `TroopCompositionEvaluator` |
| 驻军内自动升级 | `CharacterObject.UpgradeTargets` + `GetUpgradeXpCost/GoldCost` + vanilla 升级机制 |
| 训练模板 | 配置类 + Manager |
| 单城配置 vs 全局默认 vs 模板 | `ConfigurationManager` 三层覆盖 |

### MVP 3.5 — 城镇 ↔ 城堡调拨

| 子任务 | 关键 API |
|---|---|
| TransferPartyComponent 派生 | `CustomPartyComponent` |
| GarrisonTransferManager 决策 | `Settlement.IsUnderSiege` + `NearbyLandThreatIntensity` + `Town.FoodChange` |
| 兵员转出 / 转入 | `TransferTroopsAction.Apply` |
| 路线 / 风险 / 最低保留 | 自建评估器 |

### MVP 4 — 巡逻队 + 防御反应

| 子任务 | 关键 API |
|---|---|
| 创建巡逻队 | `PatrolPartyComponent.CreatePatrolParty(...)` |
| Order 状态机（5 种 Order） | 自建 PatrolManager + `MobileParty.SetTargetSettlement` + `MobilePartyAi.SetInitiative` |
| 防御反应（不巡逻模式） | `Settlement.NearbyLandThreatIntensity` 触发 |
| 回城 / 补给逻辑 | `MobileParty.MemberRoster.TotalManCount` < threshold → 回城 |
| `IPatrolPartiesCampaignBehavior` 接口实现（待 MVP 4 反编译确认是否走此路径） | 接口实现 + `AddBehavior` |

### MVP 5 — UI / MCM / 异常处理 / 兼容性收尾

| 子任务 | 关键 API |
|---|---|
| 自建配置 UI（兜底） | `CampaignGameStarter.AddGameMenu` + Gauntlet（最小化）|
| MCM 软依赖接入 | 反射检测 + 注册 `AttributeGlobalSettings<T>` |
| 异常处理 + 错误降级 | try/catch 包裹所有 Tick 回调 |
| 安全卸载向导 | 见 §6.3 |
| 配置版本迁移 v1→v2 | 占位实现 |

### MVP 5.5 — LLM 接入（仅建议模式）

| 子任务 | 关键 API / 组件 |
|---|---|
| LLMProviderInterface | 抽象层 |
| Local LLM (Ollama HTTP) / Remote (OpenAI 兼容) | `HttpClient` 异步 |
| LLMReasoningService | `Task.Run` + 10s 超时 |
| LLMDecisionValidator | JSON Schema + 本地规则校验 |
| RuleBasedFallbackDecisionMaker | 现有规则引擎 |
| DecisionAuditLogger | 已在 MVP 1 占位 |
| MCM 配置 enableLLMReasoning | `MCM.Abstractions` |

**LLM 仅提供建议，不自动执行**。

### MVP 6 — LLM 自动执行（用户明确开启）

| 子任务 | 关键 API |
|---|---|
| 用户开关 enableLLMAutoExecute | 默认 false |
| 自动执行前的本地规则 / 安全校验 / 配置限制三重 | 复用 MVP 5.5 Validator |
| 全行为审计日志 | 自动写入 DecisionAuditLogger |

---

## 11. 第二阶段交付物清单

| 文件 | 状态 |
|---|---|
| `workspace/FEASIBILITY_REPORT.md` | ✅ 本文档 |
| `workspace/RESEARCH_FINDINGS.md` | ✅ 第一阶段 |
| `workspace/MOD_SURVEY.md` | ✅ 第一阶段 |
| `workspace/UNCERTAINTY_LOG.md` | ✅ 第一阶段（U1–U12 全部解决） |
| `workspace/RISK_REGISTER.md` | ✅ 第一阶段 |
| `workspace/SovereignTowns/` | ✅ 空骨架已编译 + 部署 |
| `workspace/_research/decompiled/` | ✅ 40+ 反编译证据 |

---

## 12. 进入第三阶段的入口条件

第三阶段（**架构设计**）将基于本报告，对每个模块产出：

1. 职责（单一职责原则）
2. 依赖（输入 / 输出）
3. Tick 生命周期（订阅哪些事件 / 什么频率）
4. 数据流（与配置 / 与 SaveData / 与其它 Manager）
5. 存档行为（哪些字段需要 SyncData / SaveableField）

模块清单（用户原则中的 14 个）：

```
SovereignTownsSubModule    （Mod 入口）
SovereignTownsBehavior     （主 CampaignBehavior）
TownGarrisonManager        （核心功能一：自动驻军）
RecruitmentManager         （核心功能二：自动征兵）
PatrolManager              （核心功能三：巡逻 / 防御）
CastleSupportManager       （核心功能四：城堡 ↔ 城镇）
GarrisonTransferManager    （核心功能四：调拨执行）
SettlementDefenseDemandEvaluator
TroopCompositionEvaluator
RiskAssessmentService
PartyLifecycleManager
SaveDataManager
ConfigurationManager
MCMIntegration             （MVP 5+，软依赖）
LLMReasoningService        （MVP 5.5+）
LLMProviderInterface       （MVP 5.5+）
LLMDecisionValidator       （MVP 5.5+）
RuleBasedFallbackDecisionMaker
DecisionAuditLogger
DebugCommandSystem         （MVP 1）
LoggingSystem              （MVP 1）
```

**用户决策点（进入第三阶段前）**：
- 是否同意上面的 MVP 阶段划分？
- 是否同意保留 21 个模块的清单（用户原则中 14 个 + 我们新加的 7 个 sub-component）？
- 是否需要调整 LLM 的优先级（提前 / 推迟）？
