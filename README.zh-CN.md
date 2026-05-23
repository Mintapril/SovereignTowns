# 主权城镇 / Sovereign Towns

[English](README.md) | [简体中文](README.zh-CN.md)

为 **骑马与砍杀 II：霸主 v1.3.15**（Mount & Blade II: Bannerlord v1.3.15）
设计的端到端 **氏族级城镇治理 mod**。玩家氏族选取一个定居点作为
**首府**（capital），mod 围绕首府全面接管氏族日常自动化：驻军构成、
志愿兵招募、俘虏转化、跨定居点兵力调拨、巡逻、出击迎敌、**氏族金币
经济管理**（队伍种子金 / 招募人头费 / 装备升级费直接从 `Clan.Gold` 扣，
另补一套 Hero↔Clan.Gold 转账 UI —— vanilla 没有这个）、以及按分支
配置的兵种构成模板。最小费用流（MCMF）求解器按可配置的 **后勤 tick**
规划氏族网络内的兵力流动（默认每 6 游戏小时一次，可调范围 1–24 小时，
由 `FiscalAutonomy.CapitalLogisticsTickHours` 控制）。

> **当前作用范围**：**仅玩家氏族**。AI 氏族管理在代码层已完整实装
> （`CapitalRegistry` / `VanillaSuppressionManager` 都有对称的 AI 路径），
> 但默认关闭 —— 由 `GlobalConfig.ApplyToAiSettlementsToo` 控制，默认 `false`，
> 且故意未在游戏内/网页控制面板暴露该开关，等待后续平衡测试完成。
> 开关一旦开启，会启用 AI 氏族完整接管（首府选择、驻军再平衡、招募、
> 巡逻、出击、俘虏转化），同时屏蔽其 vanilla 渠道的招志愿兵。

> **兼容性**：SovereignTowns 治理的 vanilla 范围（驻军构成、巡逻/出击队、
> AI 氏族招募）与 `ImprovedGarrisons` (IG) 和 `GarrisonDoSomething` (GDS)
> 修改的是同一组 vanilla 状态，共存会争抢。两者因此被列入
> `<IncompatibleModules>`，启动器在它们启用时拒绝启动。**与 RBM 兼容**
> （兵种判定不依赖 `stringId`）。

> **当前状态**：预发布快速迭代期（v0.0.1）。存档格式和 `global.json` 配置
> schema 可能在任意 commit 间不兼容 —— 暂不保证向后兼容性。

## 功能简介

受管理的氏族选取一个定居点作为 **首府**（capital）。每个后勤 tick
（默认 6 游戏小时，可调范围 1–24 小时）mod 自动执行：

- 在首府就地招募志愿兵；向远方村庄派出 **征兵队**（Recruiter parties）。
- 把俘虏转化进首府驻军。
- 派出 **调拨队**（Transfer parties）在首府与分支城镇/城堡间再平衡兵力，
  由最小费用流（min-cost-flow）求解器驱动。
- 围绕每个所属定居点派出 **巡逻队**（Patrol parties），驱赶盗匪和小股袭击者。
- 当敌对部队威胁定居点时派出 **出击队**（Sally parties）迎敌。
- 补充驻军时遵循每分支的 **兵种构成模板**（等级范围 / 文化筛选 / 兵种比例）。

### 氏族经济

mod 的经济决策也围绕首府运行。首府金库 **直接等于** vanilla `Clan.Gold`，
不再有独立账本：

- vanilla 把氏族所有城镇/城堡的每日收入（税收、商业、村庄）汇总到
  `Clan.Gold`（不论由哪位 hero 持有），通过原版 `DefaultClanFinanceModel`
  自动处理。mod 不拦截这部分。
- mod 自身的支出 —— 派出队伍种子金、招募人头费、装备升级费 —— 通过
  反射 setter 直接从 `Clan.Gold` 扣（vanilla 把 Clan.Gold 的 setter
  声明为 internal，外部代码不能直接赋值，所以 mod 加了一层
  `ClanGoldAccess` 反射 helper）。
- "金币不足时暂停支出"开关（默认开）会在 `Clan.Gold` 即将变负时阻止
  mod 主动扣款；关闭则允许 mod 把 Clan.Gold 扣到负数（与 vanilla 政策
  献金等机制行为一致）。
- 控制面板（游戏内 + 网页两端）都暴露存取页面：在 **Hero.MainHero.Gold**
  和 **Clan.Gold** 之间转账 —— vanilla 没提供这层 UI，mod 自补。作坊与
  商队收益按 vanilla 仍走主角个人 `Hero.Gold`，不进氏族金币。
- vanilla 的 Clan Finance 标签页和 mod 的金库页显示**同一个数字**
  （因为它们就是同一个数字）。

配置方式：游戏内 **控制面板**（大地图左侧贴边的常驻竖向按钮 +
每个所属城镇/城堡菜单的入口选项）+ 单独提供的
**网页控制面板**（`http://127.0.0.1:<端口>/`，默认端口 `41763`，
冲突时自动递增；编辑能力更强）。

UI 完整本地化：**英文** 和 **简体中文**。

## 仓库目录结构

```
.
├── src/                         # C# 源码 + csproj
│   └── SovereignTowns.csproj
├── Module/                      # Bannerlord 端的 module 资源
│   ├── SubModule.xml
│   ├── GUI/Prefabs/             # Gauntlet UI prefab（控制面板）
│   ├── ModuleData/Languages/    # EN + CNs 本地化文件
│   └── WebUI/                   # 网页控制面板（HTML/JS/CSS bundle）
├── Directory.Build.props
├── LICENSE
├── README.md
└── README.zh-CN.md
```

以下目录可能在本地存在，但不入 git（第三方版权 / 仓库瘦身 / 内部文档）：

- `_research/` —— 反编译的 vanilla + 参考 mod 源码（查 TaleWorlds API 签名时
  作为权威参考，仅本地保留）。
- `audits/` —— 设计规格、重构 handoff、backlog 笔记。
- `docs/` —— 行为指南、计划归档。
- `.claude/` —— AI 工具的本地配置/缓存。

## 构建

需要 .NET Framework 4.7.2 dev pack + 本地 Bannerlord 安装。

```powershell
dotnet build src\SovereignTowns.csproj -c Debug
# 或 -c Release
```

`DeployToGame` MSBuild target（`AfterTargets="Build"`）会在编译后自动把
DLL/PDB + `SubModule.xml` + GUI prefab + WebUI 资源 + 语言 XML 拷贝到
`$(BannerlordPath)\Modules\SovereignTowns`。

`BannerlordPath` 默认值是 Steam 标准安装位置：

```
C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord
```

如果你的 Bannerlord 装在别处（自定义 Steam Library、Epic、GOG、其他盘等），
用以下任一方式覆盖，优先级从上到下：

1. **每次 build 用 CLI 参数**（一次性）：
   ```powershell
   dotnet build src\SovereignTowns.csproj -c Debug `
     -p:BannerlordPath="D:\Games\Mount & Blade II Bannerlord"
   ```
2. **`Directory.Build.props.user`** —— 本地持久覆盖
   （已 gitignored；contributor 推荐方式）：
   ```xml
   <Project>
     <PropertyGroup>
       <BannerlordPath>D:\Games\Mount &amp; Blade II Bannerlord</BannerlordPath>
     </PropertyGroup>
   </Project>
   ```
3. **环境变量** `BannerlordPath`：
   ```powershell
   $env:BannerlordPath = "D:\Games\Mount & Blade II Bannerlord"
   dotnet build src\SovereignTowns.csproj -c Debug
   ```

## 运行日志

日志写入：

```
%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\
```

（**不是** module 目录 —— Steam 装在 `C:` 盘会触发 UAC 写入失败。）

## 硬约束（不可违反）

以下都是已经付过代价的 bug，是底线，不可协商。

1. **`TargetFramework = net472`**。Bannerlord v1.3.15 的 CLR 无法解析
   `netstandard 2.1.0.0` → MonoMod/Harmony 连锁崩溃，启动失败。
2. **`SaveBaseId = 1_900_000_000`**（位于 `src/SaveSystem/SovereignTownsTypeDefiner.cs`）。
   早期值 `100_000_000` 落在低 8 位数段，与其他 mod 共用该段会导致存档损坏。
   该值需保持低于 ButterLib 的 `2_002_018_000`。
3. **每个 `Saveable` 类型的 `LocalSaveId` 永不复用、永不重排**。删除字段时，
   保留 ID 并标 `[Obsolete]`，类型改为 `object`，让 vanilla 跳过。
4. **GameModel 必须在 `OnGameStart(Game, IGameStarter)` 中注册** ——
   *不是* `OnSessionLaunched`。到 `OnSessionLaunched` 时 Campaign 已经初始化完毕，
   此时调 `AddModel` 会破坏 vanilla 内部 model 列表。
5. **所有事件回调入口必须 `try { ... } catch { Logger.Error(...) }` 包裹整个函数体**。
   绝对不允许我们的异常逃逸到 vanilla 中。
6. **`HourlyTickPartyEvent` 回调必须第一行按 `PartyComponent` 类型过滤**。
   玩家每小时有数百支队伍 tick，触碰非 ST 队伍既不安全又会爆性能预算。
7. **当 `StPartyComponent` 子类被持久化的那一刻，存档对本 mod 形成硬依赖**
   （即 `StRecruiterPartyComponent` / `StTransferPartyComponent` /
   `StSallyPartyComponent` / `StPatrolPartyComponent`）。Mod 内没有移除流程。
8. **JSON 使用 `Newtonsoft.Json`**（随 vanilla 自带，
   位于 `$(GameBinPath)\Newtonsoft.Json.dll`，引用时 `Private=false`）。
   不要再写手撸的正则 / MiniJson 解析器。

## 一屏看完的架构

四层依赖栈，自顶向下，无向上引用；同层 Manager 互联一律走
`SovereignTownsCampaignBehavior`（唯一的事件分发中心）。

```
Layer 4  UI                 ：DiagnosticGameMenu, STPartyDialogRegistration,
                              ControlPanel (Gauntlet, src/Ui/),
                              WebConfig (HTTP, src/WebConfig/)
Layer 3  Dispatchers        ：CapitalManager ★ (src/Capital/),
                              CapitalLogisticsManager (src/Managers/),
                              RecruitmentDispatcher + PrisonerRecruitmentManager
                              + CapitalInPlaceRecruiter (src/Recruitment/),
                              PatrolDispatcher (src/Patrol/),
                              TransferDispatcher (src/Transfer/),
                              SallyDispatcher (src/SallyForth/),
                              PartyLifecycleManager (src/Lifecycle/)
Layer 3b Component instances：StPartyComponent（抽象基类, src/Parties/）,
                              StPatrolPartyComponent / StRecruiterPartyComponent /
                              StTransferPartyComponent / StSallyPartyComponent
Layer 2  Evaluators         ：RiskAssessmentService, TroopCompositionEvaluator,
                              TroopClassifier, TroopTemplateMatcher,
                              GenericTroopMatcher, HostilePartyScanner,
                              GarrisonPowerEvaluator, HorizonForecast
                              （src/Evaluators/）
Layer 2.5 算法核              ：MinCostFlow, UnifiedGarrisonSolver,
                              GarrisonAllocationSolver, DispatchInstruction,
                              RecruitmentTopology（src/Algorithm/）
                              —— 被 CapitalLogisticsManager 调用以规划
                              招募 + 跨定居点调拨。
Layer 1  Infrastructure     ：SovereignTownsSubModule, SovereignTownsCampaignBehavior,
                              SovereignTownsTypeDefiner, ConfigurationManager,
                              Logger, DecisionAuditLogger + ActivityNarrator
                              + ActivityFeed（src/Audit/）
支撑层                       ：src/Models/（GameModel 覆盖 —— 移速、军饷、
                              容量上限、Volunteer；ClanFinance 现在只是
                              SafeTownIncome 只读 helper，不再注册）；
                              src/Economy/（ModTreasury —— 直接从 Clan.Gold
                              扣 mod 支出；TreasuryUserActions —— Hero↔
                              Clan.Gold 转账；ClanGoldAccess —— 反射 setter；
                              ModExpenseLedger 开销账本）；
                              src/Settlement/（VanillaSuppressionManager,
                              VanillaPatrolSuppressor）；
                              src/Templates/（TroopTemplateModeService）；
                              src/Upgrades/（TroopUpgradeService,
                              GarrisonXpInjector）；
                              src/Patches/（Harmony 补丁）；
                              src/Coordination/, src/Common/（工具/助手）。
```

★ **CapitalManager** 是运行时语义核心：每个受管理氏族至多一个首府。
**CapitalLogisticsManager** 是首府就地招募 / 派出征兵队 / 跨定居点调拨的
**周期性决策点** —— 由最小费用流基于首府级快照求解。tick 频率由
`FiscalAutonomy.CapitalLogisticsTickHours` 控制（默认 6 游戏小时，
可调范围 [1, 24]；该值同时也是时间展开 MCMF solver 中"一个 tick"的
时长）。当首府失守时，`PartyLifecycleManager.MigrateAllOrDisband` 会把
在途队伍迁移到新首府，或如果无可迁移则就地解散。

## 测试

**没有单元测试**。验证 = 启动游戏，观察日志。

## 协议

MIT —— 见 [LICENSE](LICENSE)。
