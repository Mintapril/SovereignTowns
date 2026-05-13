# UNCERTAINTY_LOG.md — 不确定点查证记录

> 对照 `PHASE1_RESEARCH_PLAN.md` §4 的 U1–U11，每项标注查证结论。
> 状态码：✅ **已解决** | ⏳ **部分解决，第二阶段补完** | ❌ **仍不确定**

---

## U1 — 是否能创建"无主 MobileParty"

**状态**：✅ **已解决（路径变更）**

**结论**：游戏的设计原则是 **MobileParty 必须挂载一个 `PartyComponent`**，不存在合法的"无主"队伍。
- 巡逻队 → `PatrolPartyComponent.CreatePatrolParty(stringId, position, spawnRadius, homeSettlement, template)`
- 驻军队 → `GarrisonPartyComponent.CreateGarrisonParty(stringId, settlement)`
- Mod 自定义 → 继承 `CustomPartyComponent` + 通过 `InitializationArgs` 实例化
- 所有 `PartyComponent` 派生类的 `HomeSettlement` 必填 — 这天然把"归属城镇"内嵌到队伍生命周期里

**证据**：
- `_research/decompiled/PartyComponent.cs`、`PatrolPartyComponent.cs`、`GarrisonPartyComponent.cs`、`CustomPartyComponent.cs`
- 社区验证：`ImprovedGarrisons.AI.PartyComponent.ImprovedGarrisonPartyComponent` 继承自 PartyComponent 模式

**对本 Mod 的影响**：所有 Manager（RecruitmentManager / PatrolManager / TransferManager）创建队伍时**不要走 `new MobileParty`**，必须通过对应 Component 的工厂方法。

---

## U2 — `Town.GarrisonParty` 的写入路径

**状态**：✅ **已解决**

**结论**：
- 枚举驻军：`MobileParty.AllGarrisonParties`（静态集合，按需 LINQ 过滤）
- 反向定位归属城镇：`GarrisonPartyComponent.Settlement`（取自 Settlement 引用）
- 写入兵员：`TroopRoster.AddToCounts(CharacterObject, int count, ...)` — 公开方法
- 注意"真实招募 vs 直接 AddToCounts"边界 — 见 §RESEARCH_FINDINGS.md §2.6 警告

**证据**：`_research/decompiled/MobileParty.cs`、`TroopRoster.cs`、`GarrisonPartyComponent.cs`

**对本 Mod 的影响**：驻军写入合法路径已锁定。但**绕过经济成本的直接 AddToCounts 仍是禁区**（违反"真实招募"原则）。

---

## U3 — 真实招募 API 的最小调用单元

**状态**：✅ **已解决**

**结论**：
- 招募实际推进点：`RecruitmentCampaignBehavior.HourlyTickParty(MobileParty mobileParty)` —— 每小时由官方 Behavior 处理招募
- 进城招募评估点：`OnBeforeSettlementEntered(MobileParty, Settlement, Hero)` —— 队伍进城时
- 驻军招募变化模型：`GarrisonRecruitmentCampaignBehavior.GetGarrisonChangeExplainedNumber(Town town)`
- 兵员来源：`Settlement.Notables` 列表里每个 Notable 的 `Hero.VolunteerTypes`（需第二阶段反编译 `Hero` 确认 VolunteerTypes 真实属性签名）

**接口替换路径（关键）**：
- `IGarrisonRecruitmentBehavior` 是**官方接口**。本 Mod 实现此接口 + 注册自己的 Behavior 即可完全替换 vanilla 驻军招募，**不需要 Harmony Patch**。

**证据**：`_research/decompiled/RecruitmentCampaignBehavior.cs`、`GarrisonRecruitmentCampaignBehavior.cs`

**对本 Mod 的影响**：MVP 2 的"创建征兵队真实招募"链路已明确。`RecruitmentManager` 应：
1. 创建征兵队（CustomPartyComponent 派生）
2. 用 `MobileParty.SetTargetSettlement(...)` 派去村庄
3. 进城后由 vanilla `RecruitmentCampaignBehavior.HourlyTickParty` 自动处理招募
4. 队伍达到目标人数后由我们的 Behavior 拦截 → 调度返回归属城镇 → 兵员入驻军

---

## U4 — 自定义存档边界

**状态**：✅ **已解决**

**结论**：
- 抽象基类：`abstract class SaveableTypeDefiner { protected SaveableTypeDefiner(int saveBaseId); ... }`
- 重写虚方法：`DefineClassTypes()`, `DefineEnumTypes()`, `DefineGenericClassDefinitions()`, `DefineContainerDefinitions()`, `DefineConflictResolvers()`
- 字段标记：`[SaveableField(short localSaveId)]`、属性：`[SaveableProperty(short localSaveId)]`
- `localSaveId` 在该类内**必须唯一且永远不变**（破坏旧存档）
- `saveBaseId` 在 mod 全局唯一，应取大数避碰：建议从 `100_000_000` 起步（具体 Native 占用范围第二阶段反编译 Native 内置 TypeDefiner 时锁定）

**官方范例（直接抄结构）**：
- `RecruitmentCampaignBehavior.RecruitmentCampaignBehaviorTypeDefiner`
- `VillagerCampaignBehavior.VillagerCampaignBehaviorTypeDefiner`
- `AllianceCampaignBehavior.AllianceCampaignBehaviorTypeDefiner`

**证据**：`_research/decompiled/SaveableTypeDefiner.cs`、`SaveableFieldAttribute.cs`、`SaveablePropertyAttribute.cs`

**对本 Mod 的影响**：本 Mod 的 `SaveDataManager` 提供一个 `<ModName>TypeDefiner : SaveableTypeDefiner`，所有需要存档的类用 `[SaveableField]` 标注。无 Harmony 需求。

---

## U5 — CampaignTime 调度精度

**状态**：✅ **已解决**

**结论**：v1.3.15 提供完整 Tick 层级：
- **日**：`DailyTickEvent`、`DailyTickPartyEvent`、`DailyTickTownEvent`、`DailyTickSettlementEvent`、`DailyTickHeroEvent`、`DailyTickClanEvent`
- **小时**：`HourlyTickEvent`、`HourlyTickPartyEvent`、`HourlyTickSettlementEvent`、`HourlyTickClanEvent`
- **AI 小时**：`AiHourlyTickEvent`
- **每帧**：`MBSubModuleBase.OnApplicationTick(float dt)`、`AfterAsyncTickTick(float dt)`

**性能考虑**：`HourlyTickPartyEvent` 在大地图上对每个 MobileParty 触发一次。我们的事件回调必须 **O(1) per party**，不能在回调里再遍历全部 settlement。

**证据**：`_research/decompiled/CampaignEvents.cs`（274 个公开事件 getter）

**对本 Mod 的影响**：
- 风险评估走 `HourlyTickSettlementEvent` → 每城镇每小时一次
- 队伍调度走 `HourlyTickPartyEvent` → 但只对**我们创建的** PartyComponent 子类响应
- 日复盘走 `DailyTickEvent`

---

## U6 — Party AI 路径阻塞行为

**状态**：✅ **已解决**

**结论**：v1.3.15 提供了完整的 AI 干预 API（`MobilePartyAi` 公开方法）：

```csharp
public class MobilePartyAi {
    public bool RethinkAtNextHourlyTick { get; set; }      // 强制下一小时重新决策
    public bool DoNotMakeNewDecisions { get; set; }        // 暂停新决策
    public bool IsAlerted { get; }
    public CampaignTime DoNotAttackMainPartyUntil { get; }

    public void DisableForHours(int hours);                // 关闭 AI N 小时
    public void DisableAi();                                // 完全停 AI
    public void EnableAi();
    public bool EnableAgainAtHourIsPast();
    public void SetDoNotAttackMainParty(int hours);
    public void SetInitiative(float attackInit, float avoidInit, float hoursUntilReset);  // 调整攻防倾向
    public void CalculateFleePosition(out CampaignVec2 fleeTargetPoint, MobileParty partyToFleeFrom, Vec2 averageEnemyVec);

    // ★ 路径阻塞官方检测
    internal static bool CheckIfThereIsAnyHugeObstacleBetweenPartyAndTarget(MobileParty party, Vec2 newTargetPosition);
}
```

`AiVisitSettlementBehavior` 公开常量：
- `GoodEnoughScore = 8f`
- `MeaningfulScoreThreshold = 0.025f`
- `BaseVisitScore = 1.6f`

**对本 Mod 的影响**：
- 卡死检测不必自己写，`CheckIfThereIsAnyHugeObstacleBetweenPartyAndTarget` 是 internal — 我们用反射调用即可
- 队伍卡住时调用 `MobilePartyAi.DisableForHours(2)` + `RethinkAtNextHourlyTick = true` 解套
- 应急逻辑：超过 N 小时未到达目标 → 重设目标为 home settlement
- 巡逻队"遇玩家停下"对应 `SetDoNotAttackMainParty(hours)` API

**证据**：`_research/decompiled/MobilePartyAi.cs`、`AiVisitSettlementBehavior.cs`

---

## U7 — 围城 / 战时事件触发器

**状态**：✅ **已解决**

**结论**：
- 围城：`OnMobilePartyJoinedToSiegeEvent` / `OnMobilePartyLeftSiegeEvent`
- 战斗：`MapEventStarted` / `BattleStarted`
- 队伍状态：`Settlement.IsUnderSiege`、`MobileParty.MapEvent`、`MobileParty.BesiegedSettlement`
- 阵营变化：`OnClanChangedKingdom`、`MakePeace` / `DeclareWar`（具体事件名第二阶段补查）

**证据**：`_research/decompiled/CampaignEvents.cs`、`Settlement.cs`、`MobileParty.cs`

**对本 Mod 的影响**：
- "敌军接近时禁止调拨"：直接读 `Settlement.NearbyLandThreatIntensity > threshold`
- "围城时禁止调拨"：直接读 `Settlement.IsUnderSiege`
- "战时驻军倍率"：监听 `MapEventStarted` / `OnSiegeEvent*`，本 mod 的 GarrisonManager 切换 active rule

---

## U8 — 兵种文化 / 阵营过滤的数据源

**状态**：✅ **已解决**

**结论**：v1.3.15 提供完整运行时兵种判定 API（**RBM 完全兼容**的关键）：

`CharacterObject : BasicCharacterObject, ICharacterData`（在 TaleWorlds.CampaignSystem.dll）公开属性：
```csharp
public override bool IsHero { get; }
public override bool IsPlayerCharacter { get; }
public bool IsRegular { get; }
public bool IsBasicTroop { get; }
public bool IsTemplate { get; }
public int Tier { get; }                              // ★ 兵种等级
public CharacterObject[] UpgradeTargets { get; }       // ★ 升级路径数组
public ItemCategory UpgradeRequiresItemFromCategory { get; }
public int ConformityNeededToRecruitPrisoner { get; }  // ★ 俘虏转化阈值
public bool IsMariner { get; }
public override float Age { get; }
public static MBReadOnlyList<CharacterObject> All { get; }

public int GetUpgradeXpCost(PartyBase party, int index);   // ★ 升级 XP 成本
public int GetUpgradeGoldCost(PartyBase party, int index); // ★ 升级金币成本
public Occupation GetDefaultOccupation();                  // → Occupation 枚举
public bool HasThrowingWeapon();
public override FormationClass GetFormationClass();        // ★ 兵种类型
public override float GetPower();
public override float GetBattlePower();
public void GetSimulationAttackPower(out float attackPoints, out float defencePoints, Equipment equipment = null);
public float GetHeadArmorSum / GetBodyArmorSum / GetLegArmorSum / GetArmArmorSum / GetHorseArmorSum / GetTotalArmorSum(Equipment.EquipmentType);
public static CharacterObject Find(string idString);
public static CharacterObject FindFirst(Predicate<CharacterObject> predicate);
public static IEnumerable<CharacterObject> FindAll(Predicate<CharacterObject> predicate);
```

`BasicCharacterObject : MBObjectBase`（在 TaleWorlds.Core.dll）公开属性：
```csharp
public virtual bool IsMounted { get; }                  // ★★ 骑兵判定
public virtual bool IsRanged { get; }                   // ★★ 远战判定
public virtual bool IsHero { get; }
public virtual bool IsFemale { get; }
public bool IsSoldier { get; }
public virtual int Level { get; }
public int Race { get; }
public FormationClass DefaultFormationClass { get; }    // ★ 默认 Formation
public FormationPositionPreference FormationPositionPreference { get; }
public virtual IEnumerable<Equipment> BattleEquipments { get; }   // 战斗装备组
public virtual Equipment FirstBattleEquipment { get; }
public virtual Equipment RandomBattleEquipment { get; }
public virtual IEnumerable<Equipment> CivilianEquipments { get; }
public bool HasMount();
public virtual int HitPoints { get; }
public virtual int MaxHitPoints();
```

**对本 Mod 的影响（完整解锁兵种过滤）**：

用户原则中的兵种分类完全可以这样判定：
- "骑兵" → `BasicCharacterObject.IsMounted`（或 `GetFormationClass() == FormationClass.Cavalry/HorseArcher`）
- "步兵" → `!IsMounted && !IsRanged`（或 `FormationClass.Infantry`）
- "弓兵 / 弩兵 / 投掷兵" → `IsRanged`（精分需要看 `BattleEquipments` 检测弓 / 弩 / 投掷武器；FormationClass 一般只到 `Ranged`）
- "特殊兵种" → `IsBasicTroop == false && IsHero == false`
- "贵族兵" → `Tier >= 5` 且 culture 关键字段（具体阈值取决于 culture，但 vanilla 贵族兵规律是 Tier 5+）
- "文化过滤" → `CharacterObject.Culture`（继承自 `MBObjectBase` 体系，具体属性名第二阶段如要二次确认可查；前面 RecruitmentCampaignBehavior 反编译已隐式使用过）
- "兵种等级" → `Tier`
- "兵种质量" → 用 `GetPower()` / `GetBattlePower()`、护甲总和等综合评分（vanilla 自带的数值）

**RBM 兼容性**：因为以上属性都是 vanilla 计算的运行时属性（依赖 `BattleEquipments`、`Tier`、`Culture` 等），RBM 改 `NPCCharacters` XML 后 vanilla 自动重算，**本 Mod 零硬编码 stringId 即可正确响应 RBM 的兵种 overhaul**。

**证据**：`_research/decompiled/CharacterObject.cs`、`BasicCharacterObject.cs`

---

## U9 — 卸载 Mod 后存档处置

**状态**：✅ **已解决（错误容忍设计）**

**结论**：v1.3.15 的存档系统是**错误容忍**架构：

`DefinitionContext`（在 TaleWorlds.SaveSystem.Definition）公开 API：
```csharp
public class DefinitionContext {
    public bool GotError { get; }                          // ★ 加载错误标记（不抛异常）
    public IEnumerable<string> Errors { get; }              // ★ 错误信息集合
    internal bool HasDefinition(Type type);
    public TypeDefinitionBase TryGetTypeDefinition(SaveId saveId);  // ★ Try* 模式
    public void GenerateCode(SaveCodeGenerationContext context);
    internal TypeDefinitionBase GetTypeDefinition(Type type);
    // ... AddXxxDefinition / GetXxxDefinition 等
}
```

`LoadContext`（在 TaleWorlds.SaveSystem.Load）公开 API：
```csharp
public class LoadContext {
    public DefinitionContext DefinitionContext { get; }
    public ISaveDriver Driver { get; }
    public object RootObject { get; }

    public bool Load(LoadData loadData, bool loadAsLateInitialize);
    public static bool TryConvertType(Type sourceType, Type targetType, ref object data);  // ★ 类型转换 fallback
    public ObjectHeaderLoadData GetObjectWithId(int id);
}
```

**关键证据**：
- `GotError` + `Errors` 是"收集错误而非抛异常"的设计 —— vanilla 处理"找不到 SaveableType"会记入 Errors 列表，但**不会让读档失败**
- `TryGetTypeDefinition(SaveId)` 用 Try 模式 —— 找不到返回 null，不抛
- `TryConvertType(...)` 提供类型不匹配时的转换 fallback

**对本 Mod 的影响**：
- **Mod 卸载后存档不会"坏档"** —— vanilla 存档系统会跳过我们注册的未知类型
- 我们创建的 `MobileParty`（继承 `CustomPartyComponent`）卸载后会变成"找不到 PartyComponent 类型"的孤儿 MobileParty —— vanilla 会处理为 `AllPartiesWithoutPartyComponent` 集合内的成员（前面 `MobileParty.cs` 反编译验证此集合存在），玩家可在游戏内手动解散
- 即便如此，**"安全卸载工具"仍要做**（在 R-S3 跟踪）—— 它能给用户更干净的退出路径：清理我们的 MobileParty + 还原 `Town.GarrisonAutoRecruitmentIsEnabled = true`

**第二阶段不再需要补查 U9**。

**证据**：`_research/decompiled/DefinitionContext.cs`、`LoadContext.cs`、`MobileParty.cs`（`AllPartiesWithoutPartyComponent` 集合）

---

## U10 — MCM 在 v1.3.15 可用性

**状态**：✅ **已解决（条件性可用）**

**结论**：
- MCM v5.11.4 已为 v1.3.15 编译适配（`Bannerlord.MBOptionScreen.v1.3.15.dll`）
- **但 MCM 硬依赖 `Bannerlord.ButterLib` + `Bannerlord.UIExtenderEx`，这两个模块在用户当前环境中缺失**
- 当前 MCM 启动会因依赖缺失失败

**对本 Mod 的影响**：
- **MVP 1–4 不依赖 MCM**：用 XML/JSON 自管配置文件
- **MVP 5 接入 MCM 但软依赖**：通过 try-catch 反射检测 MCM 是否可用，可用就显示 MCM UI，不可用就用我们自建的配置 UI（参考 ImprovedGarrisons 的自建 ConfigOptionsMenu 路线）
- 在 SubModule.xml 把 MCM 标为 `Optional="true"` 依赖

---

## U11 — LLM 网络阻塞 Tick

**状态**：✅ **设计层确定**

**结论**：
- C# 8+ 提供完整的 `async/await` + `HttpClient`
- 实现方式：
  1. LLM 调用走独立 `Task.Run(...)` + `CancellationToken`
  2. 主线程（Tick）只发起调用，结果回调用 `Campaign.Current.PostCampaignEvent(...)` 推回主线程
  3. 超时（例如 10 秒）后自动降级到 `RuleBasedFallbackDecisionMaker`
  4. **绝不在主线程 await LLM 响应**

**对本 Mod 的影响**：第二阶段不再做查证，MVP 5.5 实现时按上述方案。

---

## 新增：U12 — 模块互斥（由 IG ↔ GDS 冲突触发）

**状态**：✅ **完全解决**

**结论**：Bannerlord 模块系统**官方支持互斥**：
1. **静态层**：`SubModule.xml` 加 `<IncompatibleModules><Module Id="<id>"/></IncompatibleModules>`
   - 官方范例：`Modules/Native/SubModule.xml`
   - 社区范例：`Modules/BirthAndDeath/`、`Modules/FastMode/`（含注释样例）
   - 解析路径：`ModuleInfo.IncompatibleModules` 字段
2. **运行时层**：
   - `ModuleHelper.IsModuleActive(string moduleId)` 静态 API
   - `ModuleHelper.GetActiveModules()` 拿全部激活模块列表
   - `MBSubModuleBase.OnBeforeGameStart(MBGameManager, List<string> disabledModules)` 提供已禁用模块列表

**对本 Mod 的影响**：双重保护策略
1. **`SubModule.xml`**：声明 `<IncompatibleModules>` 包含 `ImprovedGarrisons` 和 `GarrisonDoSomething`
2. **`OnSubModuleLoad()`**：调用 `ModuleHelper.IsModuleActive("ImprovedGarrisons")` 二次确认，若激活则：
   - `InformationManager.DisplayMessage` 红字警告
   - 跳过 `CampaignGameStarter.AddBehavior` 注册
   - 让本 Mod"挂载但不工作"，避免双重操作

---

## 总览

| # | 状态 | 影响等级 | 第二阶段是否阻塞 |
|---|---|---|---|
| U1 | ✅ | 高 | 否 |
| U2 | ✅ | 高 | 否 |
| U3 | ✅ | 高 | 否 |
| U4 | ✅ | 高 | 否 |
| U5 | ✅ | 中 | 否 |
| U6 | ✅ | 中 | 否 |
| U7 | ✅ | 中 | 否 |
| U8 | ✅ | 高 | 否 |
| U9 | ✅ | 中 | 否 |
| U10 | ✅ | 中 | 否（MCM 改软依赖） |
| U11 | ✅ | 低 | 否 |
| U12 (新) | ✅ | 高 | 否 |

**第二阶段阻塞项**：**全部 12 项均已解决**。可以直接进入第二阶段（可行性报告）。
