# B6 — Config UI 重做（5-tab + 模板 tab + 中文化 + ribbon 翻左侧）

**日期**：2026-05-14
**触发**：用户反馈 ConfigScreen 太长、找不到驻军模板配置、只要中文、ribbon 按钮素材和右侧位置冲突。
**范围**：UI/i18n/前端逻辑，不动 Manager 层、不动存档结构、不动 LLM。

---

## 1. 目标

把当前**单页超长滚动列表**改成 5-tab 分页，让「驻军模板」（含 ExactTroopTemplate 编辑 + TrainingTemplate 一键预设）成为顶级 tab；标签清理为纯中文；map ribbon 整体从右侧搬到左侧让按钮 art 与位置一致。

### 用户三项明确诉求
1. **分页**：5 个顶部 tab（功能开关 / 数量&预算 / 兵种&Tier比例 / 模板&资源 / 按城堡覆盖）
2. **驻军模板**：两种 template 都要：
   - **ExactTroopTemplate** ——「编辑士兵定义」按钮当前藏在中间，提到「模板&资源」tab 顶部
   - **TrainingTemplate** —— 代码里有 `FrontierDefense/TradeHub/EliteNobleGarrison` 3 个 preset 但 UI 完全没接通，本批新增 Apply 按钮
3. **中文化**：所有 VM 标签去掉 `(EnglishIdentifier)` 后缀（如 `自动驻军 (AutoGarrison)` → `自动驻军`）
4. **Ribbon 翻左侧**：按钮 + 抽屉整体搬到屏幕左侧，与 `GameMenu.Extend.Button` 素材设计意图一致

---

## 2. 非目标

- 不动 ConfigurationManager / Save 结构（POCO 字段不变）
- 不动 dead-code `SovereignTownsConfigScreen.cs` 全屏对话框（grep 确认无人 PushScreen，**保留不修改**，避免节外生枝）
- 不动 SubModule.xml / TypeDefiner
- 不引入新的 hard invariant；try/catch 包裹规则继续遵守

### 关于死代码
`SovereignTownsConfigScreen.cs` + `SovereignTownsConfigScreen.xml`：实际 UI 入口只有 ribbon 抽屉。grep `new SovereignTownsConfigScreen` 0 matches。**本批不删，但所有 UI 改动只对 `SovereignTownsRibbon.xml` + VM 进行**。两份 XML 是历史并存，将来再清理。

---

## 3. Tab 分桶

| Tab | 标题 | 内容 | 来源字段 |
|---|---|---|---|
| 1 | **功能开关** | 现有 11 个 toggle | EnabledFeatures.* + UseGenericMatching |
| 2 | **数量&预算** | TargetTotalCount / MinimumDefenders / BudgetLimit / MinTier / MaxTier / WartimeMultiplier / PeacetimeMultiplier | GlobalDefaults |
| 3 | **兵种&Tier比例** | Σ readout + 警告 + 5 兵种占比 + 6 Tier 占比 | GlobalDefaults |
| 4 | **模板&资源** | A:「编辑士兵定义」按钮（ExactTroopTemplate）<br/>B: 3 个 TrainingTemplate Apply 按钮<br/>C: 食物 / XP / Conformity / 征兵护卫 / 村庄冷却 / 回首府阈值 | GlobalDefaults + GlobalConfig 顶层 |
| 5 | **按城堡覆盖** | 现有 per-settlement 列表 | SettlementSelectors |

**注**：`通用匹配 (UseGenericMatching)` 当前放在 toggle 列表里；它影响所有 ratio/template 行为，留在 tab 1（功能开关）即可，不必移到 tab 3。

---

## 4. VM 改动

### 4.1 SovereignTownsConfigVM 新增 tab 状态

```csharp
private int _selectedTabIndex = 0;

[DataSourceProperty] public bool IsFeaturesTab   { get => _selectedTabIndex == 0; }
[DataSourceProperty] public bool IsBudgetTab     { get => _selectedTabIndex == 1; }
[DataSourceProperty] public bool IsRatiosTab     { get => _selectedTabIndex == 2; }
[DataSourceProperty] public bool IsTemplatesTab  { get => _selectedTabIndex == 3; }
[DataSourceProperty] public bool IsSettlementsTab{ get => _selectedTabIndex == 4; }

// XML Command.Click 目标
public void SelectFeaturesTab()    => SetTab(0);
public void SelectBudgetTab()      => SetTab(1);
public void SelectRatiosTab()      => SetTab(2);
public void SelectTemplatesTab()   => SetTab(3);
public void SelectSettlementsTab() => SetTab(4);

private void SetTab(int idx)
{
    if (_selectedTabIndex == idx) return;
    _selectedTabIndex = idx;
    OnPropertyChanged(nameof(IsFeaturesTab));
    OnPropertyChanged(nameof(IsBudgetTab));
    OnPropertyChanged(nameof(IsRatiosTab));
    OnPropertyChanged(nameof(IsTemplatesTab));
    OnPropertyChanged(nameof(IsSettlementsTab));
}
```

### 4.2 把 NumericOptions 一个大集合拆成 3 个 MBBindingList

当前 `_numericOptions` 一锅烩。改为：

```csharp
public MBBindingList<STNumericOptionVM> BudgetNumerics  { get; }  // Tab 2
public MBBindingList<STNumericOptionVM> RatioNumerics   { get; }  // Tab 3
public MBBindingList<STNumericOptionVM> ResourceNumerics{ get; }  // Tab 4
```

`BuildOptions` 中按字段语义分配。**ratio sum readout 仍由 RecomputeRatioSum 一处计算**（5 兵种 + 6 Tier 两个 Σ 都显示在 tab 3），不需要拆开。

### 4.3 模板 tab 新增 button collection

```csharp
public MBBindingList<STButtonOptionVM> TemplateButtons { get; }   // Tab 4 顶部
```

构造时填入：
1. **「编辑士兵定义 (N)」** —— 现有 `exactTemplateButton`，从 `_buttonOptions` 移到 `TemplateButtons`
2. **「应用预设：边疆防御」** —— `TrainingTemplate.FrontierDefense()` Apply
3. **「应用预设：贸易枢纽」** —— `TrainingTemplate.TradeHub()` Apply
4. **「应用预设：精锐贵族」** —— `TrainingTemplate.EliteNobleGarrison()` Apply

每个预设按钮 hint/description 写明：「会覆盖目标人数 / 兵种比例 / Tier 比例 / 战时倍率，但不会改你的 ExactTroopTemplate / 功能开关 / 按城堡覆盖。」

### 4.4 Apply 预设的实现

```csharp
private void ApplyTrainingTemplate(TrainingTemplate t)
{
    try
    {
        var current = ConfigurationManager.Current.GlobalDefaults;
        var src = t.Rule;

        // 仅覆盖 numeric/ratio 字段，保留 ExactTroopTemplate
        current.TargetTotalCount   = src.TargetTotalCount;
        current.MinimumDefenders   = src.MinimumDefenders;
        current.BudgetLimit        = src.BudgetLimit;
        current.MinTier            = src.MinTier;
        current.MaxTier            = src.MaxTier;
        current.CavalryRatio       = src.CavalryRatio;
        current.InfantryRatio      = src.InfantryRatio;
        current.ArcherRatio        = src.ArcherRatio;
        current.CrossbowRatio      = src.CrossbowRatio;
        current.ThrowerRatio       = src.ThrowerRatio;
        current.Tier1Ratio         = src.Tier1Ratio;
        current.Tier2Ratio         = src.Tier2Ratio;
        current.Tier3Ratio         = src.Tier3Ratio;
        current.Tier4Ratio         = src.Tier4Ratio;
        current.Tier5Ratio         = src.Tier5Ratio;
        current.Tier6Ratio         = src.Tier6Ratio;
        current.WartimeMultiplier  = src.WartimeMultiplier;
        current.PeacetimeMultiplier= src.PeacetimeMultiplier;
        current.FoodSafetyThreshold= src.FoodSafetyThreshold;
        current.DailyTroopXpBonus  = src.DailyTroopXpBonus;
        // 不动 UseGenericMatching（用户语义切换，归功能开关）
        // 不动 ExactTroopTemplate（明确保留用户已编辑的具体兵种表）

        Logger.Info($"Applied TrainingTemplate '{t.TemplateId}' to GlobalDefaults");

        // 全量刷新 VM —— 重建 numeric option 集合最简单也最可靠
        RebuildAllOptionsAfterTemplateApply();
        RecomputeRatioSum();
    }
    catch (Exception ex)
    {
        Logger.Error($"ApplyTrainingTemplate({t?.TemplateId}) failed", ex);
        RatioSumWarning = $"应用预设失败：{ex.Message}";
    }
}
```

`RebuildAllOptionsAfterTemplateApply` 清空 3 个 numeric 集合 + 重新调用 `BuildOptions` 内部的同名子例程。最简做法：把现有 BuildOptions 拆成 `BuildToggleOptions / BuildBudgetNumerics / BuildRatioNumerics / BuildResourceNumerics / BuildTemplateButtons`，Apply 后只需 Clear + 重调后 4 个。

### 4.5 中文化清理

VM 字符串字面量批量改：

| 当前 | 改为 |
|---|---|
| `自动驻军 (AutoGarrison)` | `自动驻军` |
| `自动招募 (AutoRecruitment)` | `自动招募` |
| `自动巡逻 (AutoPatrol)` | `自动巡逻` |
| `城堡支持 (CastleSupport)` | `城堡支持` |
| `LLM 推理建议 (LlmReasoning)` | `LLM 推理建议` |
| `LLM 自动执行 (LlmAutoExecute)` | `LLM 自动执行` |
| `主动出击 (SallyForth)` | `主动出击` |
| `战利品-招募匹配俘虏 (AutoRecruitMatchingPrisoners)` | `战利品：招募匹配俘虏` |
| `战利品-出售非匹配俘虏 (AutoSellNonMatchingPrisoners)` | `战利品：出售非匹配俘虏` |
| `战利品-出售物品装备 (AutoSellLoot)` | `战利品：出售装备物品` |
| `通用匹配 (UseGenericMatching)` | `通用匹配（忽略文化）` |
| `目标驻军总数 (TargetTotalCount)` | `目标驻军总数` |
| `最少防守人数 (MinimumDefenders)` | `最少防守人数` |
| ... | ...（全部同理剥离括号） |

`STSettlementSelectorVM.cs` 内 `具体兵员模板` + per-rule numerics 同步处理。**保留**:
- `Σ = ...` Sigma 符号
- `tier` / `XP` / `LLM` / `denar` 等已成专有名词
- 占比/比例/倍率 等中文不动

---

## 5. XML 改动（SovereignTownsRibbon.xml）

### 5.1 Tab 栏（标题下、内容区上）

在 ContentWidget 内 InnerList 顶部插入一个固定高度的 ListPanel：

```xml
<!-- Tab bar -->
<ListPanel WidthSizePolicy="StretchToParent" HeightSizePolicy="Fixed" SuggestedHeight="44"
           MarginBottom="10" LayoutImp.LayoutMethod="HorizontalLeftToRight">
  <Children>
    <ButtonWidget WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent"
                  ButtonType="Toggle" IsSelected="@IsFeaturesTab"
                  Command.Click="SelectFeaturesTab" Brush="Recruitment.Popup.DoneButton"
                  UpdateChildrenStates="true">
      <Children>
        <RichTextWidget HorizontalAlignment="Center" VerticalAlignment="Center"
                        Brush="Recruitment.Popup.Done.Text" Text="@Tab1Title" />
      </Children>
    </ButtonWidget>
    <ButtonWidget ... IsSelected="@IsBudgetTab"      Command.Click="SelectBudgetTab"     Text="@Tab2Title" />
    <ButtonWidget ... IsSelected="@IsRatiosTab"      Command.Click="SelectRatiosTab"     Text="@Tab3Title" />
    <ButtonWidget ... IsSelected="@IsTemplatesTab"   Command.Click="SelectTemplatesTab"  Text="@Tab4Title" />
    <ButtonWidget ... IsSelected="@IsSettlementsTab" Command.Click="SelectSettlementsTab" Text="@Tab5Title" />
  </Children>
</ListPanel>
```

VM 增 5 个静态属性 `Tab1Title="功能开关"` 等（不变动 → 不需要 OnPropertyChanged）。

### 5.2 内容区分 5 块

InnerList 内部从「3 段 ListPanel 依次堆」改成「5 段 ListPanel，每段顶部 `IsVisible="@IsXxxTab"`」。

- 段 1（IsFeaturesTab）：原 ToggleOptions ListPanel
- 段 2（IsBudgetTab）：DataSource="{BudgetNumerics}" 的 numeric template
- 段 3（IsRatiosTab）：Σ readout + 警告 + DataSource="{RatioNumerics}"
- 段 4（IsTemplatesTab）：DataSource="{TemplateButtons}" 在顶部 + DataSource="{ResourceNumerics}"
- 段 5（IsSettlementsTab）：原 SettlementSelectors 列表 + empty hint

每段保留自己的 section header（FeaturesHeader / BudgetHeader / RatiosHeader / TemplatesHeader / PerSettlementHeader），但因 tab 切换后只有当前段可见，header 退化为「当前页副标题」，可直接保留视觉一致性。

### 5.3 Ribbon 翻到左侧

`SovereignTownsRibbon.xml` 顶层 4 处改动：

| 位置 | 当前 | 改后 |
|---|---|---|
| 最外层 Widget L19 | `HorizontalAlignment="Right"` | `HorizontalAlignment="Left"` |
| Overlay Widget L32 | `HorizontalAlignment="Right"` PositionXOffset=1070 | `HorizontalAlignment="Left"` PositionXOffset=-1070 |
| VisualDefinition Retracted | `PositionXOffset="1070"` | `PositionXOffset="-1070"` |
| VisualDefinition Expanded | `PositionXOffset="-32"` | `PositionXOffset="32"` |
| ExtendButton L40~ | `HorizontalAlignment` 默认 Left, MarginLeft=0 | `HorizontalAlignment="Right"`, MarginRight=0, **Brush.HorizontalFlip** 翻箭头朝向 |
| Frame1Brush L66 | `HorizontalAlignment="Right" MarginLeft="60"` | `HorizontalAlignment="Left" MarginRight="60"` |
| hinge sprite L96~ | `HorizontalAlignment="Right" MarginRight="-33"` | `HorizontalAlignment="Left" MarginLeft="-33"` |

**箭头翻向**：vanilla `GameMenu.Extend.Button.Arrow` 是右指箭头（菜单在右、按钮在菜单左缘指向左中央 = 收起方向）。翻到左侧后箭头要指向右（指向屏幕中央）。XML 的 BrushWidget 支持 `HorizontalFlip="true"`（参考 hinge sprite L102 已用 `VerticalFlip`，同语法）。

**FloatingPanelWidget.cs**：检查内部是否硬编码 PositionXOffset。若有，按上述符号同步翻转；若只是读 XML，无须改。

---

## 6. 文件清单

| 文件 | 改动 |
|---|---|
| `SovereignTowns/SovereignTowns/GUI/Prefabs/SovereignTownsRibbon.xml` | 加 tab 栏 + IsVisible 分段 + 翻左侧 |
| `SovereignTowns/src/Ui/ConfigScreen/SovereignTownsConfigVM.cs` | tab 状态 + 拆 3 numeric 集合 + TemplateButtons + ApplyTrainingTemplate + 标签中文化 |
| `SovereignTowns/src/Ui/ConfigScreen/Options/STSettlementSelectorVM.cs` | 标签中文化（剥离括号英文） |
| `SovereignTowns/src/Ui/MapRibbon/SovereignTownsFloatingPanelWidget.cs` | 如有硬编码偏移，同步翻转（可能 0 改动） |

---

## 7. 对架构契约的影响

| Hard invariant | 影响 |
|---|---|
| net472 / SaveBaseId / LocalSaveId | 无 |
| try/catch 包裹事件入口 | ApplyTrainingTemplate 加自己的 try/catch；Select*Tab 方法体量小但仍 try/catch 兜底 |
| HourlyTickPartyEvent 首行 PartyComponent 过滤 | 无关 |
| LLM 禁即时路径 | 无关（UI 不接 LLM） |
| SafeUninstall 覆盖自定义 component | 不引入新 component |
| Newtonsoft.Json | 不涉及 JSON I/O |

---

## 8. 验证

| 改动 | 命令/操作 | 预期 |
|---|---|---|
| Build | `dotnet build SovereignTowns/src/SovereignTowns.csproj -c Release` | `0 Error(s)`，**3** baseline warnings 不变 |
| Tab 切换 | 进游戏，地图打开 ribbon，依次点 5 个 tab | 每次只显示对应段，其它隐藏；无重叠 |
| Ribbon 翻左 | 打开/关闭 ribbon | 抽屉从屏幕**左侧**滑入；按钮在屏幕**左缘**；箭头朝右指向屏幕中央 |
| ExactTroop 按钮 | 模板&资源 tab 顶部 | 看到「编辑士兵定义 (N)」按钮，点击弹 STTroopPickerScreen |
| TrainingTemplate Apply | 点「应用预设：边疆防御」 | 关闭 ribbon 再打开，目标人数 = 120 / 步兵占比 = 0.55 / WartimeMultiplier = 2.0；ExactTroopTemplate 计数 N 不变 |
| 中文化 | 任意 toggle / numeric 标签 | 无 `(EnglishIdentifier)` 后缀 |

---

## 9. 实施顺序

1. **B6.1 中文化** — 纯字符串改动，最简，先 land 建立基线（独立小 commit）
2. **B6.2 5-tab pagination** — VM tab 状态 + 拆 numeric 集合 + XML tab 栏 + IsVisible
3. **B6.3 模板 tab** — 内嵌入 B6.2 框架；ApplyTrainingTemplate + 3 个预设 button
4. **B6.4 ribbon 翻左** — XML 7 处 alignment/offset/flip
5. **B6.5 构建 + 用户验证** — Release build，进游戏跑

每步独立 commit，失败不影响下一步。

---

## 10. 回滚

每步 = 单 commit，回滚 = revert。无存档兼容性影响（POCO 字段全部保留，无新字段）。中文化 + tab 化纯前端，不影响任何决策路径。

---

## 11. 已确认的设计选择

- **不引入 sub-tab**（tab 4 内部仍是「ExactTroop 按钮 + 3 预设按钮 + 资源 sliders」一列堆）
- **预设 Apply 不弹确认对话框**（v1）—— 操作不可撤销但保留 ExactTroopTemplate，风险有限；后续若用户反馈再加 yes/no
- **不删 dead-code** `SovereignTownsConfigScreen.cs/xml`，本批仅碰 ribbon 抽屉
