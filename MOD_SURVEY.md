# MOD_SURVEY.md — 现有驻军 Mod 调研 + 功能基线对照

> 本 Mod 的目标是**完整替代** `ImprovedGarrisons` 与 `GarrisonDoSomething`（用户 2026-05-12 决策）。
> 本文件负责提取这两个 Mod 的全部已实现功能，作为本 Mod **必须覆盖的"功能基线"**。

---

## 1. ImprovedGarrisons v4.2.0.5 — 全功能拆解

📂 证据：
- `Modules/ImprovedGarrisons/SubModule.xml`
- `Modules/ImprovedGarrisons/bin/Win64_Shipping_Client/ImprovedGarrisons.dll`（569 KB，175 个 class，8 个 enum）
- 反编译类型清单：`_research/decompiled/replaced/IG_classes.txt`、`IG_enums.txt`

### 1.1 ImprovedGarrisons 模块结构（按命名空间）

| 命名空间 | 类数量 | 职责 |
|---|---|---|
| `ImprovedGarrisons.Main` | 1 | `MBSubModuleBase` 入口 |
| `ImprovedGarrisons.Behaviours` | 2 | `GarrisonPartyBehavior` — 自定义 PartyComponent 关联 Behavior |
| `ImprovedGarrisons.SaveSystem` | 41 | `GarrisonBehavior`、`GarrisonDailyBehavior`、`UiBehavior`、`SaveBehavior` + `IGSaveData` + 配置 + 配置文件管理 |
| `ImprovedGarrisons.Recruitment` | 2 | `GarrisonRecruitmentLogic` — 招募计算与执行 |
| `ImprovedGarrisons.Upgrade` | 2 | `GarrisonUpgradeLogic` + `TroopTypes`（兵种类型枚举 — 兵种自动升级） |
| `ImprovedGarrisons.Models` | 4 | 4 个 GameModel 替换：`GarrisonCostModel`、`GarrisonFoodModel`、`GarrisonpartySizeLimitModel`、`GarrisonSpeedModel` |
| `ImprovedGarrisons.AI.PartyComponent` | 1 | **`ImprovedGarrisonPartyComponent`** — 自建 PartyComponent（验证 §2.4 PartyComponent 模式真的被社区用着） |
| `ImprovedGarrisons.AI.Orders` | 多个 | `ImprovedPartyOrder` + 5 种具体 Order：**`OrderDefense` / `OrderEscort` / `OrderMergeGarrison` / `OrderPatrol` / `OrderStopIfPlayerTarget`**  |
| `ImprovedGarrisons.AI.AITypes` | 6 | `BoundedParty`、`GarrisonRecruiter`、`ImprovedAi`、`ImprovedPartyAi`、`ImprovedSettlement`、`MobileGarrison` |
| `ImprovedGarrisons.AI.AIManagers` | 5 | **`GarrisonRecruiterPartyManager` / `MobileGarrisonManager` / `PartyManager` / `TransferPartyManager` / `VillageRecruitPartyManager`** |
| `ImprovedGarrisons.ImprovedGarrisonsUI` | 46 | Gauntlet UI：主面板 + 6 个子菜单（`GarrisonUIVM` / `GuardsUIVM` / `ManagementUIVM` / `OverviewUIVM` / `RecruitmentUIVM` / `TrainingUIVM`）+ CascadeMenu（嵌入式弹出菜单） |
| `ImprovedGarrisons.ConfigOptionsMenu` | 22 | 自建配置 UI（Gauntlet）— 不依赖 MCM |
| `ImprovedGarrisons.Tutorial` | 5 | 内置教程 |
| `ImprovedGarrisons.Ribbons` | 4 | UI Ribbons / 通知条 |
| `ImprovedGarrisons.HintManager` | 2 | 提示工具 |
| `ImprovedGarrisons.Menu` | 2 | 城镇菜单 `ImprovedGarrisonMenu`、主菜单按钮 |
| `ImprovedGarrisons.Debugging` | 2 | 日志文件管理 `LogFileManager` / `LogFileWriter` |
| `ImprovedGarrisons.ActivityLogging` | 8 | `ActivityLogManager` + 6 种 Activity：`GarrisonActivity` / `PartyCreationActivity` / `PartyDestructionActivity` / `PartyMergeWithGarrisonActivity` / `RecruitmentActivity` / `UpgradeActivity` |
| `ImprovedGarrisons.Utils` | 2 | `ModuleColors` / `ModuleStrings` |

### 1.2 ImprovedGarrisons 配置数据模型（必须复刻或覆盖）

| 配置类 | 用途 |
|---|---|
| `IGSaveData` | 总入口 |
| `GarrisonSettings` | 单城镇驻军设置（**对应本 Mod 的 TownGarrisonRule**） |
| `GlobalSettings` | 全局默认 |
| `NPCGarrisonSettings` | NPC 城镇也能设置（友方/敌方） |
| `TrainingTemplate` | 训练模板（兵种升级路径） |
| `ImprovedGarrisonSettings` | 总配置子集 |
| `ManagementSettings` | 管理设置 |
| `MobileGarrisonSettings` | 移动驻军设置 |
| `RecruitmentSettings` | 招募设置 |
| `TemplateManager` + `ManagementType` 枚举 | 多模板 |
| `TrainingSettings` | 训练设置 |
| `ConfigFilePath` / `GlobalSettingsFilePath` / `IGSaveFilePath` / `SettlementSaveFilePath` | 文件路径管理 |
| `Config` / `ConfigManager` / `FileWriter` | 配置读写 |

### 1.3 ImprovedGarrisons 提供的"驻军 Order"行为（5 种）

| Order | 含义 |
|---|---|
| **`OrderDefense`** | 防御模式：留城内，敌军接近时出战 |
| **`OrderEscort`** | 护送（保护贸易/补给路线） |
| **`OrderMergeGarrison`** | 把外出队伍并回驻军 |
| **`OrderPatrol`** + `Mode` 枚举 | 巡逻（多模式） |
| **`OrderStopIfPlayerTarget`** | 当玩家成为目标时停止 |

### 1.4 ImprovedGarrisons 提供的"队伍 Manager"（5 个）

| Manager | 职责 |
|---|---|
| **`GarrisonRecruiterPartyManager`** | 创建/调度 **征兵队** — 对应本 Mod 核心功能二 |
| **`MobileGarrisonManager`** | 创建/调度 **移动驻军**（可外派的驻军单位） |
| **`PartyManager`** | 通用队伍管理 |
| **`TransferPartyManager`** | **跨城镇 / 跨城堡调拨队** — 对应本 Mod 核心功能四（城镇 ↔ 城堡调拨） |
| **`VillageRecruitPartyManager`** | **村庄招募队** — 专门去村庄招募 |

### 1.5 ImprovedGarrisons 替换的 4 个 Game Model

| Model | 替换的 vanilla 模型 |
|---|---|
| `GarrisonCostModel` | 驻军工资成本 |
| `GarrisonFoodModel` | 驻军食物消耗 |
| `GarrisonpartySizeLimitModel` | 驻军 / 移动驻军队伍人数上限 |
| `GarrisonSpeedModel` | 驻军（移动单位）速度 |

⚠️ 注意：这些都是 `CampaignGameStarter.AddModel(GameModel)` 注册的 — 这是 **vanilla 公开扩展点**，**不依赖 Harmony**。

### 1.6 ImprovedGarrisons 的活动日志（6 种 Activity）

为何重要：用户的核心功能六（LLM 推理）要求"透明日志 — 调用原因 / 输入摘要 / 输出结果 / 是否采纳 / 拒绝原因"。ImprovedGarrisons 已经有相同的范式：

| Activity | 触发 |
|---|---|
| `GarrisonActivity` | 一般驻军事件 |
| `PartyCreationActivity` | 队伍创建 |
| `PartyDestructionActivity` | 队伍销毁 |
| `PartyMergeWithGarrisonActivity` | 队伍并入驻军 |
| `RecruitmentActivity` | 招募 |
| `UpgradeActivity` | 兵种升级 |

### 1.7 ImprovedGarrisons 没有的功能（我们要新增 / 升级）

1. ❌ 没有 **LLM 推理辅助**（功能六）
2. ❌ 没有 **明确的城镇 ↔ 城堡互调拨规则**（TransferPartyManager 是通用调拨，没有针对城堡的语义）
3. ❌ 没有 **存档/卸载安全策略明示**（自建文件系统，但 Mod 卸载后行为未知）
4. ❌ 没有 **MCM 集成**（自建 ConfigOptionsMenu）— 我们用 MCM 后会更"标准"
5. ⚠️ 自建 PartyComponent 但**不知 v1.3.15 兼容性**（v4.2.0.5 是不是为 v1.3.15 编译的需要确认 — 它的 SubModule.xml 没声明 DependedModule，可能就是版本无关靠反射 / 早期 API；第二阶段反编译方法体时确认）
6. ⚠️ **零声明依赖**：不依赖 Harmony / MCM / ButterLib — 这意味着它走自建路径，对版本变化非常敏感

### 1.8 已知风险（针对替代决策）

- **存档迁移**：用户若从 ImprovedGarrisons 切换到本 Mod，旧存档中的 `IGSaveData` 与自建 `MobileParty` 怎么办？
  - 选项 A：完全忽略（用户接受旧驻军变成 vanilla 驻军，由本 Mod 接管）— **建议**
  - 选项 B：写一个一次性的迁移器（在 OnGameLoaded 时读取 IGSaveData，转换为我方 GarrisonRule）— 复杂且依赖 ImprovedGarrisons 类型在内存中存在，**不建议**
- **UI 冲突**：ImprovedGarrisons 在城镇菜单里加了"Garrison Menu"按钮（`ImprovedGarrisons.Menu.ImprovedGarrisonMenu`）。我们的 Mod 同样会加按钮 — 启动检测确认 ImprovedGarrisons 未启用即可。

---

## 2. GarrisonDoSomething v1.6.6 — 全功能拆解

📂 证据：
- `Modules/GarrisonDoSomething/SubModule.xml`
- `Modules/GarrisonDoSomething/bin/Win64_Shipping_Client/GarrisonDoSomething.dll`（172 KB，27 个类型）
- 反编译类型清单：`_research/decompiled/replaced/GDS_classes.txt`

### 2.1 重要前置

⚠️ **GarrisonDoSomething 使用 zero-width / RTL 字符做代码混淆**（多个类名是不可见的 Unicode 字符串）。这意味着：
- 反编译方法体几乎不可读
- 不能依赖于"读懂它的实现细节"
- 但**类名暴露了功能意图**

🚨 **GarrisonDoSomething 在用户当前环境下根本跑不起来** — 它的 SubModule.xml 声明硬依赖：
```xml
<DependedModule Id="Bannerlord.Harmony" />
<DependedModule Id="Bannerlord.ButterLib" />      <!-- 缺失 -->
<DependedModule Id="Bannerlord.UIExtenderEx" />   <!-- 缺失 -->
<DependedModule Id="Bannerlord.MBOptionScreen" /> <!-- 装了但无法启动 -->
```

### 2.2 GarrisonDoSomething 真实的"做了什么"

去除混淆后能看到的关键类（全部都是 **Harmony Patch**）：

| Patch 类 | 修改的 vanilla 类 |
|---|---|
| `DefaultClanTierModelPatch` | `DefaultClanTierModel`（氏族等级模型） |
| `LordPartyComponent_InitializationArgsPatch` | `LordPartyComponent.InitializationArgs` |
| `PatrolPartiesCampaignBehaviorPatch` | `PatrolPartiesCampaignBehavior`（**官方巡逻队行为**） |
| `DisbandPartyCampaignBehaviorPatch` | `DisbandPartyCampaignBehavior`（解散队伍行为） |
| `DefaultPartySizeLimitModelPatch` | `DefaultPartySizeLimitModel`（队伍人数上限模型） |
| `WarPartyComponentPatch` | `WarPartyComponent`（战时领主队 component） |
| `MobilePartyPatch` | `MobileParty`（核心队伍类！） |

辅助类：
- `CheyronSubModule : MBSubModuleBase` — 入口
- `MySettings` — 配置类（MCM 关联）
- `MySaveDefiner : SaveableTypeDefiner` — 自定义存档
- `GrifterAISlopException` — 异常

### 2.3 GarrisonDoSomething 的设计哲学（推断）

从 Patch 名看，**GarrisonDoSomething 不是"管理驻军"，而是让"游戏自带的巡逻队 / 领主队 / 战争队"做更多事**（do something）。它是一个"vanilla AI 行为增强器"。

而 ImprovedGarrisons 才是真正的"驻军管理"系统。两个 Mod **职责不同**，但都在玩家的"驻军体验"链路上。

### 2.4 与本 Mod 的关系

| 维度 | GarrisonDoSomething | 本 Mod |
|---|---|---|
| 修改方式 | 7 个 Harmony Patch | 官方接口 (`IGarrisonRecruitmentBehavior` / `IPatrolPartiesCampaignBehavior`) + 自建 Behavior 替换 |
| 巡逻队 | Patch `PatrolPartiesCampaignBehavior` 让 vanilla 巡逻队更积极 | 完全替换或绕过 vanilla 巡逻队，自己创建 `PatrolPartyComponent` |
| 队伍人数上限 | Patch `DefaultPartySizeLimitModel` | **不 Patch** —— 写自己的 `GameModel` 通过 `AddModel` 注册（参考 ImprovedGarrisons 同款 `GarrisonpartySizeLimitModel`） |
| 解散行为 | Patch `DisbandPartyCampaignBehavior` | 我们的队伍生命周期自管（PartyLifecycleManager） |
| 在 v1.3.15 可用性 | ❌ 依赖缺失 | ✅ |

**结论**：本 Mod 的"巡逻队 / 防御反应"功能（核心功能三）**完全覆盖且更稳健**地实现了 GarrisonDoSomething 的所有 Patch 意图，但走的是"官方扩展点"路线，不需要 Harmony。

---

## 3. 功能基线对照矩阵（**本 Mod 必须覆盖**）

| # | 基线功能 | 来源 | 本 Mod 对应模块 | MVP |
|---|---|---|---|---|
| F-01 | 自动管理城镇驻军：目标人数 / 兵种比例 / 兵种质量 / 文化 / 阵营过滤 | IG `GarrisonSettings` + `RecruitmentSettings` | `TownGarrisonManager` + `TroopCompositionEvaluator` | MVP 1, 3 |
| F-02 | 自动征兵队（真实 MobileParty 创建并走村庄招募） | IG `GarrisonRecruiterPartyManager` + `VillageRecruitPartyManager` | `RecruitmentManager` + `RecruitingPartyComponent`（继承 `CustomPartyComponent`） | MVP 2 |
| F-03 | 驻军巡逻队（5 种 Order：Defense / Escort / MergeGarrison / Patrol / StopIfPlayerTarget） | IG `OrderXxx` 5 类 | `PatrolManager` + 用 `PatrolPartyComponent.CreatePatrolParty` | MVP 4 |
| F-04 | 跨城镇/城堡兵员调拨 | IG `TransferPartyManager` | `GarrisonTransferManager` + `CastleSupportManager` | MVP 3.5 |
| F-05 | 移动驻军（外派但仍归属城镇） | IG `MobileGarrisonManager` + `MobileGarrison` | `GarrisonTransferManager`（含 OutboundDetachment） | MVP 3.5, 4 |
| F-06 | 防御反应（敌军接近时派兵出击） | IG `OrderDefense` | `SettlementDefenseDemandEvaluator` + `PatrolManager` 防御出击分支 | MVP 4 |
| F-07 | 兵种自动升级（驻军内升级） | IG `GarrisonUpgradeLogic` + `TroopTypes` | `TroopCompositionEvaluator` 的升级建议分支 | MVP 3 |
| F-08 | 训练模板（多个兵种升级路径） | IG `TrainingTemplate` + `TrainingSettings` | 同上 | MVP 3 |
| F-09 | 4 个 GameModel 替换：成本/食物/队伍上限/速度 | IG `GarrisonCostModel` / `FoodModel` / `partySizeLimitModel` / `SpeedModel` | 同名 4 个 Model（按需，**默认不替换 vanilla 模型避免大改经济**） | MVP 5（可选） |
| F-10 | 单城镇配置 + 全局默认配置 + 多模板套用 | IG `GarrisonSettings` + `GlobalSettings` + `TemplateManager` | `ConfigurationManager` + 三层模板（全局/模板/单城） | MVP 1（配置读）→ MVP 5（编辑 UI） |
| F-11 | 配置导入/导出（文件路径） | IG 4 个 `FilePath` + `FileWriter` | `ConfigurationManager` 的 I/O 子模块 | MVP 5 |
| F-12 | 城镇内菜单按钮入口 | IG `ImprovedGarrisonMenu` + `MainMenu` | `CampaignGameStarter.AddGameMenu(...)` | MVP 5 |
| F-13 | 主面板 UI（Gauntlet） | IG 46 个 UI 类 | MVP 5（先用 vanilla MenuBased UI，正式版做 Gauntlet） | MVP 5 |
| F-14 | 6 个子菜单：Garrison / Guards / Management / Overview / Recruitment / Training | IG `*UIVM` | MVP 5 | MVP 5 |
| F-15 | 嵌入式 Cascade Menu（弹出菜单） | IG `CascadeMenu*` | MVP 5（可选） | MVP 5 |
| F-16 | Tutorial / 教程 | IG `Tutorial*` | MVP 5+ | 可选 |
| F-17 | Ribbons / Hint 通知 | IG `Ribbons*` / `HintManager*` | 用 vanilla `InformationManager` | MVP 1 |
| F-18 | 活动日志（6 种 Activity） | IG `ActivityLog*` | `DecisionAuditLogger` | MVP 1 |
| F-19 | 调试日志文件 | IG `LogFileManager` / `LogFileWriter` | `LoggingSystem` | MVP 1 |
| F-20 | NPC 城镇也可设置 | IG `NPCGarrisonSettings` | **本 Mod 限定玩家自有城镇，明确不支持 NPC 城镇** — 见用户原则 | — |
| F-21 | 配置版本迁移 | IG（隐式） | `ConfigurationManager.MigrateConfig(version)` | MVP 5 |
| F-22 | 移动驻军队（可外派且能随时回城） | IG `MobileGarrison` | `PartyLifecycleManager` 的"召回"接口 | MVP 4 |
| F-23 | 巡逻队"遇玩家停下"行为 | IG `OrderStopIfPlayerTarget` | `PatrolManager` 风险评估的玩家友军特例 | MVP 4 |
| F-24 | **GarrisonDoSomething 的"vanilla 巡逻队增强"** | GDS `PatrolPartiesCampaignBehaviorPatch` | **完全替换 vanilla 巡逻队**（用我们的 `PatrolManager`），不再"增强 vanilla"，故此项**不需要再做** | — |
| F-25 | LordParty/WarParty 增强 | GDS `LordPartyComponent_InitializationArgsPatch` / `WarPartyComponentPatch` | **不在本 Mod 范围**（用户原则：直接管理对象仅 Town） | — |
| F-26 | ClanTier 模型调整 | GDS `DefaultClanTierModelPatch` | **不在本 Mod 范围** | — |
| F-27 | **LLM 推理辅助**（功能六）| **新增 — 两个 Mod 均无** | `LLMReasoningService` + `LLMProviderInterface` + `LLMDecisionValidator` + `RuleBasedFallbackDecisionMaker` | MVP 5.5 / MVP 6 |
| F-28 | **明确"城镇 ↔ 城堡"语义化调拨**（不是通用 transfer，是明确两端） | **新增 — 升级 IG 通用调拨** | `CastleSupportManager` + `GarrisonTransferManager` | MVP 3.5 |
| F-29 | **官方接口替换路径**（IGarrisonRecruitmentBehavior / IPatrolPartiesCampaignBehavior） | **设计升级** | 注册接管 Behavior，**移除 Harmony Patch 依赖** | MVP 1+ |
| F-30 | **RBM 完整兼容**（用户要求） | **新增约束** | 全部兵种判定走运行时属性，无硬编码 stringId | 全 MVP 贯穿 |

---

## 4. 替代关系：SubModule 互斥策略（关联任务 #11）

本 Mod 启动时需主动检测 `ImprovedGarrisons` 与 `GarrisonDoSomething` 是否已启用：

**实现思路（待第二阶段查证 `ModuleHelper` / `ModuleInfo` API）**：
1. 在 `MBSubModuleBase.OnSubModuleLoad()` 早期，枚举 `ModuleHelper.GetModules()` 或读 `Modules/<id>/SubModule.xml` 的 `<ModuleType>` 与是否在 launcher 启用列表
2. 若任一上述 Mod 处于启用列表：
   - **a 选项**：`InformationManager.DisplayMessage` 显示警告，但允许并行加载（让用户自行决定）
   - **b 选项**：在 `OnGameStart` 阶段拒绝注册本 Mod 的 CampaignBehavior，仅保留一个"提示用户去禁用对方"的菜单
3. **推荐 b 选项** — 因为两个 Mod 都直接操作 `MobileParty` / `TroopRoster`，并行运行会双重创建队伍 / 双重招募 / 双重调拨

`SubModule.xml` 层面 Bannerlord 没有"反依赖"标签（`DependedModuleMetadatas` 只是声明依赖，不能声明互斥）— 必须靠运行时检测。任务 #11 跟踪。

---

## 4.5. 用户已确认事实：IG ↔ GDS 之间存在冲突

📌 用户 2026-05-12 明确告知：`ImprovedGarrisons` 与 `GarrisonDoSomething` **两者之间本身就互相冲突**。这是用户希望用一个 Mod 替代两者的**根本动机之一**。

### 冲突点推断（基于反编译类型清单）

| # | 怀疑冲突点 | IG 行为 | GDS 行为 | 影响 |
|---|---|---|---|---|
| C-1 | 队伍人数上限计算链 | `AddModel(new GarrisonpartySizeLimitModel())` 替换 vanilla GameModel | `DefaultPartySizeLimitModelPatch` 通过 Harmony Patch 修改原 `DefaultPartySizeLimitModel` 方法 | **GDS Patch 的对象可能是 vanilla 模型，但 IG 已把 vanilla 模型从 `Campaign.Models` 链路换掉** —— Patch 失效或运行错误对象，导致数值不一致 |
| C-2 | MobileParty 核心方法 | IG 5 个 Manager 大量调用 `MobileParty.SetTargetSettlement` / `SetCustomHomeSettlement` / `MemberRoster.AddToCounts` | `MobilePartyPatch` Harmony 拦截 `MobileParty` 的某些方法 | Patch 改变方法语义，IG 期望的行为与实际不符 |
| C-3 | 巡逻队 | IG 自建 `OrderPatrol` 队伍走 `ImprovedGarrisonPartyComponent` | `PatrolPartiesCampaignBehaviorPatch` 增强 vanilla `PatrolPartiesCampaignBehavior` | GDS 的"vanilla 巡逻增强"可能误识别 IG 的自定义队伍并施加意料外行为 |
| C-4 | LordParty / WarParty 初始化 | IG 不直接修改这些，但创建依赖 `LordPartyComponent` 的派生类型 | `LordPartyComponent_InitializationArgsPatch` + `WarPartyComponentPatch` | Patch 的初始化逻辑可能干扰 IG 创建的队伍属性 |
| C-5 | 解散队伍 | IG 通过 `PartyLifecycleManager`（推断）解散自有队伍 | `DisbandPartyCampaignBehaviorPatch` Patch vanilla 解散行为 | IG 自有队伍若意外走 vanilla 解散流程会触发被 Patch 的代码路径，行为不可预期 |
| C-6 | 自定义存档 ID 空间 | IG `TroopTypesSaveableTypeDefiner` | GDS `MySaveDefiner` | 两个 SaveableTypeDefiner 的 `saveBaseId` 若碰撞，存档加载会出错 |
| C-7 | UI 入口 | IG 加 `ImprovedGarrisonMenu` + `MainMenu` | GDS 走 MCM 配置 | 一般不冲突，但 UI 层若都试图 hook 同一 vanilla menu 会有顺序问题 |

### 对本 Mod 的设计启示

1. **不要走 GDS 路线（Harmony Patch 改 vanilla 模型）**：因为 Patch 的"被 Patch 对象"可能被任何其他 Mod 通过 `AddModel` 替换掉，Patch 就静默失效。改用 IG 路线（`AddModel` 替换）但**只在用户启用相关功能时才注册自定义 Model**，否则保留 vanilla — 不强加于经济。
2. **MobileParty 行为不 Patch**：依赖事件总线（CampaignEvents）+ 自有 PartyComponent，所有调度走公开 API。
3. **存档 saveBaseId 文档化**：在 README 与代码注释中明确我们使用的 saveBaseId 区间，方便未来排查与其它 Mod 的冲突。
4. **明确单 Mod 替代承诺**：用户启用本 Mod 后无需保留 IG / GDS — 这才是用户期望的"一个 Mod 解决全部问题"。

---

## 5. 调研结论

| 维度 | 结论 |
|---|---|
| **能否完全替代 ImprovedGarrisons** | ✅ 能。其全部 175 个类的职责都对应到本 Mod 的 Manager / Behavior / Component / UI 模块（F-01 ~ F-23）。 |
| **能否完全替代 GarrisonDoSomething** | ✅ 能。其 7 个 Harmony Patch 的全部意图均能通过本 Mod 的"官方接口替换"路径覆盖（F-24 涵盖 patrol；F-25/26 用户原则明确不在范围）。 |
| **本 Mod 相对优势** | (1) F-27 LLM 推理 (2) F-28 语义化城镇 ↔ 城堡调拨 (3) F-29 不用 Harmony，对游戏更新更稳健 (4) F-30 完整兼容 RBM |
| **替代成本** | 用户需在 Bannerlord Launcher 取消勾选这两个 Mod。**存档迁移建议忽略旧数据**（让旧驻军变 vanilla 驻军后由本 Mod 接管，避免复杂的存档迁移） |
| **第二阶段必须补的反编译** | (1) ImprovedGarrisons 几个核心 Manager 方法体（仅取算法思路，不抄代码）— 反编译已就位 (2) `ModuleHelper` / `ModuleInfo` 互斥检测 API |
