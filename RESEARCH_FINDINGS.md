# 第一阶段调查产出：RESEARCH_FINDINGS.md

> **版本锚定**：Mount & Blade II: Bannerlord **v1.3.15**（Native 模块 `<Version value="v1.3.15"/>`）
> 安装路径：`D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`
> 调查日期：2026-05-12
> 反编译产物：`_research/decompiled/*.cs`（ilspycmd 8.1.0.7455）

本文件只记录**已经在 v1.3.15 反编译中亲眼看到的真实 API 与 attribute**。每条结论都标注"📂 证据：<反编译文件路径> 或 <SubModule.xml 路径>"。

---

## 0. 运行时基线（关键变化，影响 csproj 配置）

| 项 | v1.3.15 实测 | 说明 |
|---|---|---|
| Target Runtime | **System.Runtime 引用**（不再是 mscorlib） | TaleWorlds 已迁出 .NET Framework 4.7.2。**不要再按 `net472` 建项目**；按 BUTR `Bannerlord.ReferenceAssemblies` NuGet 的当前推荐 TFM（应为 `net6` 或 `netstandard2.1`，需在引入 NuGet 时再确认） |
| TaleWorlds DLL AssemblyVersion | 全部 1.0.0.0 | TW 不更新 AssemblyVersion，引用稳定 |
| PublicKeyToken | 空（unsigned） | 正常 |
| .NET SDK on host | dotnet 9.0.1025 | 工具链充足 |
| 反编译工具 | ilspycmd 8.1.0.7455（ICSharpCode.Decompiler 同版） | 全部签名通过该工具读取 |

---

## 1. 模块/插件依赖（已装 vs 缺失）

📂 证据：`D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\*\SubModule.xml`

| Module | 状态 | 版本 | 备注 |
|---|---|---|---|
| Native | ✅ 已装 | v1.3.15 | 主模块 |
| **Bannerlord.Harmony** | ✅ 已装 | **v2.4.2.225** | 含 `0Harmony.dll`、`Mono.Cecil.dll`、`MonoMod.*` |
| **Bannerlord.MBOptionScreen (MCM v5)** | ⚠️ 已装但**无法启动** | **v5.11.4** | 有针对 v1.3.15 的专属 dll `Bannerlord.MBOptionScreen.v1.3.15.dll`，但**硬依赖 ButterLib / UIExtenderEx 缺失** |
| **Bannerlord.ButterLib** | ❌ **缺失** | — | MCM 硬依赖（DependentVersion v2.10.2+） |
| **Bannerlord.UIExtenderEx** | ❌ **缺失** | — | MCM 硬依赖（DependentVersion v2.13.1+） |
| **Bannerlord.BLSE** | ❌ 未装 | — | 社区启动器，非强制 |
| RBM | ✅ 已装 | **v4.2.23** | 主要覆盖 `Items`、`CraftingPieces`、**`NPCCharacters`(兵种 overhaul)**、`ItemModifiers`、`SiegeEngines`、`SkillSets` |
| ImprovedGarrisons / GarrisonDoSomething / Governors Handle Issues / ReinforcementSystem / SupplyLines / LordRetinueUptier / DeReMilitari-PlayerKingdomTemplate / PartyScreenEnhancements / TroopDesigner 等 | ✅ 已装 | 各自 SubModule 待查 | 参考 Mod，可反编译学习 |

### ⚠️ 必须提请用户决策

MCM 当前**实际不可启动**（ButterLib + UIExtenderEx 缺失）。两个选项：

- **A. 补齐依赖**：从 NexusMods/ButterLib 与 UIExtenderEx 各下一份装入 `Modules/`，让 MCM 跑起来。
- **B. 第一版先用 XML/JSON 兜底**：本 Mod 配置走自定义文件，不接 MCM。MVP 5 再接 MCM。

📌 **建议 B 在前**：MVP 5 接 MCM；MVP 1–4 不依赖它，可降低早期联调复杂度。

---

## 2. 已验证 API 清单（v1.3.15 实测签名）

### 2.1 CampaignBehavior 注册与生命周期

📂 证据：`_research/decompiled/CampaignBehaviorBase.cs`、`CampaignGameStarter.cs`

```csharp
// CampaignBehaviorBase.cs (TaleWorlds.CampaignSystem)
public abstract class CampaignBehaviorBase : ICampaignBehavior {
    public readonly string StringId;
    public CampaignBehaviorBase(string stringId);
    public CampaignBehaviorBase();                    // StringId = GetType().Name
    public abstract void RegisterEvents();
    public abstract void SyncData(IDataStore dataStore);
    public static T GetCampaignBehavior<T>();
}

// CampaignGameStarter.cs
public class CampaignGameStarter : IGameStarter {
    public ICollection<CampaignBehaviorBase> CampaignBehaviors { get; }
    public void AddBehavior(CampaignBehaviorBase campaignBehavior);
    public void AddModel(GameModel gameModel);
    public void AddGameMenu(string menuId, string menuText, OnInitDelegate initDelegate, GameMenu.MenuOverlayType overlay = ..., GameMenu.MenuFlags = ...);
    public void AddWaitGameMenu(string idString, string text, OnInitDelegate, OnConditionDelegate, OnConsequenceDelegate, OnTickDelegate, GameMenu.MenuAndOptionType, ...);
    public void AddGameMenuOption(string menuId, string optionId, string optionText, GameMenuOption.OnConditionDelegate, GameMenuOption.OnConsequenceDelegate, bool isLeave = false, int index = -1, ...);
    public void AddDialogFlow(DialogFlow, object relatedObject = null);
    public ConversationSentence AddPlayerLine(...); 
    public ConversationSentence AddDialogLine(...);
}
```

**结论 — 注册路径已锁定**：在 `MBSubModuleBase.OnGameStart(...)` 里拿到 `CampaignGameStarter`，调用 `AddBehavior(new XxxBehavior())`。

### 2.2 CampaignEvents（事件总线）— 关键事件目录

📂 证据：`_research/decompiled/CampaignEvents.cs`，共 **274 个 public/internal `IMbEvent<...>` getter**，全部通过 `Instance._xxxEvent` 表达式暴露。

事件订阅模式：
```csharp
CampaignEvents.<EventName>.AddNonSerializedListener(this, handlerDelegate);
```

#### 与本 Mod 相关的关键事件（部分清单）：

| 事件名 | 用途 |
|---|---|
| `DailyTickPartyEvent`、`DailyTickTownEvent`、`DailyTickSettlementEvent`、`DailyTickHeroEvent`、`DailyTickClanEvent` | 日 Tick — 用于每日复盘 |
| `HourlyTickPartyEvent`、`HourlyTickSettlementEvent`、`HourlyTickClanEvent` | 小时 Tick — 用于风险评估、路径决策 |
| `AiHourlyTickEvent` | AI 思考 Tick |
| `MobilePartyCreated` | 任何 MobileParty 创建 — 用于发现非我方队伍 |
| `OnPartyLeaderChangedEvent` | 队伍领主变化 |
| `OnSettlementLeftEvent`、`SettlementEntered`、`AfterSettlementEntered`、`BeforeSettlementEntered` | 进出定居点 |
| `OnTroopGivenToSettlementEvent` | **关键** — 兵员被给到定居点（驻军真实写入路径） |
| `BattleStarted`、`MapEventStarted` | 战斗 / 地图事件开始 |
| `OnMobilePartyJoinedToSiegeEvent`、`OnMobilePartyLeftSiegeEvent` | 围城 |
| `CanMoveToSettlementEvent` | **关键** — 允许 Mod 否决"前往某 settlement"的 AI 决策 |
| `OnPartyAddedToMapEventEvent` | 队伍加入地图事件 |
| `VillageStateChanged`、`VillageLooted` | 村庄被劫掠（招募来源关心） |
| `MercenaryTroopChangedInTown`、`MercenaryNumberChangedInTown` | **关键** — 城镇佣兵槽变化（招募的真实信号） |
| `BanditPartyRecruited` | 山贼招募事件 |
| `RebellionFinished` | 反叛 |
| `OnClanLeaderChangedEvent`、`ClanChangedKingdom` | 阵营变化 |
| `OnHideoutSpottedEvent`、`OnHideoutBattleCompletedEvent` | 山贼基地 |

⚠️ **完整事件清单**有 274 项，超出本文档范围。源文件已保留：`_research/decompiled/CampaignEvents.cs`，需要哪个事件时按需查。

### 2.3 真实招募 API（解决 U3）

📂 证据：`_research/decompiled/RecruitmentCampaignBehavior.cs`、`GarrisonRecruitmentCampaignBehavior.cs`

```csharp
public class RecruitmentCampaignBehavior : CampaignBehaviorBase {
    public class RecruitmentCampaignBehaviorTypeDefiner : SaveableTypeDefiner { ... }   // ← 官方 TypeDefiner 范例
    public class TownMercenaryData { ... }   // 公开嵌套类
    public enum RecruitingDetail { ... }
    public override void RegisterEvents();
    public override void SyncData(IDataStore dataStore);
    public TownMercenaryData GetMercenaryData(Town town);                 // ← 查询招募数据
    public void HourlyTickParty(MobileParty mobileParty);                 // ← 招募实际推进点（小时 Tick）
    public void OnBeforeSettlementEntered(MobileParty, Settlement, Hero); // ← 队伍进城时招募评估
}

public class GarrisonRecruitmentCampaignBehavior : CampaignBehaviorBase, IGarrisonRecruitmentBehavior {
    public struct VolunteerTroop : IComparable { ... }
    public override void RegisterEvents();
    public ExplainedNumber GetGarrisonChangeExplainedNumber(Town town); // ← 官方计算驻军变化的解释模型
    public override void SyncData(IDataStore dataStore);
}
```

**关键发现**：`IGarrisonRecruitmentBehavior` 与 `IPatrolPartiesCampaignBehavior` 是**官方接口**。意味着可以通过 **实现自己的 IGarrisonRecruitmentBehavior + 注册自己的 Behavior**，**完全不用 Harmony Patch** 就替换官方行为。这是最佳切入点。

### 2.4 MobileParty / PartyComponent 创建路径（解决 U1）

📂 证据：`_research/decompiled/PartyComponent.cs`、`PatrolPartyComponent.cs`、`GarrisonPartyComponent.cs`、`CustomPartyComponent.cs`、`MobilePartyHelper.cs`、`MobileParty.cs`

```csharp
// PartyComponent.cs — 抽象基类
public abstract class PartyComponent {
    public delegate void OnPartyComponentCreatedDelegate(MobileParty mobileParty);
    public MobileParty MobileParty { get; private set; }
    public PartyBase Party => MobileParty.Party;
    public abstract Hero PartyOwner { get; }
    public abstract TextObject Name { get; }
    public abstract Settlement HomeSettlement { get; }
    public virtual bool AvoidHostileActions => false;
    public virtual int WagePaymentLimit => Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit;
    public virtual Hero Leader => null;
}

// PatrolPartyComponent.cs — 官方巡逻队工厂
public class PatrolPartyComponent : PartyComponent {
    public static MobileParty CreatePatrolParty(string stringId, CampaignVec2 position, float spawnRadius, Settlement homeSettlement, PartyTemplateObject template);
}

// GarrisonPartyComponent.cs — 官方驻军队工厂
public class GarrisonPartyComponent : PartyComponent {
    public static MobileParty CreateGarrisonParty(string stringId, Settlement settlement);
    public static void ConvertPartyToGarrisonParty(MobileParty mobileParty, Settlement settlement);
}

// CustomPartyComponent.cs — Mod 用的自定义队（带 InitializationArgs）
public class CustomPartyComponent : PartyComponent {
    protected class InitializationArgs {
        public CampaignVec2 Position;
        public float SpawnRadius;
        public Clan Clan;
        public TroopRoster TroopRoster;
        public TroopRoster PrisonerRoster;
        public PartyTemplateObject PartyTemplate;
        public bool IsCreatedWithPartyTemplate => PartyTemplate != null;
        public InitializationArgs(CampaignVec2 position, float spawnRadius, Clan clan, PartyTemplateObject partyTemplate);
        public InitializationArgs(CampaignVec2 position, float spawnRadius, Clan clan, TroopRoster troopRoster, TroopRoster prisonerRoster);
        public void InitializeCustomPartyPropertiesWithPartyTemplate(MobileParty mobileParty);
        public void InitializeCustomPartyPropertiesWithTroopRoster(MobileParty mobileParty);
    }
    protected CustomPartyComponent(Settlement homeSettlement, TextObject name, Hero owner, string partyMountStringId, string partyHarnessStringId, float customPartyBaseSpeed, bool avoidHostileActions, InitializationArgs args, Hero leader = null);
}

// MobilePartyHelper.cs
public static class MobilePartyHelper {
    public static MobileParty SpawnLordParty(Hero hero, Settlement spawnSettlement);
    public static MobileParty SpawnLordParty(Hero hero, CampaignVec2 position, float spawnRadius);
    public static MobileParty CreateNewClanMobileParty(Hero hero, Clan clan);
    public static void FillPartyManuallyAfterCreation(MobileParty mobileParty, PartyTemplateObject partyTemplate, int desiredMenCount);
}

// MobileParty.cs — 全局枚举与控制
public sealed class MobileParty : CampaignObjectBase, ... {
    public static MBReadOnlyList<MobileParty> All { get; }
    public static MBReadOnlyList<MobileParty> AllPatrolParties { get; }
    public static MBReadOnlyList<MobileParty> AllGarrisonParties { get; }
    public static MBReadOnlyList<MobileParty> AllMilitiaParties { get; }
    public static MBReadOnlyList<MobileParty> AllVillagerParties { get; }
    public static MBReadOnlyList<MobileParty> AllCustomParties { get; }
    public static MBReadOnlyList<MobileParty> AllLordParties { get; }
    public static MBReadOnlyList<MobileParty> AllCaravanParties { get; }
    public static MBReadOnlyList<MobileParty> AllBanditParties { get; }
    public static MBReadOnlyList<MobileParty> AllPartiesWithoutPartyComponent { get; }
    public PartyComponent PartyComponent { get; }              // 反向访问
    public PatrolPartyComponent PatrolPartyComponent { get; }
    public GarrisonPartyComponent GarrisonPartyComponent { get; }
    public bool IsPatrolParty, IsGarrison, IsCustomParty, IsMilitia, IsLordParty, IsVillager, IsCaravan, IsBandit;
    public TroopRoster MemberRoster { get; }
    public TroopRoster PrisonRoster { get; }
    public ItemRoster ItemRoster { get; }
    public Settlement TargetSettlement { get; }
    public Settlement BesiegedSettlement { get; }
    public Hero Owner { get; }
    public Hero LeaderHero { get; }
    public bool IsActive { get; }
    public MapEvent MapEvent { get; }
    public void SetCustomHomeSettlement(Settlement customHomeSettlement);
    public void SetTargetSettlement(Settlement settlement, bool isTargetingPort);
    public void SetWagePaymentLimit(int newLimit);
    public PartyObjective Objective { get; set; }
    public MobilePartyAi Ai { get; }
}
```

**U1 结论**：**不要凭空 `new MobileParty`**。我们的"征兵队 / 巡逻队 / 调拨队"必须通过 `PartyComponent` 模式接入：
- **巡逻队** → 直接用 `PatrolPartyComponent.CreatePatrolParty(...)`（这就是游戏内官方巡逻队走的路径）
- **征兵队 / 调拨队** → 自定义一个继承 `CustomPartyComponent` 的类，用其 `InitializationArgs` 创建
- 全部以 `Settlement homeSettlement` 为锚点 — 这天然解决了"归属哪个城镇"的问题

### 2.5 Settlement / Town / Village 关键字段

📂 证据：`_research/decompiled/Settlement.cs`、`Town.cs`

```csharp
public sealed class Settlement : MBObjectBase, ILocatable<Settlement>, IMapPoint, ... {
    public PartyBase Party { get; }
    public bool IsActive { get; }
    public Hero Owner { get; }                           // ← 玩家城镇判定：Settlement.Owner == Hero.MainHero
    public IFaction MapFaction { get; }                  // ← 阵营过滤
    public MBReadOnlyList<MobileParty> Parties { get; }   // ← 当前在此 settlement 的所有 MobileParty
    public PatrolPartyComponent PatrolParty { get; }      // ← 当前巡逻队 component（一对一）
    public MBReadOnlyList<Hero> Notables { get; }         // ← 城镇贵族（招募来源）
    public bool IsTown, IsCastle, IsVillage, IsHideout { get; }
    public bool IsUnderSiege { get; }
    public MBReadOnlyList<Village> BoundVillages { get; } // ← 所属村庄（招募范围、补给来源）
    public float NearbyLandThreatIntensity { get; }       // ★ 官方风险评估数值
    public float NearbyNavalThreatIntensity { get; }      // ★
    public float NearbyLandAllyIntensity { get; }         // ★ 周边友军强度
    public float NearbyNavalAllyIntensity { get; }        // ★
    public CampaignTime LastThreatTime { get; }
    public int GarrisonWagePaymentLimit { get; }
    public void SetGarrisonWagePaymentLimit(int limit);
    public bool IsSettlementBusy(object asker, int limitingPriority);
    public static MBReadOnlyList<Settlement> All { get; }
}

public class Town : Fief {
    public bool GarrisonAutoRecruitmentIsEnabled = true;  // ★ 关键 — 官方自带"自动招募驻军"开关
    public bool IsTown { get; }
    public bool IsCastle { get; }
    public bool IsUnderSiege { get; }
    public Clan LastCapturedBy { get; }
    public MBReadOnlyList<Village> Villages { get; }      // 隶属村庄
    public IEnumerable<PartyBase> GetDefenderParties(MapEvent.BattleTypes battleType);
    public int GetWallLevel();
    public IReadOnlyCollection<SellLog> SoldItems { get; }
    public TownMarketData MarketData { get; }
    public static MBReadOnlyList<Town> AllTowns { get; }
    public static MBReadOnlyList<Town> AllCastles { get; }
    public float MilitiaChange { get; }
    public ExplainedNumber MilitiaChangeExplanation { get; }
    public float FoodChange { get; }                      // ★ 粮食安全阈值的数据源
    public float LoyaltyChange { get; }
    public float SecurityChange { get; }
}
```

**U2 结论**：
- 玩家自有城镇 = `Settlement.All.Where(s => s.IsTown && s.OwnerClan == Clan.PlayerClan)`（或更精确：`s.MapFaction == Clan.PlayerClan.MapFaction && s.IsTown`，看政治状态）
- 驻军（Garrison MobileParty）通过 `MobileParty.AllGarrisonParties` 枚举，并由 `GarrisonPartyComponent.Settlement` 反向定位
- **必须考虑** `Town.GarrisonAutoRecruitmentIsEnabled` —— 官方自带的自动招募若与我们逻辑并存会**双重招募**；建议方案：本 Mod 接管的城镇将其设为 `false`，由我们的 `RecruitmentManager` 全权负责
- 风险评估直接复用 `Settlement.NearbyLandThreatIntensity` / `NearbyLandAllyIntensity` — **不用自己重写**
- 粮食阈值用 `Town.FoodChange` / `Town.MarketData`

### 2.6 TroopRoster（驻军写入的最终通道）

📂 证据：`_research/decompiled/TroopRoster.cs`

```csharp
public class TroopRoster : ISerializableObject {
    internal PartyBase OwnerParty;                 // ← internal — 不允许外部直接挂载，必须通过 PartyBase 既有 roster
    public int Count, TotalRegulars, TotalHeroes, TotalWounded, TotalManCount, TotalHealthyCount;
    public int AddToCounts(CharacterObject character, int count, bool insertAtFront = false, int woundedCount = 0, int xpChange = 0, bool removeDepleted = true, int index = -1);
    public int AddToCountsAtIndex(int index, int countChange, int woundedCount = 0, int xpChange = 0, bool removeDepleted = true);
    public void RemoveTroop(CharacterObject troop, int numberToRemove = 1, UniqueTroopDescriptor seed = default, int xp = 0);
    public void Add(TroopRoster other);            // ← 合并 roster
    public TroopRoster CloneRosterData();
    public void Clear();
    // 其他 Set/Get 索引器、版本号、洗牌、计数缓存方法
}
```

⚠️ **重要边界**：`AddToCounts(...)` 是合法的官方 API。但**直接 AddToCounts 一名兵到 `Town.GarrisonParty.MemberRoster`**就**绕过了招募经济**（不会扣 Notable 关系、不消耗村庄招募槽位、不付招募金）。我们的"真实招募"必须先走 `Notable.VolunteerTypes` / `Settlement.Notables` 的合法消耗路径，再把转换后的兵员通过 `OnTroopGivenToSettlementEvent` 或 `TransferTroopsAction` 写入驻军（这些 Action 类待第二阶段补查）。

### 2.7 自定义存档（SaveSystem）

📂 证据：`_research/decompiled/SaveableTypeDefiner.cs`、`SaveableFieldAttribute.cs`、`SaveablePropertyAttribute.cs`、`RecruitmentCampaignBehavior.cs` 中的内嵌 `RecruitmentCampaignBehaviorTypeDefiner` 范例

```csharp
namespace TaleWorlds.SaveSystem;

public abstract class SaveableTypeDefiner {
    protected SaveableTypeDefiner(int saveBaseId);    // ← 必须传一个"基准 ID"，本 Mod 的 saveBaseId 必须固定一次，不能改
    protected internal virtual void DefineBasicTypes();
    protected internal virtual void DefineClassTypes();
    protected internal virtual void DefineStructTypes();
    protected internal virtual void DefineInterfaceTypes();
    protected internal virtual void DefineEnumTypes();
    protected internal virtual void DefineRootClassTypes();
    protected internal virtual void DefineGenericClassDefinitions();
    protected internal virtual void DefineGenericStructDefinitions();
    protected internal virtual void DefineContainerDefinitions();
    protected internal virtual void DefineConflictResolvers();
    protected void ConstructGenericClassDefinition(Type type);
    protected void ConstructGenericStructDefinition(Type type);
}

[AttributeUsage(AttributeTargets.Field)]
public class SaveableFieldAttribute : Attribute {
    public short LocalSaveId { get; set; }
    public SaveableFieldAttribute(short localSaveId);
}

[AttributeUsage(AttributeTargets.Property)]
public class SaveablePropertyAttribute : Attribute {
    public short LocalSaveId { get; set; }
    public SaveablePropertyAttribute(short localSaveId);
}
```

**U4 结论 — 存档机制锁定**：
- 创建一个 `class MyModTypeDefiner : SaveableTypeDefiner { public MyModTypeDefiner() : base(<固定 int>) {} }`
- `<固定 int>` 范围社区习俗是从 `1_000_000` 起步避开 Native（**第二阶段必须查证 Native 实际占用范围**）
- 在 `DefineClassTypes()` / `DefineEnumTypes()` 里注册我们的类
- 类字段用 `[SaveableField(0)]`、属性用 `[SaveableProperty(1)]`，**LocalSaveId 在类内必须唯一且永远不变**
- 注册位置：在 `MBSubModuleBase.OnSubModuleLoad()` 里通过 `MBObjectManager` 注册（具体 API 第二阶段补）

📌 **不需要 Harmony**：存档机制是官方公开扩展点，无任何 Patch 需求。

### 2.8 v1.3.15 中可参考的"官方 CampaignBehavior 实现"清单

📂 证据：`_research/decompiled/CampaignSystem_classes.txt`（`TaleWorlds.CampaignSystem.CampaignBehaviors.*` 共 **347 个类型**）

与本 Mod 直接相关的官方 Behavior（用作"如何写"的活范本）：

| 官方 Behavior | 我们需要学的东西 |
|---|---|
| `RecruitmentCampaignBehavior` | 招募真实推进 |
| `GarrisonRecruitmentCampaignBehavior` | 驻军招募 + 实现 `IGarrisonRecruitmentBehavior` |
| `PatrolPartiesCampaignBehavior` | 巡逻队生命周期（生成/补员/调度/解散）+ 实现 `IPatrolPartiesCampaignBehavior` |
| `MilitiasCampaignBehavior` | 民兵管理（参考但不接管） |
| `VillagerCampaignBehavior` | 村民队的派遣 / 路径 |
| `NotablesCampaignBehavior`、`NotablePowerManagementBehavior`、`NotableHelperCharacterCampaignBehavior`、`NotableSupportersCampaignBehavior` | Notable 体系（招募源） |
| `PrisonerRecruitCampaignBehavior`、`RecruitPrisonersCampaignBehavior` | 俘虏转化 |
| `AiBehaviors.AiVisitSettlementBehavior` | 让自定义 MobileParty 前往 settlement 的官方 AI 行为 |
| `AiBehaviors.AiPatrollingBehavior` | 巡逻 AI 决策 |
| `AiBehaviors.AiEngagePartyBehavior` | 接战 AI |
| `AiBehaviors.AiPartyThinkBehavior` | "想干嘛" AI 评估器 |
| `AiBehaviors.AiMilitaryBehavior` | 军事 AI |
| `AlliancesCampaignBehavior` / `BanditInteractionsCampaignBehavior` | 阵营互动 |

⚠️ **极强复用前提**：第二阶段对每个上面这些类做单独反编译（同样落到 `_research/decompiled/`），把它们的 `RegisterEvents` 订阅链与 Tick 处理流程拷下来作为我们 Manager 的模板。

---

## 3. RBM 兼容性专项发现（响应"Mod 完整兼容 RBM"）

📂 证据：`D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RBM\SubModule.xml`

### 3.1 RBM 实际影响范围

RBM v4.2.23 在 SubModule.xml 中声明的 XmlNode 覆盖：

| XML 类别 | 节点 |
|---|---|
| `Items` | 武器（ranged / siege_ranged / lances / gladius）、护甲（body / shoulder / arm / leg / head / horse / shields）、马匹、视觉效果 |
| `CraftingPieces` | sword / mace / axe / lance pieces |
| `CraftingTemplates` | no_bastard_axes |
| **`NPCCharacters`** | **`RBMCombat_unit_overhaul`** — **覆盖兵种定义**（装备、技能、文化关系） |
| `SiegeEngines` | 攻城器 |
| `WeaponDescriptions` | 武器分类 |
| `ItemModifiers` | 物品修饰符 |
| `SkillSets` | 技能集 |
| `GameText` | UI 文本 |

### 3.2 与本 Mod 的兼容点 / 冲突点

| 我们的模块 | 与 RBM 的关系 |
|---|---|
| **兵种文化过滤、兵种比例、贵族兵识别** | ⚠️ **RBM 覆盖了 NPCCharacters**。我们必须只通过 `CharacterObject.Culture`、`CharacterObject.Occupation`、`CharacterObject.Tier`、`CharacterObject.IsHero` 等运行时属性判断，**不能** 写死兵种 stringId。这样 RBM 改了兵种内部装备也不影响我们。 |
| **驻军规则、目标兵种比例** | ✅ 兼容 — 我们的"骑兵/步兵/弓兵"分类应当用 `CharacterObject` 的运行时分类方法（`IsRanged`、`IsMounted` 等，第二阶段补查具体属性名），不写死 stringId |
| **真实招募** | ✅ 兼容 — 我们走 `Notable.VolunteerTypes`、`RecruitmentCampaignBehavior` 这些上游逻辑，RBM 不动这些 |
| **巡逻 / 调拨 / 风险评估** | ✅ 兼容 — 走 `Settlement.NearbyLandThreatIntensity` 等运行时数值，与 RBM 战斗 overhaul 无交集 |
| **MapEvent / 战斗模拟** | ⚠️ RBM 改战斗系统，但我们的征兵队 / 巡逻队若被卷入 MapEvent，**自动模拟**走 RBM 的模拟。**不需要我们做特殊处理**，但测试时必须验证 |
| **存档加载顺序** | ⚠️ 必须 `LoadAfterThis: RBM` —— 本 Mod 在 SubModule.xml 里把 RBM 列入 `<ModulesToLoadAfterThis>` 或 `DependedModuleMetadatas optional=true`，避免数据加载顺序问题 |

### 3.3 RBM 兼容性硬规则（写入 MOD_SURVEY.md / 编码规范）

1. **禁止硬编码兵种 stringId**。所有"兵种"判定走 `CharacterObject` 运行时属性。
2. **禁止假设兵种装备**。装备过滤如有需要，从 `CharacterObject.Equipment` / `BattleEquipments` 实时读。
3. **加载顺序**：`<ModulesToLoadAfterThis>` 加 RBM、SandBox、SandBoxCore、Native、StoryMode。
4. **测试矩阵必须包含 RBM 启用与禁用两种场景**。
5. **不要 Patch 任何 RBM 也 Patch 的方法**（具体清单第二阶段反编译 `RBM.dll` 后定）。

---

## 4. 已解决 / 未解决的不确定点（U1–U11）

| # | 不确定点 | 结论 | 证据 |
|---|---|---|---|
| **U1** | 无主 MobileParty 是否合法 | ✅ **已解决** — 不应"无主"。用 `PartyComponent` 模式：巡逻=`PatrolPartyComponent.CreatePatrolParty`；自建=继承 `CustomPartyComponent`；以 `Settlement homeSettlement` 为锚点 | §2.4 |
| **U2** | GarrisonParty 写入路径 | ✅ **已解决** — `MobileParty.AllGarrisonParties` 枚举 + `GarrisonPartyComponent.Settlement` 反查；驻军写入兵员的合法路径是触发 `OnTroopGivenToSettlementEvent` 链（具体 Action 类第二阶段补） | §2.5、§2.6 |
| **U3** | 真实招募 API | ✅ **已解决** — `RecruitmentCampaignBehavior.HourlyTickParty(...)` + `Notable.VolunteerTypes`；实现 `IGarrisonRecruitmentBehavior` 接口可不用 Harmony | §2.3 |
| **U4** | 自定义存档边界 | ✅ **已解决** — `SaveableTypeDefiner` + `[SaveableField]/[SaveableProperty]`；`saveBaseId` 取大数避碰 Native | §2.7 |
| **U5** | CampaignTime 调度精度 | ✅ **已解决** — 日 Tick + 小时 Tick + AiHourlyTick 全部公开，足够 | §2.2 |
| **U6** | Party AI 路径阻塞 | ⏳ **未完成** — 需第二阶段反编译 `MobilePartyAi` + `AiVisitSettlementBehavior` 看路径失败时的回退 | — |
| **U7** | 围城 / 战时事件触发器 | ✅ **已解决** — `OnMobilePartyJoinedToSiegeEvent` / `OnMobilePartyLeftSiegeEvent` / `MapEventStarted` / `Settlement.IsUnderSiege` | §2.2、§2.5 |
| **U8** | 兵种文化 / 阵营过滤的数据源 | ⏳ **部分** — 已知 `CharacterObject.Culture` / `CharacterObject.Occupation` 公开；具体 `IsMounted` / `IsRanged` 等运行时属性需第二阶段反编译 `CharacterObject` 确认 | — |
| **U9** | 卸载 Mod 后存档处置 | ⏳ **未完成** — TaleWorlds 的"未知 SaveableType"恢复机制需第二阶段反编译 `DefinitionContext` / `SaveContext` | — |
| **U10** | MCM 在 v1.3.15 可用性 | ✅ **已解决** — MCM v5.11.4 自身已为 v1.3.15 编译适配 dll；**但其依赖 ButterLib + UIExtenderEx 缺失，当前实际不可用**。建议第一版用 XML/JSON 兜底，MVP 5 再接 MCM | §1 |
| **U11** | LLM 网络阻塞 Tick | ⏳ **设计层确定** — 必须用 `HttpClient` + `Task.Run` 异步；C# 8+ 已就绪。具体实现 MVP 5.5 再细化 | — |

---

## 5. 哪些功能仍可能需要 Harmony Patch（修正）

| 功能 | 之前预判 | 现在结论 |
|---|---|---|
| 监听城镇所有者变化 | 否 | ✅ 否（CampaignEvents 自带） |
| 创建自定义 MobileParty | "待验证" | ✅ **完全否**（PartyComponent 工厂） |
| 修改驻军 TroopRoster | "待验证" | ✅ **否**（TroopRoster.AddToCounts 公开，但仍需配合 Action 链以避免破坏经济） |
| 真实招募流程嵌入 | "可能否" | ✅ **否**（实现 `IGarrisonRecruitmentBehavior` 接口 + 替换 Behavior） |
| 阻止驻军被官方 AI 抽空 | "可能需要" | ⚠️ **可能否** — 通过把 `Town.GarrisonAutoRecruitmentIsEnabled = false` 收回控制权；如果 `MobilePartyAi` 仍会调度驻军则需 Patch（第二阶段查证） |
| 自定义 Party 不被官方征兵 AI 接管 | "可能需要" | ⚠️ 需观察 `MobilePartyAi` + `AiVisitSettlementBehavior` 行为；`PartyComponent.AvoidHostileActions = true` 可能足够 |
| 围城时禁止调拨 | 否 | ✅ 否（`Settlement.IsUnderSiege`） |
| MCM 接入 | 否 | ✅ 否（MCM 自有 API） |
| LLM 接入 | 否 | ✅ 否 |

**当前判断**：本 Mod 的所有核心功能**可能完全不需要 Harmony**。若有需要，预计仅限 1–2 个点（`MobilePartyAi` 调度的拦截），第二阶段反编译后再决定。

---

## 6. 风险登记修订（详细 → RISK_REGISTER.md）

仅列出**已被新证据修订**的项：

| 风险 | 旧预判 | 修订 |
|---|---|---|
| **R-P1**（每小时遍历 settlement × party） | "O(N×M)" | ✅ 缓解 — 复用 `Settlement.NearbyLandThreatIntensity` 等游戏自身已计算的数据；不必我们重算 |
| **R-P2**（MobileParty 总数膨胀） | "硬上限" | ⚠️ 仍存 — 必须设置每城镇 1 征兵队 + 1 巡逻队（巡逻队由 `PatrolPartyComponent` 一对一保证），但调拨队需要单独限流 |
| **R-S3**（卸载 Mod 后孤儿 MobileParty） | "存档系统是否自动剔除" | ⏳ 待 U9 查证 |
| **R-C1**（与 Diplomacy 等大 Mod 事件顺序） | "未知" | ⏳ Diplomacy 未在已装清单中 — 暂不考虑 |
| **R-C2**（与其他驻军 Mod 双重招募） | "未知" | ⚠️ **确证存在** — `ImprovedGarrisons` 和 `GarrisonDoSomething` 已在装。MOD_SURVEY 必须确认它们与本 Mod 的接管对象是否冲突；建议**与 ImprovedGarrisons 互斥**（用户二选一） |
| **R-C-RBM** (新) | — | RBM v4.2.23 已装。规则见 §3.3 |

---

## 7. 第二阶段（可行性报告）入口条件 — 待办

进入第二阶段前，仍需补完以下小调查（无需用户决策，本助手可自行完成）：

1. ✅ **CampaignSystem 全类型枚举** — 已完成（1931 classes + 87 interfaces 落盘）
2. 🔲 `MobilePartyAi` + `AiBehavior` 枚举 + `AiVisitSettlementBehavior` 反编译 — U6 / R-P2 必要
3. 🔲 `CharacterObject` 公开属性反编译 — U8 必要（兵种过滤）
4. 🔲 `MBObjectManager` 反编译 — TypeDefiner 注册路径
5. 🔲 `TransferTroopsAction` / `AddTroopsAction` 等 Action 类反编译 — 真实招募闭环
6. 🔲 `IGarrisonRecruitmentBehavior` / `IPatrolPartiesCampaignBehavior` 接口反编译 — 接管点形态
7. 🔲 RBM `RBM.dll` 主程序集反编译（仅 CampaignBehavior 部分）— 兼容矩阵最终化

需要用户决策的：
1. **MCM 依赖补全 vs XML/JSON 兜底**（§1） — 建议 MVP 1–4 走 XML/JSON，MVP 5 再接 MCM 并提示用户装 ButterLib + UIExtenderEx
2. **是否同意建立空 Mod 骨架**（PHASE1 中的 Q7） — 仍待回复

---

## 附录：反编译产物索引

| 文件 | 大小 | 用途 |
|---|---|---|
| `_research/decompiled/CampaignSystem_classes.txt` | 300 KB | 1931 个 class 完全限定名 |
| `_research/decompiled/CampaignSystem_interfaces.txt` | 12 KB | 87 个接口 |
| `_research/decompiled/SaveSystem_classes.txt` | 13 KB | 111 个 class |
| `_research/decompiled/CampaignBehaviorBase.cs` | 0.5 KB | 入口抽象类 |
| `_research/decompiled/CampaignEvents.cs` | 120 KB | 274 个公共事件 getter |
| `_research/decompiled/CampaignGameStarter.cs` | 7 KB | 注册接口 |
| `_research/decompiled/MobileParty.cs` | 120 KB | 主队伍类 |
| `_research/decompiled/MobilePartyHelper.cs` | 13 KB | 队伍工厂工具 |
| `_research/decompiled/PartyComponent.cs` | 3 KB | 抽象基类 |
| `_research/decompiled/CustomPartyComponent.cs` | 9 KB | Mod 自定义队基类 |
| `_research/decompiled/PatrolPartyComponent.cs` | 3 KB | 官方巡逻队 |
| `_research/decompiled/GarrisonPartyComponent.cs` | 3 KB | 官方驻军队 |
| `_research/decompiled/PatrolPartiesCampaignBehavior.cs` | 32 KB | 巡逻管理 |
| `_research/decompiled/RecruitmentCampaignBehavior.cs` | 36 KB | 招募管理 |
| `_research/decompiled/GarrisonRecruitmentCampaignBehavior.cs` | 9 KB | 驻军招募 |
| `_research/decompiled/Town.cs` | 20 KB | 城镇 |
| `_research/decompiled/Settlement.cs` | 39 KB | 定居点 |
| `_research/decompiled/TroopRoster.cs` | 19 KB | 兵员名册 |
| `_research/decompiled/SaveableTypeDefiner.cs` | 4 KB | 存档基类 |
| `_research/decompiled/SaveableFieldAttribute.cs` | 0.3 KB | 字段标记 |
| `_research/decompiled/SaveablePropertyAttribute.cs` | 0.3 KB | 属性标记 |
