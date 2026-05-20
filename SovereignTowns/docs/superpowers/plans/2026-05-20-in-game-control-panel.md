# 游戏内控制面板（1:1 复刻 WebUI）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在游戏内用原生 Gauntlet UI 实现一个控制面板，结构布局 1:1 复刻 `SovereignTowns/WebUI/index.html`，入口为大地图左侧常驻按钮。

**Architecture:** `ControlPanelMapButtonView : MapView` 在大地图常驻一个按钮 → 点击 `ScreenManager.PushScreen(new ControlPanelScreen())` 弹出 `ScreenBase` 全屏面板 → `ControlPanelVM` 根 VM 下挂 6 个 TabVM。数据层同进程直接调 `ConfigurationManager` / `TroopDumper` / `ModExpenseLedger`，不走 HTTP/JSON。配置改在一份深拷贝工作副本上，保存走 `ConfigurationManager.ReplaceAndSave`。

**Tech Stack:** C# net472、TaleWorlds Gauntlet UI（`ScreenBase` / `MapView` / `GauntletLayer` / `ViewModel` / `MBBindingList` / prefab XML）、Newtonsoft.Json（深拷贝）。

**设计文档:** `docs/superpowers/specs/2026-05-20-in-game-control-panel-design.md`（已经用户确认）。

---

## 关于本计划的两点说明（执行前必读）

1. **没有单元测试。** CLAUDE.md 明确：项目无单元测试，验证 = 启动游戏看 `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\` 日志。因此本计划每个任务的「验证」步骤 = `dotnet build` 编译通过 +（功能可达时）描述好的游戏内核对。**不**写 xUnit/NUnit 测试。

2. **Gauntlet prefab XML 的处理。** prefab XML 语法无法脱离游戏 API 凭空写对。涉及 prefab 的任务给出**精确的 widget 树结构 + 绑定名契约 + 必读的 vanilla 参考文件**，由执行者对照 vanilla prefab 写出 XML。这是有意的取舍，不是占位符——结构与绑定名是完全确定的。C# 代码该给全的地方给全。

**Gauntlet 语法参考文件**（执行 Task 4 起按需阅读，路径相对游戏安装根 `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`）：
- `Modules\Native\GUI\Prefabs\` 下任意 prefab —— 看 `Widget` / `ListPanel` / `ScrollablePanel` / `RichTextWidget` / `ButtonWidget` / `EditableTextWidget` / `ItemTemplate` / `@属性` 绑定 / `Command.Click` 写法。
- `Modules\SandBox\GUI\Prefabs\MapBar\` —— 看地图上的 widget 怎么定位。
- `_research\ImprovedGarrisons\ImprovedGarrisons.ConfigOptionsMenu\ConfigMenuGauntletScreen.cs`、`ImprovedGarrisons.ImprovedGarrisonsUI\ImprovedGarrisonsUIGauntlet.cs` —— ScreenBase / MapView 的 C# 串联范式。

**CLAUDE.md 硬约束提醒：** 不变量 5 —— 所有事件 / tick 回调体必须 `try { … } catch { Logger.Error(…) }`，绝不让异常逃逸进 vanilla。本计划新增的 `MapView.CreateLayout`、tick bootstrap、`Command` 回调全部适用。

---

## 文件结构

**新增（全部在 `SovereignTowns/src/Ui/ControlPanel/`）：**

| 文件 | 职责 |
|---|---|
| `ControlPanelLoc.cs` | `Tr(zh,en)` 双语助手 + 游戏语言探测 |
| `ControlPanelData.cs` | 进程内数据适配：克隆 `GlobalConfig` 工作副本、`Save`、`Reload`、取兵种、取财务 |
| `ControlPanelSpecs.cs` | 静态 spec 元数据表（从 WebUI JS 移植） |
| `ControlPanelMapButtonView.cs` | `MapView` 子类：大地图左侧常驻按钮 |
| `MapButtonVM.cs` | 地图按钮 ViewModel |
| `ControlPanelScreen.cs` | `ScreenBase` 子类：弹出面板的屏幕容器 |
| `ControlPanelVM.cs` | 根 ViewModel：表头状态、6 个 TabVM、活动日志、保存/重读/关闭 |
| `Tabs/FeaturesTabVM.cs` | Tab1 功能开关 |
| `Tabs/StrategyTabVM.cs` | Tab2 策略参数 |
| `Tabs/CompositionTabVM.cs` | Tab3 兵种编制 |
| `Tabs/TemplatesTabVM.cs` | Tab4 兵员模板 |
| `Tabs/BranchesTabVM.cs` | Tab5 非首府驻军 |
| `Tabs/FinanceTabVM.cs` | Tab6 财务 |
| `Items/ToggleRowVM.cs` | 开关行 VM |
| `Items/SliderRowVM.cs` | 滑块+数值行 VM |
| `Items/SettingsGroupVM.cs` | 策略参数分组 VM |
| `Items/ChipVM.cs` | 筛选 chip / tier chip VM |
| `Items/TroopRowVM.cs` | 兵种名录行 VM |
| `Items/TroopTemplateRowVM.cs` | 已选兵种行 VM |
| `Items/FinanceTableVM.cs` | 财务汇总表 VM（今日/本周/全部各一个） |
| `Items/FinanceRowVM.cs` | 财务表格行 VM |
| `Items/LogEntryVM.cs` | 活动日志条目 VM |

**新增 prefab（`SovereignTowns/SovereignTowns/GUI/Prefabs/`，扁平）：**
`SovereignTownsMapButton.xml`、`SovereignTownsControlPanel.xml`、`STCPToggleRow.xml`、`STCPSliderRow.xml`、`STCPChip.xml`、`STCPTroopCatalogRow.xml`、`STCPTroopTemplateRow.xml`、`STCPFinanceRow.xml`

**修改既有文件（3 处）：**
- `src/SovereignTowns.csproj` —— 加 `SandBox.View` 引用（Task 1）。
- `src/WebConfig/TroopDumper.cs` —— 暴露进程内兵种枚举 API（Task 3）。
- `src/Campaign/SovereignTownsCampaignBehavior.cs` —— 加地图按钮 bootstrap（Task 5）。

---

## Task 1: csproj 加 SandBox.View 引用

**Files:**
- Modify: `SovereignTowns/src/SovereignTowns.csproj`（TaleWorlds 引用 `ItemGroup`，约 23-88 行之间）

- [ ] **Step 1: 在 TaleWorlds 引用 ItemGroup 末尾（`Newtonsoft.Json` 引用之后、`</ItemGroup>` 之前）加入 SandBox.View 引用**

`MapView` / `MapScreen` 在 `SandBox.View.dll`，该 dll 不在 `$(GameBinPath)`，而在 SandBox 模块的 bin 下。`$(BannerlordPath)` 来自 `Directory.Build.props`。

```xml
    <Reference Include="SandBox.View">
      <HintPath>$(BannerlordPath)\Modules\SandBox\bin\Win64_Shipping_Client\SandBox.View.dll</HintPath>
      <Private>false</Private>
    </Reference>
```

- [ ] **Step 2: 编译验证引用解析**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 编译成功（尚无新代码，只验证 `SandBox.View.dll` HintPath 能被解析、不报 `MSB3245` 找不到引用）。若报找不到，确认游戏安装路径下确有 `Modules\SandBox\bin\Win64_Shipping_Client\SandBox.View.dll`。

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/SovereignTowns.csproj
git commit -m "build: reference SandBox.View for in-game control panel MapView"
```

---

## Task 2: ControlPanelLoc — 双语助手

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/ControlPanelLoc.cs`

- [ ] **Step 1: 写 ControlPanelLoc.cs**

游戏语言探测：用 `TaleWorlds.Localization.LocalizedTextManager` 或 `BannerlordConfig.Language` 取当前语言；以 `"简体中文"` / `"zh"` 开头判定中文。先读 `src/WebConfig/` 下 `WebConfigEndpoints.cs` 或 `WebConfigServer.cs` 里计算 `uiLang` 的那段代码，**复用同一套判定逻辑**（保证与 WebUI 语言一致）。下面用占位 `DetectIsChinese()`，执行时换成查到的真实判定。

```csharp
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 控制面板的内联双语助手。镜像 WebUI 的 tr(zh,en)：不向 std_sovereigntowns_strings.xml
/// 灌 100+ 个 key，面板自带文案按当前游戏语言就地切换。
/// </summary>
internal static class ControlPanelLoc
{
    private static bool? _isZh;

    /// <summary>当前游戏语言是否为中文。首次调用时探测并缓存。</summary>
    public static bool IsChinese
    {
        get
        {
            if (_isZh == null) _isZh = DetectIsChinese();
            return _isZh.Value;
        }
    }

    /// <summary>按游戏语言返回 zh 或 en。</summary>
    public static string Tr(string zh, string en) => IsChinese ? zh : en;

    private static bool DetectIsChinese()
    {
        try
        {
            // 复用 WebConfig 计算 uiLang 的同一判定（执行时替换为查到的真实代码）。
            string lang = BannerlordConfig.Language ?? "";
            return lang.StartsWith("简") || lang.ToLowerInvariant().Contains("zh")
                   || lang.Contains("Chinese");
        }
        catch { return false; }
    }
}
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 成功。若 `BannerlordConfig.Language` 不存在或签名不符，按 WebConfig 里查到的真实判定改写 `DetectIsChinese()`。

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/ControlPanelLoc.cs
git commit -m "feat(ui): add ControlPanelLoc bilingual helper"
```

---

## Task 3: ControlPanelData — 进程内数据适配

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/ControlPanelData.cs`
- Modify: `SovereignTowns/src/WebConfig/TroopDumper.cs`（`TroopEntry` 改 public + 加公开枚举方法）
- Reference（执行前必读，确认真实签名）: `src/Configuration/ConfigurationManager.cs`、`src/Configuration/GlobalConfig.cs`、`src/Economy/ModExpenseLedger.cs`

- [ ] **Step 1: 改 TroopDumper.cs —— 暴露进程内兵种枚举**

把私有 `sealed class TroopEntry` 改为 `public sealed class TroopEntry`（类定义那一行）。在 `Dump()` 之后加一个公开方法：

```csharp
/// <summary>进程内取可招募兵种列表（游戏内控制面板用，不落盘）。</summary>
public static System.Collections.Generic.List<TroopEntry> Collect() => CollectTroops();
```

- [ ] **Step 2: 读 ConfigurationManager / GlobalConfig / ModExpenseLedger 确认签名**

确认并记下：`ConfigurationManager.Current` 返回类型、`ReplaceAndSave` 与 `TryReload` 的确切签名、`GlobalConfig` 是否可被 Newtonsoft 往返序列化（应可——CLAUDE.md 不变量 8）、`ModExpenseLedger` 取财务报告的方法名与返回类型（`/api/finance` 的实现处即调用点，见 `WebConfigEndpoints.cs`）。

- [ ] **Step 3: 写 ControlPanelData.cs**

下面的 `ReplaceAndSave` / `TryReload` / 财务方法名按 Step 2 查到的真实签名校正。

```csharp
using System;
using Newtonsoft.Json;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.WebConfig;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 游戏内控制面板的数据适配层。与配置系统同进程，直接调 ConfigurationManager /
/// TroopDumper / ModExpenseLedger，不经 WebConfig 的 HTTP+JSON。
/// </summary>
internal static class ControlPanelData
{
    /// <summary>深拷贝当前配置成一份工作副本（面板在副本上改，保存时整体写回）。</summary>
    public static GlobalConfig CloneCurrentConfig()
    {
        var current = ConfigurationManager.Current;
        // Newtonsoft 往返实现深拷贝 —— GlobalConfig 全程可序列化（不变量 8）。
        string json = JsonConvert.SerializeObject(current);
        return JsonConvert.DeserializeObject<GlobalConfig>(json)
               ?? throw new InvalidOperationException("GlobalConfig 深拷贝失败");
    }

    /// <summary>把工作副本写回配置系统并落盘。返回是否成功 + 失败原因。</summary>
    public static bool Save(GlobalConfig working, out string reason)
    {
        try
        {
            // 真实签名以 ConfigurationManager.cs 为准（Step 2 已确认）。
            bool ok = ConfigurationManager.ReplaceAndSave(working, out reason, out _);
            return ok;
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelData.Save failed", ex);
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>从磁盘重读配置，成功后返回新的工作副本。</summary>
    public static GlobalConfig Reload(out string reason)
    {
        try
        {
            ConfigurationManager.TryReload(out reason, out _);
        }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelData.Reload failed", ex);
            reason = ex.Message;
        }
        return CloneCurrentConfig();
    }

    /// <summary>取可招募兵种列表（进程内枚举，含 RBM 等 mod 兵种）。</summary>
    public static System.Collections.Generic.List<TroopDumper.TroopEntry> CollectTroops()
    {
        try { return TroopDumper.Collect(); }
        catch (Exception ex)
        {
            Logger.Error("ControlPanelData.CollectTroops failed", ex);
            return new System.Collections.Generic.List<TroopDumper.TroopEntry>();
        }
    }

    // 财务报告：方法名 / 返回类型按 ModExpenseLedger.cs 真实 API 补全（Step 2）。
    // public static FinanceReport BuildFinanceReport() { ... }
}
```

- [ ] **Step 4: 补全财务方法**

按 Step 2 查到的 `ModExpenseLedger` 真实 API，在 `ControlPanelData` 末尾加 `BuildFinanceReport()`，try/catch 包裹，失败返回空报告或 null。

- [ ] **Step 5: 编译验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 成功。

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/ControlPanelData.cs SovereignTowns/src/WebConfig/TroopDumper.cs
git commit -m "feat(ui): add ControlPanelData in-process data adapter"
```

---

## Task 4: ControlPanelScreen + 最小 ControlPanelVM + 最小根 prefab

目标：能 `PushScreen` 弹出一个暂停游戏的空面板、能关闭。架构验证点。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/ControlPanelScreen.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs`
- Create: `SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml`
- Reference: `_research/ImprovedGarrisons/ImprovedGarrisons.ConfigOptionsMenu/ConfigMenuGauntletScreen.cs`

- [ ] **Step 1: 写最小 ControlPanelVM.cs**

```csharp
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

public sealed class ControlPanelVM : ViewModel
{
    private bool _isClosing;

    /// <summary>屏幕轮询此标记决定是否 PopScreen。</summary>
    public bool IsClosing => _isClosing;

    public ControlPanelVM()
    {
    }

    /// <summary>关闭按钮 / ESC 调用。</summary>
    public void ExecuteClose()
    {
        _isClosing = true;
    }
}
```

- [ ] **Step 2: 写 ControlPanelScreen.cs**

照搬 `ConfigMenuGauntletScreen` 范式。**关键**：本面板从地图态打开、时间在走，必须显式暂停 —— `OnInitialize` 里 `Game.Current.GameStateManager.RegisterActiveStateDisableRequest(this)`，`OnFinalize` 里 `UnregisterActiveStateDisableRequest(this)`。`ScreenBase` 不直接是 disable-request 的合法 target 时，改在 VM 里持有一个 request 对象——执行时以 `ConfigMenuVM.PauseGame/UnpauseGame`（`_research` 内）的真实写法为准。

```csharp
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>
/// 游戏内控制面板的屏幕容器。由大地图按钮 PushScreen 弹出；PopScreen 关闭。
/// 打开期间暂停游戏（地图态时间在走，必须显式暂停）。
/// </summary>
internal sealed class ControlPanelScreen : ScreenBase
{
    private GauntletLayer _layer;
    private ControlPanelVM _vm;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        try
        {
            _vm = new ControlPanelVM();
            _layer = new GauntletLayer("GauntletLayer", 4000, false);
            _layer.LoadMovie("SovereignTownsControlPanel", _vm);
            _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            _layer.IsFocusLayer = true;
            AddLayer(_layer);
            ScreenManager.TrySetFocus(_layer);
            // 暂停游戏：写法以 _research 的 ConfigMenuVM.PauseGame 为准。
            Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(this);
        }
        catch (System.Exception ex) { Logger.Error("ControlPanelScreen.OnInitialize failed", ex); }
    }

    protected override void OnFrameTick(float dt)
    {
        base.OnFrameTick(dt);
        try
        {
            if (_vm != null && (_vm.IsClosing || _layer.Input.IsHotKeyReleased("Exit")))
            {
                Close();
            }
        }
        catch (System.Exception ex) { Logger.Error("ControlPanelScreen.OnFrameTick failed", ex); }
    }

    private void Close()
    {
        try { Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(this); }
        catch (System.Exception ex) { Logger.Error("ControlPanelScreen unpause failed", ex); }
        ScreenManager.PopScreen();
    }

    protected override void OnFinalize()
    {
        base.OnFinalize();
        try
        {
            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
        }
        catch (System.Exception ex) { Logger.Error("ControlPanelScreen.OnFinalize failed", ex); }
    }
}
```

- [ ] **Step 3: 写最小根 prefab SovereignTownsControlPanel.xml**

先读 `Modules\Native\GUI\Prefabs\` 下一个 prefab 学语法。最小版本：一个 `Window` / `Widget` 根，铺一块深色背景（`Sprite="General\white_64"` 之类 + 暗色 `Color`），中央放一个 `RichTextWidget` 显示「SOVEREIGN TOWNS」，右上角一个 `ButtonWidget`（`Command.Click="ExecuteClose"`）。结构：

```
Prefab > Window
  Widget  (根，铺满，深色背景)
    RichTextWidget  Text="SOVEREIGN TOWNS"   (居中)
    ButtonWidget    Command.Click="ExecuteClose"  (右上角，文字「✕」)
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 成功。DeployToGame 把 dll + prefab 拷进游戏。

- [ ] **Step 5: 标记本任务的游戏内验证为「待 Task 5 后执行」**

此时面板还没有入口，无法在游戏里打开。游戏内验证并入 Task 5。

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/ControlPanelScreen.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): add ControlPanelScreen skeleton with pause + close"
```

---

## Task 5: 地图按钮 + bootstrap（架构打通）

目标：大地图左侧出现按钮 → 点击弹出（空）面板 → 关闭回到地图、游戏恢复。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/MapButtonVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/ControlPanelMapButtonView.cs`
- Create: `SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsMapButton.xml`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`
- Reference: `_research/ImprovedGarrisons/ImprovedGarrisons.ImprovedGarrisonsUI/ImprovedGarrisonsUIGauntlet.cs`、`UIManager.cs`

- [ ] **Step 1: 写 MapButtonVM.cs**

```csharp
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

public sealed class MapButtonVM : ViewModel
{
    private string _label;

    [DataSourceProperty]
    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(nameof(Label)); } }
    }

    public MapButtonVM()
    {
        _label = ControlPanelLoc.Tr("控制面板", "Control Panel");
    }

    /// <summary>按钮点击 → 弹出控制面板。</summary>
    public void ExecuteOpen()
    {
        try
        {
            ScreenManager.PushScreen(new ControlPanelScreen());
        }
        catch (System.Exception ex) { Logger.Error("MapButtonVM.ExecuteOpen failed", ex); }
    }
}
```

- [ ] **Step 2: 写 ControlPanelMapButtonView.cs**

照搬 `ImprovedGarrisonsUIGauntlet`：`MapView` 子类，`CreateLayout` 里加一个**不拦截地图输入**（`SetInputRestrictions(false, …)`）的 `GauntletLayer` 到 `MapScreen.Instance`。

```csharp
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>大地图左侧常驻「打开控制面板」按钮。模仿 IG 的 ImprovedGarrisonsUIGauntlet。</summary>
internal sealed class ControlPanelMapButtonView : MapView
{
    private GauntletLayer _layer;
    private MapButtonVM _vm;

    public ControlPanelMapButtonView()
    {
        CreateLayout();
    }

    protected override void CreateLayout()
    {
        base.CreateLayout();
        try
        {
            _vm = new MapButtonVM();
            _layer = new GauntletLayer("GauntletLayer", 200, false);
            _layer.LoadMovie("SovereignTownsMapButton", _vm);
            // false = 不拦截地图拖拽 / 缩放，只有按钮自身吃点击。
            _layer.InputRestrictions.SetInputRestrictions(false, InputUsageMask.All);
            ((TaleWorlds.ScreenSystem.ScreenBase)MapScreen.Instance).AddLayer(_layer);
        }
        catch (System.Exception ex) { Logger.Error("ControlPanelMapButtonView.CreateLayout failed", ex); }
    }
}
```

- [ ] **Step 3: 写 SovereignTownsMapButton.xml prefab**

大地图**左侧边缘**一个小按钮。结构：

```
Prefab > Window
  Widget  (定位到左侧边缘，HorizontalAlignment=Left VerticalAlignment=Center，小尺寸)
    ButtonWidget  Command.Click="ExecuteOpen"   (金/黑主题描金)
      RichTextWidget  Text="@Label"
```

- [ ] **Step 4: 在 SovereignTownsCampaignBehavior 里加 bootstrap**

先读 `SovereignTownsCampaignBehavior.cs` 找到事件注册处（`OnSessionLaunched` 或构造器里 `CampaignEvents.*.AddNonSerializedListener`）。`MapScreen.Instance` 在 session 早期可能为 null，照搬 IG `UIManager` 思路：订阅 `CampaignEvents.TickEvent`（每帧级触发），回调里幂等创建一次。

加一个私有字段与方法（方法体 try/catch —— 不变量 5）：

```csharp
private SovereignTowns.Ui.ControlPanel.ControlPanelMapButtonView _mapButtonView;

// 在 OnSessionLaunched 里订阅（若已有 TickEvent 监听则并入）：
//   CampaignEvents.TickEvent.AddNonSerializedListener(this, OnCampaignTick);

private void OnCampaignTick(float dt)
{
    try
    {
        if (_mapButtonView == null
            && SandBox.View.Map.MapScreen.Instance != null)
        {
            _mapButtonView = new SovereignTowns.Ui.ControlPanel.ControlPanelMapButtonView();
            Logger.Info("ControlPanel: map button view created");
        }
    }
    catch (System.Exception ex)
    {
        SovereignTowns.Logging.Logger.Error("OnCampaignTick map-button bootstrap failed", ex);
    }
}
```

注意：`TickEvent` 回调签名以 `CampaignEvents.TickEvent` 真实委托为准（可能是 `Action<float>`）；若签名不符按真实签名改。

- [ ] **Step 5: 编译验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 成功。

- [ ] **Step 6: 游戏内验证（架构打通的关键核对）**

启动游戏 → 读档进大地图 → 左侧应出现「控制面板」按钮 → 点击 → 弹出深色空面板、显示「SOVEREIGN TOWNS」、游戏暂停（时间停） → 点 ✕ 或按 ESC → 面板关闭、回到地图、时间恢复。看 `ModLogs/SovereignTowns/` 无 Error。若任一步失败，**先修通再继续后续任务**。

- [ ] **Step 7: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/MapButtonVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelMapButtonView.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsMapButton.xml" SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs
git commit -m "feat(ui): add campaign-map button that opens the control panel"
```

---

## Task 6: 表头（logo / 状态 pill / 重读 / 保存 / 横幅）

对应 `index.html` 的 `<header>` 与 warning/success 横幅。

**Files:**
- Modify: `SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs`
- Modify: `SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml`

- [ ] **Step 1: 扩展 ControlPanelVM —— 工作副本 + 表头状态 + 保存/重读**

在 `ControlPanelVM` 里加：持有工作副本 `GlobalConfig Config`；`bool IsDirty`、`bool IsSaving`、`string CapitalName`、`string Warning`、`string Success`（均 `[DataSourceProperty]`）；`MarkDirty()`；命令 `ExecuteSave()` / `ExecuteReload()`。

```csharp
// 字段
private SovereignTowns.Configuration.GlobalConfig _config;
private bool _isDirty, _isSaving;
private string _capitalName = "", _warning = "", _success = "";

public SovereignTowns.Configuration.GlobalConfig Config => _config;

[DataSourceProperty] public bool IsDirty   { get => _isDirty;  set { if (_isDirty != value)  { _isDirty = value;  OnPropertyChanged(nameof(IsDirty));  OnPropertyChanged(nameof(SaveLabel)); } } }
[DataSourceProperty] public bool IsSaving  { get => _isSaving; set { if (_isSaving != value) { _isSaving = value; OnPropertyChanged(nameof(IsSaving)); OnPropertyChanged(nameof(SaveLabel)); } } }
[DataSourceProperty] public string CapitalName { get => _capitalName; set { if (_capitalName != value) { _capitalName = value; OnPropertyChanged(nameof(CapitalName)); } } }
[DataSourceProperty] public string Warning { get => _warning; set { if (_warning != value) { _warning = value; OnPropertyChanged(nameof(Warning)); OnPropertyChanged(nameof(HasWarning)); } } }
[DataSourceProperty] public string Success { get => _success; set { if (_success != value) { _success = value; OnPropertyChanged(nameof(Success)); OnPropertyChanged(nameof(HasSuccess)); } } }
[DataSourceProperty] public bool HasWarning => !string.IsNullOrEmpty(_warning);
[DataSourceProperty] public bool HasSuccess => !string.IsNullOrEmpty(_success);
[DataSourceProperty] public string SaveLabel =>
    _isSaving ? ControlPanelLoc.Tr("保存中…", "Saving…")
    : _isDirty ? ControlPanelLoc.Tr("● 保存改动", "● Save changes")
    : ControlPanelLoc.Tr("已保存", "Saved");
[DataSourceProperty] public string DirtyLabel =>
    _isDirty ? ControlPanelLoc.Tr("● 有未保存改动", "● Unsaved changes")
             : ControlPanelLoc.Tr("✓ 已保存", "✓ Saved");

public void MarkDirty()
{
    IsDirty = true;
    OnPropertyChanged(nameof(DirtyLabel));
}
```

构造器里：`_config = ControlPanelData.CloneCurrentConfig();`，并填 `CapitalName`（取玩家首府名——读 `src/Capital/CapitalManager.cs` 找取首府的 API；取不到回退 `Tr("无","none")`）。

`ExecuteSave`：

```csharp
public void ExecuteSave()
{
    if (_config == null || _isSaving) return;
    IsSaving = true;
    Warning = "";
    bool ok = ControlPanelData.Save(_config, out string reason);
    IsSaving = false;
    if (ok)
    {
        IsDirty = false;
        Success = ControlPanelLoc.Tr("已保存到游戏。", "Saved to the game.");
        AddLog(ControlPanelLoc.Tr("配置已保存", "Configuration saved"), LogKind.Ok);
    }
    else
    {
        Warning = ControlPanelLoc.Tr("保存失败：", "Save failed: ") + reason;
        AddLog(Warning, LogKind.Err);
    }
}
```

`ExecuteReload`：若 `IsDirty` 先无法弹浏览器 confirm —— 改用 `InformationManager.ShowInquiry` 二次确认；确认后 `_config = ControlPanelData.Reload(out _)`，`IsDirty=false`，`RefreshAllTabs()`（Task 7 引入），`AddLog`。`ExecuteDismissWarning` / `ExecuteDismissSuccess` 清空对应字符串。`AddLog` / `LogKind` 在 Task 7 定义——本任务可先留 `AddLog` 为空方法占位，Task 7 补全（**注意**：执行 Task 7 时必须回填，否则保存日志不显示）。

- [ ] **Step 2: prefab 加表头**

把 Task 4 的最小根 prefab 扩成：根 `Widget` → 纵向 `ListPanel`：
- **表头 Widget**（横向 `ListPanel`）：左 = ⚜ + `RichTextWidget`「SOVEREIGN TOWNS」+ 副标题；右 = pill1 `RichTextWidget Text="@CapitalName"` + pill2 `RichTextWidget Text="@DirtyLabel"` + `ButtonWidget`「↻ 重读」`Command.Click="ExecuteReload"` + `ButtonWidget Text="@SaveLabel" Command.Click="ExecuteSave"`。表头底部一条金色细线 `Widget`。
- **warning 横幅 Widget**：`IsVisible="@HasWarning"`，红边，`RichTextWidget Text="@Warning"` + ✕ `Command.Click="ExecuteDismissWarning"`。
- **success 横幅 Widget**：`IsVisible="@HasSuccess"`，绿边，`RichTextWidget Text="@Success"` + ✕ `Command.Click="ExecuteDismissSuccess"`。
- **body 占位 Widget**（Task 7 填标签栏 + 内容区）。

- [ ] **Step 3: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：打开面板 → 表头显示标题、首府名、「✓ 已保存」、重读/保存按钮 → 点保存 → 不报错（此时无改动，保存空操作或成功）。

- [ ] **Step 4: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel header with save/reload + dirty state"
```

---

## Task 7: 左侧标签栏 + 活动日志

对应 `index.html` 的 `<aside>`。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/LogEntryVM.cs`
- Modify: `SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs`
- Modify: `SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml`

- [ ] **Step 1: 写 LogEntryVM.cs**

```csharp
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

public enum LogKind { Info, Ok, Err }

public sealed class LogEntryVM : ViewModel
{
    [DataSourceProperty] public string Timestamp { get; }
    [DataSourceProperty] public string Message { get; }
    /// <summary>prefab 按此着色：info=羊皮纸 / ok=绿 / err=红。</summary>
    [DataSourceProperty] public string ColorHint { get; }

    public LogEntryVM(string message, LogKind kind)
    {
        Timestamp = System.DateTime.Now.ToString("HH:mm:ss");
        Message = message;
        ColorHint = kind == LogKind.Err ? "err" : kind == LogKind.Ok ? "ok" : "info";
    }
}
```

- [ ] **Step 2: ControlPanelVM 加标签页选择 + 活动日志**

```csharp
// 字段
private int _activeTab;
private readonly MBBindingList<LogEntryVM> _logEntries = new MBBindingList<LogEntryVM>();

[DataSourceProperty] public MBBindingList<LogEntryVM> LogEntries => _logEntries;
[DataSourceProperty] public int ActiveTab
{
    get => _activeTab;
    set { if (_activeTab != value) { _activeTab = value; OnPropertyChanged(nameof(ActiveTab)); RefreshTabVisibility(); } }
}
// 6 个标签页可见性绑定（prefab 用 IsVisible="@IsTab0Active" …）
[DataSourceProperty] public bool IsTab0Active => _activeTab == 0;
[DataSourceProperty] public bool IsTab1Active => _activeTab == 1;
[DataSourceProperty] public bool IsTab2Active => _activeTab == 2;
[DataSourceProperty] public bool IsTab3Active => _activeTab == 3;
[DataSourceProperty] public bool IsTab4Active => _activeTab == 4;
[DataSourceProperty] public bool IsTab5Active => _activeTab == 5;

private void RefreshTabVisibility()
{
    for (int i = 0; i < 6; i++) OnPropertyChanged($"IsTab{i}Active");
}

public void ExecuteSelectTab0() => ActiveTab = 0;
public void ExecuteSelectTab1() => ActiveTab = 1;
public void ExecuteSelectTab2() => ActiveTab = 2;
public void ExecuteSelectTab3() => ActiveTab = 3;
public void ExecuteSelectTab4() => ActiveTab = 4;
public void ExecuteSelectTab5() => ActiveTab = 5;

public void AddLog(string message, LogKind kind = LogKind.Info)
{
    _logEntries.Insert(0, new LogEntryVM(message, kind));
    while (_logEntries.Count > 20) _logEntries.RemoveAt(_logEntries.Count - 1);
}
```

回填 Task 6 Step 1 留空的 `AddLog`（删占位，用上面这个真实实现）。构造器末尾 `AddLog(ControlPanelLoc.Tr("配置已读取","Configuration loaded"), LogKind.Ok);`。

- [ ] **Step 3: prefab 填 body 的左栏**

body Widget 改成横向 `ListPanel`：
- **左栏 Widget**（窄）：
  - 标签栏 Widget：6 个 `ButtonWidget`，文字 `01`–`06` + 标签名（用 `ControlPanelLoc.Tr` 的结果——可在 VM 暴露 `Tab0Label`…`Tab5Label` 只读属性），`Command.Click="ExecuteSelectTab0"`…`5`，激活态描金（绑 `IsTabNActive`）。
  - 活动日志 Widget：标题「活动日志」+ `ScrollablePanel` 内 `ListPanel` `DataSource="@LogEntries"` `ItemTemplate` 行 = `RichTextWidget Text="@Timestamp"` + `RichTextWidget Text="@Message"`。
- **右栏内容区 Widget**（宽）：6 个 tab 容器 Widget，各 `IsVisible="@IsTab{N}Active"`，本任务内先放标题占位，后续任务逐个填。

VM 暴露标签名：

```csharp
[DataSourceProperty] public string Tab0Label => ControlPanelLoc.Tr("功能开关", "Features");
[DataSourceProperty] public string Tab1Label => ControlPanelLoc.Tr("策略参数", "Strategy");
[DataSourceProperty] public string Tab2Label => ControlPanelLoc.Tr("兵种编制", "Composition");
[DataSourceProperty] public string Tab3Label => ControlPanelLoc.Tr("兵员模板", "Templates");
[DataSourceProperty] public string Tab4Label => ControlPanelLoc.Tr("非首府驻军", "Branches");
[DataSourceProperty] public string Tab5Label => ControlPanelLoc.Tr("财务", "Finance");
```

- [ ] **Step 4: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：打开面板 → 左侧 6 个标签按钮 → 点击切换，右侧内容区切换、激活态高亮 → 活动日志显示「配置已读取」。

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/Items/LogEntryVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel tab rail + activity log"
```

---

## Task 8: Tab 1 功能开关

对应 `index.html` Tab 1（`toggleSpecs`，8 个开关，root 全为 `EnabledFeatures`）。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/ToggleRowVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Tabs/FeaturesTabVM.cs`
- Create: `SovereignTowns/SovereignTowns/GUI/Prefabs/STCPToggleRow.xml`
- Modify: `SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs`、`SovereignTownsControlPanel.xml`

- [ ] **Step 1: 写 ToggleRowVM.cs**

通用开关行。直接读写工作副本的字段（用反射按 `EnabledFeatures` 的属性名读写，或传 getter/setter 委托——下面用委托，类型安全）。

```csharp
using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

public sealed class ToggleRowVM : ViewModel
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;
    private readonly Action _onChanged;

    [DataSourceProperty] public string Label { get; }
    [DataSourceProperty] public string Hint { get; }
    [DataSourceProperty] public bool IsDanger { get; }

    [DataSourceProperty]
    public bool IsChecked
    {
        get => _get();
        set
        {
            if (_get() == value) return;
            _set(value);
            OnPropertyChanged(nameof(IsChecked));
            _onChanged?.Invoke();
        }
    }

    public ToggleRowVM(string label, string hint, bool isDanger,
                       Func<bool> get, Action<bool> set, Action onChanged)
    {
        Label = label; Hint = hint; IsDanger = isDanger;
        _get = get; _set = set; _onChanged = onChanged;
    }
}
```

- [ ] **Step 2: 写 FeaturesTabVM.cs**

8 个开关的 label/hint **从 `index.html` 的 `toggleSpecs`（约 1063-1077 行）逐条抄**（zh + en 都在那）。字段名见下，全部 `config.EnabledFeatures.*`。

```csharp
using TaleWorlds.Library;
using SovereignTowns.Configuration;

namespace SovereignTowns.Ui.ControlPanel;

public sealed class FeaturesTabVM : ViewModel
{
    [DataSourceProperty] public string Title { get; }
    [DataSourceProperty] public string Intro1 { get; }
    [DataSourceProperty] public string Intro2 { get; }
    [DataSourceProperty] public MBBindingList<ToggleRowVM> Toggles { get; } = new MBBindingList<ToggleRowVM>();

    public FeaturesTabVM(GlobalConfig config, System.Action markDirty)
    {
        Title  = ControlPanelLoc.Tr("功能开关", "Feature switches");
        Intro1 = ControlPanelLoc.Tr(/* index.html:456 的 zh */ "", /* en */ "");
        Intro2 = ControlPanelLoc.Tr(/* index.html:457 的 zh */ "", /* en */ "");
        var ef = config.EnabledFeatures;
        void Add(string zh, string en, string zhHint, string enHint,
                 System.Func<bool> g, System.Action<bool> s)
            => Toggles.Add(new ToggleRowVM(ControlPanelLoc.Tr(zh, en),
                   ControlPanelLoc.Tr(zhHint, enHint), false, g, s, markDirty));

        Add("自动招募", "Auto-recruitment", /*hint*/ "", "", () => ef.AutoRecruitment, v => ef.AutoRecruitment = v);
        Add("自动巡逻", "Auto-patrol", "", "", () => ef.AutoPatrol, v => ef.AutoPatrol = v);
        Add("兵力调拨", "Troop transfers", "", "", () => ef.TroopTransfers, v => ef.TroopTransfers = v);
        Add("主动出击", "Sally forth", "", "", () => ef.SallyForth, v => ef.SallyForth = v);
        Add("抑制 vanilla 自动招募", "Suppress vanilla auto-recruitment", "", "", () => ef.SuppressVanillaGarrisonRecruitment, v => ef.SuppressVanillaGarrisonRecruitment = v);
        Add("金币不足时暂停支出", "Pause spending when broke", "", "", () => ef.PauseSpendingWhenBroke, v => ef.PauseSpendingWhenBroke = v);
        Add("每日活动汇总弹窗", "Daily activity summary popup", "", "", () => ef.ShowDailySummary, v => ef.ShowDailySummary = v);
        Add("详细诊断日志", "Verbose diagnostic logging", "", "", () => ef.VerboseLogging, v => ef.VerboseLogging = v);
    }
}
```

> 所有 hint 的空字符串占位 **必须**在执行时从 `index.html:1066-1075` 的对应 `hint` 抄全（zh + en）。`EnabledFeatures` 的真实属性名以 `src/Configuration/` 下该类为准（执行前读一遍核对）。

- [ ] **Step 3: ControlPanelVM 持有 FeaturesTabVM**

`ControlPanelVM` 加 `[DataSourceProperty] public FeaturesTabVM FeaturesTab { get; }`，构造器（在 `_config` 克隆后）`FeaturesTab = new FeaturesTabVM(_config, MarkDirty);`。

- [ ] **Step 4: 写 STCPToggleRow.xml + 填 Tab1 容器**

`STCPToggleRow.xml`：一行 = 勾选框（vanilla 的 checkbox/toggle widget，绑 `IsChecked`）+ `RichTextWidget Text="@Label"` + `RichTextWidget Text="@Hint"`。紧凑行高（见设计文档 §2.5）。
根 prefab Tab0 容器：标题 `RichTextWidget Text="@FeaturesTab.Title"` + 两段说明 + 菱形分隔线 Widget + `ListPanel DataSource="@FeaturesTab.Toggles" ItemTemplate="STCPToggleRow"`。

- [ ] **Step 5: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：打开面板 Tab1 → 8 个开关行、文字正确 → 勾选某项 → 表头变「● 有未保存改动」→ 保存 → 退面板 → 查 `global.json` 对应 `EnabledFeatures` 字段已变。

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/Items/ToggleRowVM.cs SovereignTowns/src/Ui/ControlPanel/Tabs/FeaturesTabVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/STCPToggleRow.xml" "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel Tab 1 — feature switches"
```

---

## Task 9: ControlPanelSpecs — spec 元数据表

把 WebUI 的 `budgetSpecs` / `resourceSpecs` / `thresholdSpecs` / `settingsGroups` 移植成 C# 静态表，供 Task 10 的策略参数页用。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs`

- [ ] **Step 1: 定义 SpecEntry 与分组结构**

```csharp
using System.Collections.Generic;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>一条数值 / bool 参数的元数据。对应 WebUI 的 *Specs 条目。</summary>
public sealed class SpecEntry
{
    public string Root;        // "GlobalDefaults" / "Thresholds" / "ClanPatrol" / "ClanRecruiter" / "" (=GlobalConfig 根)
    public string Key;         // 属性名
    public string LabelZh, LabelEn, HintZh, HintEn;
    public bool IsBool;        // true=开关行
    public double Min, Max, Step;
    public bool Discrete;      // 整数
    public double? Def;        // 出厂默认值（用于「恢复默认」）；null=无
    public bool Advanced;      // 开发者级旋钮，默认折叠
}

public sealed class SpecGroup
{
    public string Key, LabelZh, LabelEn, HintZh, HintEn;
    public bool Advanced;      // 整组高级（如 mcmf）
    public List<SpecEntry> Specs = new List<SpecEntry>();
}
```

- [ ] **Step 2: 填全部 spec 与分组**

`ControlPanelSpecs` 静态类提供 `public static IReadOnlyList<SpecGroup> AllGroups { get; }`。**所有条目从 `index.html` 逐条移植**：
- `budgetSpecs` → index.html:1079-1088（root=`GlobalDefaults`）
- `resourceSpecs` → index.html:1111-1117
- `thresholdSpecs` → index.html:1122-1178（root 见每条 `root` 字段）
- 6 个分组与成员顺序、`adv` 标记、`boolSpec`（如 `ClanPatrol.AvoidRaidedVillages`）→ index.html:1196-1292（`settingsGroups`）

逐条对照写，**不省略**。一条完整示例（其余照此格式）：

```csharp
new SpecEntry {
    Root = "GlobalDefaults", Key = "TargetTotalCount",
    LabelZh = "目标驻军总数", LabelEn = "Target garrison size",
    HintZh = "驻军应维持的兵员数", HintEn = "The number of troops a garrison should maintain.",
    IsBool = false, Min = 50, Max = 500, Step = 1, Discrete = true, Def = 150, Advanced = false,
},
```

bool spec 示例（`AvoidRaidedVillages`）：`IsBool = true, Def = 1`（1=默认开）。

- [ ] **Step 3: 编译验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 成功。

- [ ] **Step 4: 核对完整性**

逐一比对 `index.html` 的 `settingsGroups` 6 个分组成员，确认 `ControlPanelSpecs.AllGroups` 一条不漏、`root`/`adv` 一致。`ClanPatrol` 与 `ClanRecruiter` 下有同名 key（`EtaBufferHours` 等），靠 `Root` 区分——确认没串。

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs
git commit -m "feat(ui): port strategy-parameter spec metadata table"
```

---

## Task 10: Tab 2 策略参数

对应 `index.html` Tab 2：分组筛选 chip + 高级参数开关 + 当前组的 spec 行（滑块 / 开关）+ 本组恢复默认。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/SliderRowVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/SettingsGroupVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Tabs/StrategyTabVM.cs`
- Create: `SovereignTowns/SovereignTowns/GUI/Prefabs/STCPSliderRow.xml`、`STCPChip.xml`
- Modify: `ControlPanelVM.cs`、`SovereignTownsControlPanel.xml`

- [ ] **Step 1: 写 SliderRowVM.cs**

数值行：滑块 + 数值框联动 + 「↺ 恢复默认」。读写工作副本用 `Func/Action<double>` 委托（调用方按 `SpecEntry.Root`+`Key` 反射生成）。

```csharp
using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

public sealed class SliderRowVM : ViewModel
{
    private readonly SpecEntry _spec;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly Action _markDirty;

    [DataSourceProperty] public string Label { get; }
    [DataSourceProperty] public string Hint { get; }
    [DataSourceProperty] public bool IsAdvanced { get; }
    [DataSourceProperty] public float Min { get; }
    [DataSourceProperty] public float Max { get; }

    [DataSourceProperty]
    public float Value
    {
        get => (float)_get();
        set
        {
            double v = Clamp(value);
            if (Math.Abs(_get() - v) < 1e-9) return;
            _set(v);
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(ShowReset));
            _markDirty?.Invoke();
        }
    }

    /// <summary>数值框文本（离散显示整数）。</summary>
    [DataSourceProperty]
    public string ValueText
    {
        get => _spec.Discrete ? ((int)Math.Round(_get())).ToString()
                              : _get().ToString("0.00");
        set { if (double.TryParse(value, out double v)) Value = (float)v; }
    }

    [DataSourceProperty] public bool ShowReset =>
        _spec.Def.HasValue && Math.Abs(_get() - _spec.Def.Value) > 1e-6;
    [DataSourceProperty] public string ResetLabel =>
        ControlPanelLoc.Tr("↺ 恢复默认 ", "↺ Reset ") +
        (_spec.Def.HasValue ? (_spec.Discrete ? ((int)_spec.Def.Value).ToString()
                                              : _spec.Def.Value.ToString("0.##")) : "");

    public SliderRowVM(SpecEntry spec, Func<double> get, Action<double> set, Action markDirty)
    {
        _spec = spec; _get = get; _set = set; _markDirty = markDirty;
        Label = ControlPanelLoc.Tr(spec.LabelZh, spec.LabelEn);
        Hint = ControlPanelLoc.Tr(spec.HintZh, spec.HintEn);
        IsAdvanced = spec.Advanced;
        Min = (float)spec.Min; Max = (float)spec.Max;
    }

    public void ExecuteReset()
    {
        if (_spec.Def.HasValue) Value = (float)_spec.Def.Value;
    }

    private double Clamp(double v)
    {
        if (v < _spec.Min) v = _spec.Min;
        if (v > _spec.Max) v = _spec.Max;
        if (_spec.Discrete) v = Math.Round(v);
        return v;
    }
}
```

- [ ] **Step 2: 写 SettingsGroupVM.cs**

一个分组：持有该组的 `MBBindingList<SliderRowVM>` + `MBBindingList<ToggleRowVM>`（或合并成一个混合列表——prefab 两个 `ListPanel` 分别绑更简单）。提供 `Label` / `Hint` / `Key` / `ExecuteResetGroup()`（把组内所有 spec 置回 `Def`）。构造时按 `showAdvanced` 过滤 `Advanced` 条目。

- [ ] **Step 3: 写 StrategyTabVM.cs**

职责：持有 `MBBindingList<SettingsGroupVM>`（可见分组）、`ActiveGroupKey`、`ShowAdvanced`；切高级时重建分组列表并隐藏空组（移植 `visibleSettingsGroups`：`adv` 组在关高级时整组隐藏）。委托生成：按 `SpecEntry.Root`+`Key` 反射读写 `GlobalConfig`——

```csharp
// 反射读写工作副本某字段为 double：
static object RootObj(GlobalConfig cfg, string root)
    => string.IsNullOrEmpty(root) ? cfg : cfg.GetType().GetProperty(root).GetValue(cfg);
static double GetD(GlobalConfig cfg, SpecEntry s)
{
    var o = RootObj(cfg, s.Root);
    var p = o.GetType().GetProperty(s.Key);
    return System.Convert.ToDouble(p.GetValue(o));
}
static void SetD(GlobalConfig cfg, SpecEntry s, double v)
{
    var o = RootObj(cfg, s.Root);
    var p = o.GetType().GetProperty(s.Key);
    object boxed = p.PropertyType == typeof(int) ? (object)(int)System.Math.Round(v)
                 : p.PropertyType == typeof(float) ? (object)(float)v
                 : (object)v;
    p.SetValue(o, boxed);
}
```

> 反射在面板里是可接受的——只在打开/拖动时跑、非热路径。若执行者偏好类型安全，可改成每个 spec 显式委托，但工作量大得多；反射是此处的合理取舍。

- [ ] **Step 4: 写 STCPSliderRow.xml + STCPChip.xml + 填 Tab2 容器**

- `STCPChip.xml`：一个小 `ButtonWidget`（chip 风格），`Text` + `Command.Click`，激活态描金。供分组筛选、Tab3 文化/tier chip 复用。
- `STCPSliderRow.xml`：`RichTextWidget Text="@Label"` + `Text="@Hint"` + 「↺恢复默认」`ButtonWidget`（`IsVisible="@ShowReset"` `Text="@ResetLabel"` `Command.Click="ExecuteReset"`）+ vanilla 滑块（绑 `Value`/`Min`/`Max`）+ `EditableTextWidget`（绑 `ValueText`）。
- Tab2 容器：标题 + 2 段说明 + 菱形线 + 分组 chip 行（`ListPanel` of `STCPChip`）+「显示高级参数」勾选 + 当前组标题/说明 +「本组恢复默认」按钮 + 当前组的滑块/开关 `ListPanel`。

- [ ] **Step 5: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：Tab2 → 分组 chip 切换、参数行显示 → 拖滑块/改数值框两者联动、表头变 dirty →「显示高级参数」勾上出现 MCMF 组与 adv 行 →「↺恢复默认」生效 → 保存后 `global.json` 数值正确。

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/Items/SliderRowVM.cs SovereignTowns/src/Ui/ControlPanel/Items/SettingsGroupVM.cs SovereignTowns/src/Ui/ControlPanel/Tabs/StrategyTabVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/STCPSliderRow.xml" "SovereignTowns/SovereignTowns/GUI/Prefabs/STCPChip.xml" "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel Tab 2 — strategy parameters"
```

---

## Task 11: Tab 5 非首府驻军

对应 `index.html` Tab 5：2 张滑块卡片（`BranchDefaults.TargetPower` 默认 150、`BranchDefaults.LowTierMinFraction` 默认 0.20）+「全部恢复默认」。简单任务，复用 `SliderRowVM`。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/Tabs/BranchesTabVM.cs`
- Modify: `ControlPanelVM.cs`、`SovereignTownsControlPanel.xml`

- [ ] **Step 1: 写 BranchesTabVM.cs**

用两个 `SliderRowVM` 复用 Task 10 的行控件。为两项各造一个 `SpecEntry`：
- `TargetPower`：Root=`BranchDefaults`、Min=0、Max=100000、Step=1、Discrete=true、Def=150。
- `LowTierMinFraction`：Root=`BranchDefaults`、Min=0、Max=1、Step=0.01、Discrete=false、Def=0.20。

label/hint 抄 `index.html:864-865`、`889-890`。读写委托用 Task 10 的反射 `GetD/SetD`（或直接 `config.BranchDefaults.TargetPower` 委托）。提供 `Title`、`Intro`、`ExecuteResetAll()`（两项回默认）、`ShowResetAll`（任一非默认时 true）。

- [ ] **Step 2: ControlPanelVM 持有 + prefab 填 Tab4 容器**

（Tab5 在 0-based 索引为 4。）标题 +「↺ 全部恢复默认」按钮（`IsVisible="@BranchesTab.ShowResetAll"`）+ 说明 + 菱形线 + 2 个 `STCPSliderRow`（卡片样式包一层）。

- [ ] **Step 3: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：Tab5 → 2 张卡片、拖动改值 dirty、恢复默认生效 → 保存后 `global.json` 的 `BranchDefaults` 正确。

- [ ] **Step 4: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/Tabs/BranchesTabVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel Tab 5 — branch garrison"
```

---

## Task 12: Tab 3 兵种编制

对应 `index.html` Tab 3：2 个模式按钮 + 通用模式下的文化过滤 chip + 4 条比例滑块（自动归一化）+ Tier 范围 chip。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/ChipVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Tabs/CompositionTabVM.cs`
- Modify: `ControlPanelVM.cs`、`SovereignTownsControlPanel.xml`（复用 `STCPChip.xml`）

- [ ] **Step 1: 写 ChipVM.cs**

```csharp
using System;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ControlPanel;

/// <summary>通用 chip：分组筛选 / 文化过滤 / tier 选择 / 兵种类型过滤 复用。</summary>
public sealed class ChipVM : ViewModel
{
    private readonly Action _onClick;
    private bool _isActive, _isDimmed;

    [DataSourceProperty] public string Label { get; }
    [DataSourceProperty] public bool IsActive { get => _isActive; set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } } }
    [DataSourceProperty] public bool IsDimmed { get => _isDimmed; set { if (_isDimmed != value) { _isDimmed = value; OnPropertyChanged(nameof(IsDimmed)); } } }

    public ChipVM(string label, Action onClick) { Label = label; _onClick = onClick; }
    public void ExecuteClick() { if (!_isDimmed) _onClick?.Invoke(); }
}
```

- [ ] **Step 2: 写 CompositionTabVM.cs**

字段全在 `config.GlobalDefaults`：`UseGenericMatching`(bool)、`GenericCultureFilter`(string)、`CavalryRatio`/`HorseArcherRatio`/`InfantryRatio`/`RangedRatio`(float)、`MinTier`/`MaxTier`(int)。

- 模式：`IsGenericMode` 绑 `UseGenericMatching`；2 个模式按钮 `ExecuteSetGenericMode()` / `ExecuteSetExactMode()`。
- 文化过滤：3 个 `ChipVM`（`PlayerCulture`/`CapitalCulture`/`Any`），label 抄 `index.html:1105-1107`。
- 4 条比例滑块：**完整移植 `adjustRatio`**（index.html:1523-1553）——拖一条，其余按当前相对比例 rescale 使 Σ=1.0；其余全 0 时均分；最后把浮点漂移补到「除当前项外最大的那条」。`ResetRatios()` 移植 `resetRatios`（0.20/0.05/0.50/0.25）。Σ 显示 + `RatioSumOk`（0.9–1.1）。
- Tier 范围：最低/最高各 6 个 `ChipVM`(1-6)。移植联动：选 Min 时若 Max<Min 则 Max=Min；Max chip 中 `n<Min` 的置 `IsDimmed`。`ResetTier()`=T2/T5。

比例归一化核心（C# 版，移植 `adjustRatio`）：

```csharp
static readonly string[] RatioKeys = { "CavalryRatio", "HorseArcherRatio", "InfantryRatio", "RangedRatio" };

public void AdjustRatio(string key, double raw)
{
    var g = _config.GlobalDefaults;
    double v = Math.Max(0, Math.Min(1, double.IsNaN(raw) ? 0 : raw));
    SetRatio(g, key, v);
    var others = RatioKeys.Where(k => k != key).ToArray();
    double remaining = 1 - v;
    double otherSum = others.Sum(k => GetRatio(g, k));
    if (otherSum > 0.0001)
    {
        double f = remaining / otherSum;
        foreach (var k in others) SetRatio(g, k, Math.Max(0, Math.Min(1, GetRatio(g, k) * f)));
    }
    else
    {
        double each = Math.Max(0, remaining / others.Length);
        foreach (var k in others) SetRatio(g, k, each);
    }
    double total = RatioKeys.Sum(k => GetRatio(g, k));
    double drift = 1 - total;
    if (Math.Abs(drift) > 0.0001)
    {
        string biggest = others[0];
        foreach (var k in others) if (GetRatio(g, k) > GetRatio(g, biggest)) biggest = k;
        SetRatio(g, biggest, Math.Max(0, Math.Min(1, GetRatio(g, biggest) + drift)));
    }
    _markDirty();
    RefreshRatioBindings();   // 4 条滑块 + Σ 都 OnPropertyChanged
}
// GetRatio/SetRatio 用反射或 switch 读写 GlobalDefaults 的对应 float 属性。
```

- [ ] **Step 3: prefab 填 Tab3 容器（Tab 索引 2）**

标题 + 说明 + 2 个模式 `ButtonWidget`（选中描金）+ 菱形线。精确模式下显示引导卡（`IsVisible="@IsExactMode"`，含「前往兵员模板」按钮 `Command.Click` 切到 Tab3）。通用模式下（`IsVisible="@IsGenericMode"`）：文化 chip 行 + 4 条比例 `STCPSliderRow`（或专用行——比例行带 Σ）+ Tier min/max 各一行 `STCPChip`。

- [ ] **Step 4: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：Tab3 → 切模式（精确↔通用，UI 随之显隐）→ 通用模式拖一条比例滑块，其余三条自动缩放、Σ≈1.00 → tier chip min/max 联动、越界置灰 → 保存后 `global.json` 的 4 个 Ratio 与 Min/MaxTier 正确。

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/Items/ChipVM.cs SovereignTowns/src/Ui/ControlPanel/Tabs/CompositionTabVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel Tab 3 — troop composition"
```

---

## Task 13: Tab 4 兵员模板

对应 `index.html` Tab 4：左「兵种名录」（搜索 + 文化/类型/tier 过滤 + 可滚动列表 + 加入按钮）+ 右「已选名单」（占比滑块 + 移除）。最复杂的一页。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/TroopRowVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/TroopTemplateRowVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Tabs/TemplatesTabVM.cs`
- Create: `SovereignTowns/SovereignTowns/GUI/Prefabs/STCPTroopCatalogRow.xml`、`STCPTroopTemplateRow.xml`
- Modify: `ControlPanelVM.cs`、`SovereignTownsControlPanel.xml`

- [ ] **Step 1: 写 TroopRowVM.cs（名录行）**

字段：`Id`、`Name`、`CultureName`、`Tier`（int）、`TierText`(`"T{n}"`)、`Type`、`TypeGlyph`（♞/↝/⚔/⤧，移植 `typeGlyph`）、`TypeLabel`、`IsAdded`(bool，绑「＋加入」/「✓已加」)。命令 `ExecuteToggle()`（未加→add，已加→remove），回调进 `TemplatesTabVM`。

- [ ] **Step 2: 写 TroopTemplateRowVM.cs（已选行）**

字段：`Id`、`Name`、`CultureName`、`TierText`、`TypeLabel`、`Ratio`(float 0-1)、`RatioPercent`(`"{0:0}%"`)、`EstimatedCount`（`Math.Round(ratio * TargetTotalCount)`）。命令 `ExecuteRemove()`；`Ratio` setter 调 `TemplatesTabVM.UpdateTroopRatio(id, v)`。

- [ ] **Step 3: 写 TemplatesTabVM.cs**

数据：`ControlPanelData.CollectTroops()` 得全量兵种；`config.GlobalDefaults.ExactTroopTemplate`（`Dictionary<string,float>`）是已选。

- 搜索/过滤：`SearchText`、`CultureFilter`、`TypeFilter`、`TierFilter`、`HideSelected` —— 任一变更时**重建** `MBBindingList<TroopRowVM> FilteredTroops`（MBBindingList 不自动通知，必须清空重填，见设计 §6），上限前 200 条（移植 `filteredTroops` + `slice(0,200)`）。文化/类型/tier 过滤 chip 用 `ChipVM`；文化 chip 列表从兵种去重得到（移植 `cultureList`）。
- 已选名单：`MBBindingList<TroopTemplateRowVM> SelectedTroops`，从 `ExactTroopTemplate` 构建。
- 占比归一化：**完整移植** `addTroop`/`updateTroopRatio`/`removeTroop`/`clearTroops`/`_snapTroopSumTo1`（index.html:1635-1714）。`addTroop`：新条目得 `1/(N+1)`，其余缩 `N/(N+1)`，再 snap。`removeTroop`：删后剩余按原占比补到 Σ=1。`updateTroopRatio`：改一条，其余 rescale 剩余额度。
- 每次增删改后：`_markDirty()`、重建两个列表、刷新表头计数（已选数 / 估算人数）、刷新名录行的 `IsAdded`。
- `clearTroops`：`InformationManager.ShowInquiry` 确认后清空 `ExactTroopTemplate`。
- 顶部计数：`SelectedCount`、`ExactTroopTotal`（`Σratio × TargetTotalCount`）、`RatioSumPercent`。
- 模式提示横幅：`UseGenericMatching` 时显示「不生效」横幅 + 一键切精确模式。

- [ ] **Step 4: 写 2 个行 prefab + 填 Tab4 容器（Tab 索引 3）**

- `STCPTroopCatalogRow.xml`：tier 圆点（小 Widget，颜色绑 tier）+ `T{n}` + 名称 + 文化徽章 + 类型字形 + `ButtonWidget`（`Text` 绑「＋加入」/「✓已加」，`Command.Click="ExecuteToggle"`）。
- `STCPTroopTemplateRow.xml`：tier 圆点 + 名称 + 文化/tier/类型 + 占比% + ≈人数 + ✕ 移除 + 占比滑块（绑 `Ratio`）。
- Tab4 容器：表头计数行 + 说明 + 模式横幅 + 菱形线 + 左右两栏。左栏：搜索 `EditableTextWidget`（绑 `SearchText`）+ 3 行过滤 chip + 匹配计数 + `ScrollablePanel`>`ListPanel DataSource="@FilteredTroops" ItemTemplate="STCPTroopCatalogRow"`。右栏：Σ + `ScrollablePanel`>`ListPanel DataSource="@SelectedTroops" ItemTemplate="STCPTroopTemplateRow"`。

- [ ] **Step 5: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：Tab4 → 左侧兵种列表加载（数百条，显示前 200）→ 搜索/文化/类型/tier 过滤生效 → 点「＋加入」→ 右侧出现、按钮变「✓已加」→ 拖右侧占比滑块，其余 rescale、Σ≈100% → ✕移除/清空生效 → 保存后 `global.json` 的 `ExactTroopTemplate` 正确。

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/Items/TroopRowVM.cs SovereignTowns/src/Ui/ControlPanel/Items/TroopTemplateRowVM.cs SovereignTowns/src/Ui/ControlPanel/Tabs/TemplatesTabVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/STCPTroopCatalogRow.xml" "SovereignTowns/SovereignTowns/GUI/Prefabs/STCPTroopTemplateRow.xml" "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel Tab 4 — troop template picker"
```

---

## Task 14: Tab 6 财务

对应 `index.html` Tab 6：3 张汇总表（今日/本周/全部）+ 近期流水表（最近 50 条）。

**Files:**
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/FinanceRowVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Items/FinanceTableVM.cs`
- Create: `SovereignTowns/src/Ui/ControlPanel/Tabs/FinanceTabVM.cs`
- Create: `SovereignTowns/SovereignTowns/GUI/Prefabs/STCPFinanceRow.xml`
- Modify: `ControlPanelVM.cs`、`SovereignTownsControlPanel.xml`

- [ ] **Step 1: 写 FinanceRowVM.cs**

一行三列：`Col1`、`Col2`、`Col3`、`Col4`（字符串，复用于汇总表的「类别/金额」两列和流水表的「时间/类别/金额/备注」四列）+ `IsTotal`(bool，合计行加粗)。

- [ ] **Step 2: 写 FinanceTableVM.cs**

一张汇总表：`Title` + `MBBindingList<FinanceRowVM> Rows`（分类行）+ 合计行。构造时传入分类金额字典 + 合计。金额格式 `"-{amt}d"`（移植 WebUI）。

- [ ] **Step 3: 写 FinanceTabVM.cs**

调 `ControlPanelData.BuildFinanceReport()` 得报告，构造 3 个 `FinanceTableVM`（今日/本周/全部）+ `MBBindingList<FinanceRowVM> RecentEntries`（最近 50 条流水：时间用 `DateTime` 本地化格式、类别、金额、备注）。提供 `Refresh()` 方法重取重建。`financeError` 文案处理同 WebUI。游戏暂停期间财务不变 —— **只在面板打开 / 切到本 tab 时 `Refresh()` 一次**（设计 §5）。

- [ ] **Step 4: prefab 填 Tab6 容器（Tab 索引 5）**

标题 + 说明 + 3 张表（横向排，每张 = 标题 + `ListPanel DataSource` of `STCPFinanceRow`）+ 近期流水表（`ScrollablePanel` + 表头行 + `ListPanel`）。`STCPFinanceRow.xml`：4 个 `RichTextWidget` 列，`IsTotal` 时加粗描金。`ControlPanelVM` 在 `ActiveTab` 切到 5 时调 `FinanceTab.Refresh()`。

- [ ] **Step 5: 编译 + 游戏内验证**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
游戏内：Tab6 → 3 张汇总表 + 流水表显示（新档无开销则为空表，不报错）→ 触发过招募/调拨后重开面板 → 财务有数据。

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/Items/FinanceRowVM.cs SovereignTowns/src/Ui/ControlPanel/Items/FinanceTableVM.cs SovereignTowns/src/Ui/ControlPanel/Tabs/FinanceTabVM.cs SovereignTowns/src/Ui/ControlPanel/ControlPanelVM.cs "SovereignTowns/SovereignTowns/GUI/Prefabs/STCPFinanceRow.xml" "SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsControlPanel.xml"
git commit -m "feat(ui): control panel Tab 6 — finance report"
```

---

## Task 15: 主题打磨 + 整体验收

**Files:**
- Modify: 全部 `GUI/Prefabs/*.xml`（仅样式属性）

- [ ] **Step 1: 主题统一**

逐个 prefab 核对金/黑主题：深色面板底、金色描边与标题、羊皮纸色正文、`gold-rule` 金线、`diamond-divider` 菱形线、`tier-dot` 按 tier 着色。控件保持紧凑（设计 §2.5：细滑块、13–15px 字号、行高紧凑），面板大、控件占比小，**不**出现 IG 那种大块控件。若纯色 Brush 无法达成，按设计 §2.3 应急项加 `GUI/Brushes/*.xml` 并在 csproj 加 `GUI/Brushes` 部署行。

- [ ] **Step 2: 编译**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 成功。

- [ ] **Step 3: 端到端游戏内验收**

逐项对照 `WebUI/index.html` 核对：
1. 大地图左侧按钮 → 弹面板、暂停游戏；✕ / ESC 关闭、恢复。
2. 6 个标签页布局、文案、交互逐一对照 WebUI 一致。
3. 改动 → 表头 dirty → 保存 → 关面板 → 看 `global.json` 全部字段正确写入。
4. 重开面板读到新值；有未保存改动时关闭/重读有二次确认。
5. WebUI 仍能照常打开（两套并行）。
6. `ModLogs/SovereignTowns/` 全程无 Error。

- [ ] **Step 4: Commit**

```bash
git add "SovereignTowns/SovereignTowns/GUI/Prefabs"
git commit -m "style(ui): unify control panel gold/dark theme + density pass"
```

---

## Self-Review（计划自查结果）

- **Spec 覆盖**：设计文档 §3 入口→Task 5；§4 表头/左栏→Task 6/7；§5 六个 tab→Task 8/10/12/13/11/14；§2.2 工作副本→Task 3/6；§2.3 主题→Task 15；§2.4 本地化→Task 2；§2.5 尺寸密度→Task 4/15。无遗漏。
- **类型一致**：`ControlPanelVM`/`ControlPanelData`/`SpecEntry`/`SliderRowVM`/`ToggleRowVM`/`ChipVM` 在跨任务引用处命名一致；`TroopDumper.TroopEntry`(public)、`ControlPanelData.Collect*` 一致。
- **占位说明**：Task 8/9 中「从 index.html 第 X 行抄文案」是**有意**的——双语文案在 index.html 已完全确定，重抄进计划反而易错；执行者照行号抄即可。Task 6 的 `AddLog` 占位已在 Task 7 Step 2 明确要求回填。
- **已知执行期需核对项**：`ConfigurationManager.ReplaceAndSave`/`TryReload` 真实签名、`ModExpenseLedger` 财务 API、`EnabledFeatures`/`GlobalDefaults`/`BranchDefaults` 真实属性名、`CampaignEvents.TickEvent` 委托签名、`GameStateManager` 暂停 API、Gauntlet prefab XML 语法——计划已在对应任务标注「执行前读 X 文件核对」。
