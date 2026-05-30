# 主权城镇 / Sovereign Towns

[English](README.md) | [简体中文](README.zh-CN.md)

**把氏族日常的杂事 —— 驻军、招募、巡逻、出击、账本 —— 全部交给首府。** 最小费用流调度器决定兵力怎么流；政策由你定。

> **骑马与砍杀 II：霸主 v1.3.15** · 依赖 **Bannerlord.Harmony** · 与 **ImprovedGarrisons** / **GarrisonDoSomething** 不可共存 · 兼容 **RBM** · 预发布（v0.0.1，存档与配置 schema 可能在 commit 间不兼容）

---

## 功能

氏族选一个定居点作为 **首府**（capital），其余一切围绕首府运行。

### 首府主导的自动化

- **驻军构成** —— 每个所属城镇 / 城堡都按模板维护：文化筛选 + 可选的优先 / 禁用兵种名单。养多少兵、什么等级由调度器按预算决定 —— 你定政策，无需手填数字。
- **招募** —— 首府就地招志愿兵 + 派征兵队去村庄 + 战俘原地转化。
- **物流** —— 调拨队在氏族网络内调兵，由最小费用流求解器基于首府级快照规划。
- **防御** —— 巡逻队绕每块领地巡查驱赶盗匪；真正威胁出现时派出击队。
- **首府失守恢复** —— 首府陷落时在途队伍干净地迁到新首府或就地解散。

### 氏族经济

vanilla 没有独立的"氏族金库"概念 —— `Clan.Gold` 是计算属性 `=> Leader?.Gold ?? 0`。对玩家氏族就是 `Hero.MainHero.Gold`，mod 以此为唯一真相。

- vanilla 把 `clan.Fiefs` 全部收入汇入 `Clan.Gold`（mod 不拦截）。
- mod 自身支出 —— 派出队伍种子金、招募人头费、装备升级费 —— 走 vanilla `Hero.ChangeHeroGold` 直接从 `Clan.Gold`（即 `Hero.MainHero.Gold`）扣。
- 每支派出队伍（征兵 / 巡逻 / 出击 / 调拨）携带 vanilla `MobileParty.PartyTradeGold` 作为运行预算：出发前先备足够走完第一段路程的食物，途经定居点再补给，把战利品卖回钱袋，解散时余款归还氏族领袖。整条经济与 vanilla `Settlement.Gold` 闭环 —— 与 vanilla 商队走同一通道。
- "金币不足时暂停支出"开关（默认开）阻止 mod 把 `Clan.Gold` 扣到负。
- 作坊 / 商队仍按 vanilla 走 `Hero.Gold` —— 与上面是同一个账户，没有独立账本。
- 每次买食物在左下角推一条日志：`[Sovereign Towns] {队伍} bought {N} {物品} at {地点} (-{N}d)`（仅玩家氏族部队）。

### 卫队（首府常驻精锐）

每座受管首府都维持一支 **卫队（Honor Guard）**—— 永驻首府的私属 party（容量 300），按 per-troop 模板招募，围城时被 vanilla `Town.GetDefenderParties` 自动纳入守城兵力。MCMF 调度器在常规驻军达到充足目标之后才把村庄供给灌入卫队（cost 平衡使其严格次于常规驻军，不会抽空线列兵）；mod 每日为它备粮，让伤兵像普通守军一样在城内自愈。在「卫队编制」标签页编辑名册，在「卫队」标签页或城镇菜单的「管理卫队」选项查看实时状态。

### 旋钮与可观测性

- **节奏可调** —— 后勤 tick 在 1 小时到 24 小时之间任意调（默认 6 小时）。
- **游戏内控制面板** —— 大地图左侧贴边的常驻竖向按钮，加每个所属城镇/城堡菜单上的入口。所有配置都在这里，共六个标签页：功能开关、策略参数、兵种编制、卫队编制、状态一览、卫队。
- **活动流** —— 每次派出 / 招募 / 调拨 / 出击都有记录，「状态一览」标签页可查。
- **大地图追踪** —— 可选：把本 Mod 的活动部队像「追踪商队」一样显示为大地图标记，在「功能开关」里开关。
- **本地化** —— 英文与简体中文。

---

## 作用范围

目前 **仅玩家氏族**。AI 氏族管理在代码层已完整实装（`CapitalRegistry` / `VanillaSuppressionManager` 都有对称的 AI 路径），但默认关闭 —— `EnabledFeatures.ApplyToAiSettlementsToo` 默认 `false`，且故意未在面板暴露，等待平衡测试完成。开关一旦开启，会把每个持有首府的氏族纳入同一套接管规则，并把它们的招募完全引导到 mod 渠道。

---

## 安装

1. 安装 [Bannerlord.Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006)（以及任何其他常规前置依赖）。
2. 把本 mod 的 `SovereignTowns/` 目录与 `Native/` 等并列放进 `Modules/`。
3. 在启动器里启用 **Sovereign Towns**（加载顺序自动处理 —— 模块在 vanilla 故事模块之后加载）。
4. 开档，进入一座你拥有的城镇或城堡，在菜单里选 **Sovereign Towns: set as capital（设为首府）** —— 这座首府就是 mod 运行一切的中枢。
5. 点大地图贴边按钮打开控制面板设定政策；之后 mod 全权接管。

日志写入：

```
%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\
```

—— 不在 module 目录，避免 Steam 装 `C:` 盘时触发 UAC 写入失败。

---

## 从源码构建

```powershell
dotnet build src\SovereignTowns.csproj -c Debug
```

`Directory.Build.props` 中 `BannerlordPath` 默认指向 Steam 标准安装位置。按优先级覆盖：

1. **CLI 参数** —— `dotnet build … -p:BannerlordPath="D:\Games\Mount & Blade II Bannerlord"`
2. **`Directory.Build.props.user`** —— gitignored，一行 XML 搞定
3. **环境变量** —— `$env:BannerlordPath = "..."`

`DeployToGame` MSBuild target 在 `AfterTargets="Build"` 时把 DLL、GUI prefab、语言 XML 自动拷贝到 `$(BannerlordPath)\Modules\SovereignTowns`。

**没有单元测试** —— 验证 = 启动游戏看日志。

---

## 仓库目录结构

```
.
├── src/                     # C# 源码 + csproj
├── Module/                  # SubModule.xml + Gauntlet prefab + ModuleData
├── Directory.Build.props
├── README.md  README.zh-CN.md  LICENSE
```

本地保留、不入 git：`_research/`（反编译的 vanilla + 参考 mod）、`audits/`（设计笔记）、`docs/`（计划）、`.claude/`（AI 工具状态）。

---

## 给 mod 开发者

<details>
<summary><strong>架构概览</strong></summary>

四层依赖栈，自顶向下，无向上引用；同层 manager 互联一律走唯一的 `SovereignTownsCampaignBehavior` 事件分发中心。

```
Layer 4   UI                 DiagnosticGameMenu、STPartyDialogRegistration、
                             ControlPanel（Gauntlet）—— 唯一 UI 真相源
Layer 3   Dispatchers        CapitalManager ★、CapitalLogisticsManager、
                             RecruitmentDispatcher、PrisonerRecruitmentManager、
                             PatrolDispatcher、TransferDispatcher、SallyDispatcher、
                             PartyLifecycleManager
Layer 3b  Components         StPartyComponent + Patrol / Recruiter / Transfer / Sally
Layer 2   Evaluators         Risk、TroopClassifier、TemplateMatcher、
                             HostilePartyScanner、GarrisonPowerEvaluator …
Layer 2.5 算法核              MinCostFlow、UnifiedGarrisonSolver、
                             GarrisonAllocationSolver、RecruitmentTopology
Layer 1   Infrastructure     SubModule、CampaignBehavior、TypeDefiner、
                             ConfigurationManager、Logger、DecisionAuditLogger
支撑层                        Models/（vanilla GameModel 覆盖）、
                             Economy/（ModTreasury 走 vanilla Hero.ChangeHeroGold +
                                      ledger / audit；ClanGoldAccess 薄 facade）、
                             Settlement/（vanilla 招募抑制）、
                             Templates/、Upgrades/、Patches/、Coordination/、Common/
```

★ `CapitalManager` 是 per-clan：每个受管氏族最多一个首府。
`CapitalLogisticsManager` 跑周期性决策（招募 + 跨定居点调拨）：把首府级快照丢给 MCMF solver，再把 flow 解码成 dispatch 指令。tick 间隔由 `FiscalAutonomy.CapitalLogisticsTickHours` 控制（默认 6，范围 1–24）；该值同时也是时间展开 MCMF 中一个 tick 的时长。

</details>

<details>
<summary><strong>硬约束 —— 已经付过代价的 bug，别改回去</strong></summary>

1. **`TargetFramework = net472`**。v1.3.15 的 CLR 无法解析 `netstandard 2.1.0.0` —— 改其它任何 target 都会让 MonoMod/Harmony 链式崩溃。
2. **`SaveBaseId = 1_900_000_000`**（`src/SaveSystem/SovereignTownsTypeDefiner.cs`）。早期 `100_000_000` 落在低位 8 位段，会与其他 mod 共用 → 存档损坏。保持低于 ButterLib 的 `2_002_018_000`。
3. **每个 `Saveable` 类型的 `LocalSaveId` 永不复用、永不重排**。删除字段时保留 ID 并标 `[Obsolete]`，类型改为 `object`，让 vanilla 跳过。
4. **GameModel 在 `OnGameStart` 注册，不在 `OnSessionLaunched`**。到 `OnSessionLaunched` 时 Campaign 已 finalize，调 `AddModel` 会破坏 vanilla 内部 model list。
5. **所有事件回调入口必须 `try { ... } catch { Logger.Error(...) }` 包裹整个函数体**。绝不让我们的异常逃逸到 vanilla。
6. **`HourlyTickPartyEvent` 回调必须第一行按 `PartyComponent` 类型过滤**。玩家每小时有数百队伍 tick，触碰非 ST 队伍既不安全又会爆性能预算。
7. **当 `StPartyComponent` 子类被持久化的那一刻，存档对本 mod 形成硬依赖**。mod 内没有移除流程。
8. **JSON 走 Newtonsoft.Json**（vanilla 自带，位于 `$(GameBinPath)\Newtonsoft.Json.dll`，`Private=false`）。不要再写手撸的正则 / MiniJson 解析器。

</details>

---

## 协议

MIT —— 见 [LICENSE](LICENSE)。
