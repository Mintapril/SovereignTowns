# 设计文档：游戏内控制面板（1:1 复刻 WebUI）

- 日期：2026-05-20
- 目标：在游戏内用原生 Gauntlet UI 实现一个控制面板，**结构布局 1:1 复刻** `SovereignTowns/WebUI/index.html`，**交互行为完全对齐**。
- 决策前提（已与用户确认）：
  - 视觉精度 = 结构布局 1:1 + 交互行为对齐 + 金/黑中世纪主题用 Gauntlet 纯色 Brush 近似。**不**逐像素复刻 CSS 渐变 / SVG 噪点 / 描金角框。
  - 一次性给出全部 6 个标签页的完整设计。
  - WebUI **保留不动**，本面板是新增的并行入口，二者同时启用。
  - 入口 = 大地图左侧一个常驻按钮（模仿 IG 在地图上加按钮的做法），点击弹出面板。
  - IG 仅作**技术实现参考**；IG 面板本身「不够精致、控件过大」，不作为视觉目标。
  - 弹出的面板比 IG 大、可交互控件占比更小，尽量贴合 WebUI 的紧凑布局密度；避免 IG 那种控件过大导致的观感问题。

---

## 1. 技术路线

| 候选 | 结论 |
|---|---|
| MCM | 否决。属性声明式，只有 5 类控件、无标签页、无自定义控件、无可搜索列表、无表格。代码对 UI 的可控程度 ≈ 0。 |
| Gauntlet 叠加层（`GauntletLayer` on MapScreen） | 否决。本面板是一个完整的全屏面板，不是地图浮层。 |
| **原生 Gauntlet 独立屏幕（`ScreenBase`）** | **采用。** prefab XML 像素级布局 + ViewModel 任意数据绑定，是唯一能 1:1 复刻的路线。IG 的 `ConfigMenuGauntletScreen` 即现成样板。 |

**数据层零适配**：游戏内面板与配置系统同进程，直接调用 `ConfigurationManager` / `TroopDumper` / `ModExpenseLedger`，**完全跳过 WebConfig 的 HTTP + JSON 层**。

---

## 2. 架构

照搬 IG 验证过的 `ScreenBase → GauntletLayer → ViewModel → prefab` 模式，并遵守 CLAUDE.md 的分层规则（UI = Layer 4）。分两部分：常驻地图按钮 + 弹出面板。

```
ControlPanelMapButtonView : MapView      常驻地图层：在 MapScreen.Instance 上加一个不拦截地图输入的
  └─ GauntletLayer                       GauntletLayer，左侧渲染一个「打开控制面板」按钮
       └─ MapButtonVM : ViewModel        按钮 VM；Command.Click → PushScreen(new ControlPanelScreen())

                          —— 点击按钮，弹出 ——▼

ControlPanelScreen : ScreenBase          屏幕容器；创建 GauntletLayer、LoadMovie、焦点、暂停游戏、清理
  └─ GauntletLayer  LoadMovie("SovereignTownsControlPanel", vm)
       └─ ControlPanelVM : ViewModel     根 VM：6 个 TabVM、ActiveTabIndex、表头状态、活动日志
            ├─ FeaturesTabVM             Tab1 功能开关
            ├─ StrategyTabVM             Tab2 策略参数
            ├─ CompositionTabVM          Tab3 兵种编制
            ├─ TemplatesTabVM            Tab4 兵员模板
            ├─ BranchesTabVM             Tab5 非首府驻军
            └─ FinanceTabVM              Tab6 财务
```

`MapView` / `MapScreen` 来自 `SandBox.View` 程序集（IG 的 `ImprovedGarrisonsUIGauntlet : MapView` 即此做法）。SubModule.xml 已依赖 `Sandbox` / `SandBoxCore` 模块，仅需在 csproj 加 `SandBox.View` 的 `<Reference>`（HintPath 指向 `Modules\SandBox\bin\Win64_Shipping_Client\SandBox.View.dll`，非 GameBinPath）。

### 2.1 文件结构（全部新增，无破坏性改动）

```
SovereignTowns/src/Ui/ControlPanel/
  ControlPanelMapButtonView.cs MapView 子类：大地图左侧常驻按钮（模仿 _research 的 ImprovedGarrisonsUIGauntlet.cs）
  MapButtonVM.cs               地图按钮 ViewModel（Command 触发弹出面板）
  ControlPanelScreen.cs        ScreenBase 子类（模仿 _research 的 ConfigMenuGauntletScreen.cs）
  ControlPanelVM.cs            根 ViewModel
  ControlPanelData.cs          进程内数据适配：克隆 GlobalConfig 工作副本、读兵种、读财务、写回
  ControlPanelLoc.cs           Tr(zh,en) 双语助手 + 游戏语言探测
  ControlPanelSpecs.cs         静态 spec 元数据表（从 WebUI JS 的 *Specs / settingsGroups 移植）
  Tabs/  FeaturesTabVM / StrategyTabVM / CompositionTabVM / TemplatesTabVM / BranchesTabVM / FinanceTabVM .cs
  Items/ ToggleRowVM / SliderRowVM / SettingsGroupVM / ChipVM / TroopRowVM / TroopTemplateRowVM /
         FinanceTableVM / FinanceRowVM / LogEntryVM .cs

SovereignTowns/SovereignTowns/GUI/Prefabs/   （csproj 已有 GUI/Prefabs/*.xml 部署 target，扁平非递归）
  SovereignTownsMapButton.xml       大地图左侧「打开控制面板」按钮（小 prefab）
  SovereignTownsControlPanel.xml    根 movie：表头 + 左侧标签栏 + 右侧 6 个 tab 容器
  STCPToggleRow.xml                 开关行模板
  STCPSliderRow.xml                 滑块+数值行模板
  STCPChip.xml                      筛选 chip 模板
  STCPTroopCatalogRow.xml           兵种名录行模板
  STCPTroopTemplateRow.xml          已选名单行模板
  STCPFinanceRow.xml                财务表格行模板
```

改动到的既有文件，仅两处：
1. `SovereignTownsCampaignBehavior.cs` —— 加几行地图按钮 bootstrap（见 §3）。
2. `src/SovereignTowns.csproj` —— 新增 `SandBox.View` 程序集 `<Reference>`。

`DiagnosticGameMenu.cs` / `SubModule.xml` 不动。

### 2.2 工作副本 / dirty 模型（对齐 WebUI）

WebUI 持有一份可自由改写的 `config` 对象 + `dirty` 标记，保存时整体 PUT。游戏内同理：

- 打开面板时：`ControlPanelData` 把 `ConfigurationManager.Current` 深拷贝成一份工作副本 `GlobalConfig`（用 Newtonsoft 序列化往返实现深拷贝——JSON 是项目硬约束，已绑定 Newtonsoft）。
- 所有 TabVM 改的都是这份工作副本；任何改动调 `ControlPanelVM.MarkDirty()`。
- 保存：`ControlPanelData.Save(工作副本)` → `ConfigurationManager.ReplaceAndSave(...)`，落盘并触发 `OnConfigChanged`。
- 「↻ 重读磁盘」：`ConfigurationManager.TryReload` 后重新克隆工作副本。
- 关闭时若 dirty，弹 `InformationManager` 确认框（对应 WebUI 的 `beforeunload` 拦截）。

### 2.3 主题

不使用自定义 Brush 文件（避免改 csproj 部署 glob、降低迭代）。金/黑主题用：vanilla 内置 Brush + `Widget` 的 `Color` + 纯色 sprite（如 vanilla 的 1×1 白图）叠色实现深色面板 / 金色描边 / 金色文字。`gold-rule`、`diamond-divider` 用细条 `Widget`；`tier-dot` 用小方/圆 `Widget` 着色。若后续证明 vanilla Brush 不够，再补一个 `GUI/Brushes/*.xml` 文件并相应加 csproj 部署行（列为应急项）。

### 2.4 本地化

面板自带约 120+ 段文案（标签页名、8 个开关、~40 条 spec 的 label+hint、文化过滤、按钮等）。**采用内联双语**：`ControlPanelLoc.Tr(中文, English)` 按当前游戏语言返回——与 WebUI 的 `tr()` 完全同构。理由：避免向 `std_sovereigntowns_strings.xml` 灌 120+ 个 key、保持 spec 表可读、与 WebUI 行为一致。游戏语言探测复用 mod 现有机制（即 WebConfig `uiLang` 的同一套判定）。

### 2.5 面板尺寸与布局密度

- 弹出面板取**接近全屏的大画布**（如固定参考分辨率 1600×900，按屏幕等比缩放），明显大于 IG 的配置屏。
- 控件**紧凑**：对齐 WebUI 的密度——细滑块、13–15px 字号、行高紧凑、表头/标签栏占比小。**不**采用 IG 那种大块厚重控件。
- 整体维持 WebUI 的 `max-w-7xl` 居中 + 12 列栅格观感：左栏窄（标签栏 + 活动日志）、右栏宽（内容区）。
- 地图按钮本身小巧，仅占大地图左侧边缘一小块。

---

## 3. 入口：大地图常驻按钮

模仿 IG 的 `ImprovedGarrisonsUIGauntlet : MapView`：

- `ControlPanelMapButtonView : MapView`，在 `CreateLayout()` 里 `new GauntletLayer(...)` → `LoadMovie("SovereignTownsMapButton", MapButtonVM)` → `AddLayer` 到 `MapScreen.Instance`。
- `InputRestrictions.SetInputRestrictions(false, ...)`：按钮层**不拦截**地图拖拽 / 缩放，玩家操作大地图不受影响。
- prefab 在大地图**左侧边缘**渲染一个金/黑主题的小按钮（按 WebUI 主题描金，不抄 IG 按钮外观）。
- **Bootstrap**：`MapScreen.Instance` 在 `OnSessionLaunched` 时未必就绪。照搬 IG `UIManager.TryInitializeImprovedGarrisonsUI` 的做法——在 `SovereignTownsCampaignBehavior` 的 tick 回调里，检测到地图态且 `MapScreen.Instance != null` 且按钮尚未创建时实例化一次（幂等、try/catch 包裹，遵守 CLAUDE.md 不变量 5）。
- 点击按钮 → `MapButtonVM` 的 Command → `ScreenManager.PushScreen(new ControlPanelScreen())`。

**游戏暂停**：面板从地图态打开，地图态时间在走，故 `ControlPanelScreen` / `ControlPanelVM` **必须显式暂停**——`OnInitialize` 时 `Game.Current.GameStateManager.RegisterActiveStateDisableRequest(...)`，`OnFinalize` / 关闭时 `Unregister...` 配对（与 IG `ConfigMenuVM.PauseGame/UnpauseGame` 一致）。关闭：`ScreenManager.PopScreen()` 回到大地图。

地图键盘热键（类似 IG 的 Ctrl+Y）本轮**不做**——入口就是这个可见按钮。

---

## 4. 布局 1:1 映射 — 表头 / 左栏

### 表头（对应 index.html `<header>`）
- 左：⚜ 字形 + 「SOVEREIGN TOWNS」标题 + 「控制面板」副标题。
- 右：状态 pill ×2 + 「↻ 重读」按钮 + 「保存改动」按钮 + 底部金色细线。
- **WebUI 专属元素的处理**：
  - `SERVER 在线/失联` pill → 游戏内无服务器概念，**改为显示首府名**（`首府: <名>`），保留双 pill 布局。
  - dirty/saved pill → 保留，绑定 `ControlPanelVM.IsDirty`。
  - `↻ 重读磁盘` → 保留（同进程虽少见分歧，但玩家手编 global.json 或同时开着 WebUI 时仍有意义）。
- 红色 warning / 绿色 success 横幅：保留，绑定 VM 的 `Warning` / `Success` 字符串，保存成功/失败时显示。

### 左侧标签栏（对应 `<aside>`）
- 6 个标签按钮，编号 `01`–`06`，激活态高亮。绑定 `ActiveTabIndex`。
- 下方「活动日志」框：可滚动，`MBBindingList<LogEntryVM>`，保留最近 20 条。日志内容 = 面板操作日志（配置已读取 / 已保存 / 模板已清空…），与 WebUI 一致。

---

## 5. 布局 1:1 映射 — 6 个标签页

### Tab 1 功能开关（FeaturesTabVM）
标题 + 2 段说明 + 菱形分隔线 + 8 个开关行。开关：`AutoRecruitment` / `AutoPatrol` / `TroopTransfers` / `SallyForth` / `SuppressVanillaGarrisonRecruitment` / `PauseSpendingWhenBroke` / `ShowDailySummary` / `VerboseLogging`，全部 root = `EnabledFeatures`。
- `MBBindingList<ToggleRowVM>`，行模板 `STCPToggleRow.xml`（勾选框 + 标题 + 说明）。

### Tab 2 策略参数（StrategyTabVM）
标题 + 2 段说明 + 菱形分隔线 + 分组筛选 chip 行 + 「显示高级参数」勾选 + 当前分组标题/说明 + 「本组恢复默认」按钮 + spec 行列表。
- 分组：目标预算 / 招募 / 巡逻调拨 / 主动出击 / MCMF 调度（adv）/ 生命周期升级。`ShowAdvanced=false` 时隐藏 adv spec，并隐藏全空的 MCMF 组（移植 `visibleSettingsGroups` 逻辑）。
- 每条 spec 行：bool → `STCPToggleRow`；数值 → `STCPSliderRow`（label + hint + 「↺ 恢复默认 X」链接 + 范围滑块 + 数值输入框，三者联动）。
- `MBBindingList<SettingsGroupVM>`，每组内 `MBBindingList<ToggleRowVM | SliderRowVM>`。
- spec 元数据（min/max/step/discrete/def/adv/root/key）从 WebUI 的 `budgetSpecs` / `resourceSpecs` / `thresholdSpecs` / `settingsGroups` 完整移植进 `ControlPanelSpecs.cs`。

### Tab 3 兵种编制（CompositionTabVM）
标题 + 说明 + 2 个模式按钮（通用比例匹配 / 精确兵员模板，对应 `GlobalDefaults.UseGenericMatching`）+ 菱形分隔线。
- 精确模式 → 显示一张引导卡（指向 Tab 4）。
- 通用模式 → 文化过滤 chip（`GenericCultureFilter`：玩家文化/首府文化/不过滤）+ 4 条兵种比例滑块（`Cavalry/HorseArcher/Infantry/Ranged Ratio`，带 Σ 显示、自动按比例归一化、恢复默认）+ Tier 范围（最低/最高各 6 个 chip，移植 min≤max 联动与置灰逻辑）。

### Tab 4 兵员模板（TemplatesTabVM）
表头（已选兵种数 / 估算人数 / 目标驻军 / 清空按钮）+ 说明 + 模式提示横幅 + 菱形分隔线 + 左右两栏。
- 左「兵种名录」：搜索框 + 3 行筛选 chip（文化 / 兵种类型 / Tier）+「隐藏已选」+ 匹配计数 + 可滚动兵种列表。每行：tier 圆点、`Txx`、名称（搜索高亮）、文化徽章、类型字形、`＋加入`/`✓已加` 按钮。列表上限显示前 200 条（同 WebUI）。
- 右「已选名单」：Σ 显示 + 可滚动列表。每行：tier 圆点、名称、文化/tier/类型、占比%、≈人数、✕ 移除、占比滑块。
- 占比的自动归一化（add/update/remove 时 rescale 至 Σ=1.0、浮点漂移修正）完整移植 `addTroop` / `updateTroopRatio` / `removeTroop` / `_snapTroopSumTo1`。
- 兵种数据：`TroopDumper` 进程内枚举（id/name/tier/type/culture/cultureName），不读 troops.json 磁盘文件。

### Tab 5 非首府驻军（BranchesTabVM）
标题 + 「↺ 全部恢复默认」+ 说明 + 菱形分隔线 + 2 张滑块卡片：`BranchDefaults.TargetPower`（默认 150）、`BranchDefaults.LowTierMinFraction`（默认 0.20）。各自带「↺ 恢复默认」。

### Tab 6 财务（FinanceTabVM）
标题 + 说明 + 3 张汇总表（今日 / 本周 / 全部，每张：分类行 + 合计页脚）+ 近期流水表（最近 50 条：时间 / 类别 / 金额 / 备注）。
- 数据：`ModExpenseLedger.BuildReport()` 进程内调用。
- 刷新：WebUI 每 5 秒轮询；游戏内面板打开期间游戏暂停、财务不变，故**只在面板打开 / 切到本 tab 时刷新一次**（布局完全相同，去掉无意义的轮询）。

---

## 6. 数据绑定与控件

- `[DataSourceProperty]` 暴露属性；列表用 `MBBindingList<T>`；prefab 用 `ItemTemplate` 重复渲染行；事件用 `Command.Click`。
- 条件显隐（WebUI 的 `x-show`）→ prefab `IsVisible` 绑定到 VM 的 bool 属性。
- `MBBindingList` 增删不像 WPF 自动通知 —— 过滤兵种列表等场景需手动重建并 `OnPropertyChanged`（IG 调研已确认此坑）。
- 滑块 / 数值框 / 勾选框 / 按钮用 vanilla Widget；标签页、滚动列表用 vanilla `ListPanel` / `ScrollablePanel`。

---

## 7. 风险与迭代驱动因素

| 风险 | 缓解 |
|---|---|
| prefab XML 每改一次要重启游戏验证 → 主要迭代成本 | 内部按「先骨架后逐 tab」实现顺序推进；骨架（地图按钮 + 弹出/关闭/暂停 + 表头保存 + 一个最简单 tab）先跑通，验证架构与主题基线后再批量复刻其余 tab。 |
| Brush 主题与 WebUI 视觉有差距 | 已与用户约定为「主题近似」，可接受。 |
| `EditableTextWidget` 数值解析 / 越界钳制 | VM setter 内统一钳制 + 解析失败回退（移植 `setConfigValue`）。 |
| `MapScreen.Instance` 在 session 启动早期为 null | 照搬 IG `UIManager`：tick 内反复检测，就绪且未创建时才实例化一次；幂等 + try/catch。 |
| `SandBox.View.dll` 不在 GameBinPath | HintPath 指向 `$(BannerlordPath)\Modules\SandBox\bin\Win64_Shipping_Client\`，`Private=false`。 |

---

## 8. 验收

- `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`，DeployToGame 自动部署 DLL + prefab。
- 游戏内：进大地图 → 点左侧按钮弹出面板 → 逐 tab 核对布局与交互 → 改动 → 保存 → 关闭面板确认游戏恢复、`global.json` 已更新 → 重开面板确认读到新值。
- 无单元测试（项目约定）；验证 = 启动游戏 + 看 `ModLogs/SovereignTowns/` 日志。

## 9. 不在本轮范围

- 不删除 / 不改动 WebUI（与本面板并行启用）。
- 不做键盘热键入口（入口即大地图左侧按钮）。
- 不逐像素复刻 CSS 视觉、不绘制 sprite 美术资源。
- 不实现 WebUI 未暴露的 `/api/settlements/{id}/activities` 定居点活动视图。
