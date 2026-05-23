# 设计文档：建筑等级 → 队伍加成（军营 / 哨所）

- 日期：2026-05-21
- 目标：让受管氏族定居点的**建筑等级**正确地驱动本 Mod 各类队伍的加成；加成系数在控制面板双端可调；巡逻队加成改绑**哨所**；受管氏族的 vanilla 巡逻队功能被禁用。
- 决策前提（已与用户确认）：
  - 加成广度 = **适中方案**：每种队伍一个「基础值 + 每级增量」的并发上限；驻军每日 XP ← 军营等级；巡逻模板等级 ← 哨所等级。不做每种队伍的额外质量维度。
  - 巡逻加成建筑 = **哨所**（Guard House）；其余队伍（征兵 / 调拨 / 出击）= **军营**（Barracks）。
  - 整个特性作为一份设计 + 一份实现计划推进，不拆分。

---

## 1. 背景与已核实事实

### 1.1 vanilla 建筑（反编译 `TaleWorlds.CampaignSystem.Settlements.Buildings.DefaultBuildingTypes`，v1.3.15）

| 玩家叫法 | vanilla 英文名 | 城镇 StringId | 城堡 StringId | 本地化 key | vanilla 效果 |
|---|---|---|---|---|---|
| 军营 | Barracks | `building_settlement_barracks` | `building_castle_barracks` | `{=x2B0OjhI}` → "军营" | 驻军上限、驻军薪酬减免 |
| 哨所 | Guard House | `building_settlement_guard_house` | `building_castle_guard_house` | `{=OHEiwoHC}` → "哨所" | 城镇版：`PatrolPartyStrength` +1/2/3、囚犯上限；城堡版：民兵、囚犯上限 |

- `DefaultBuildingTypes.MaxBuildingLevel = 3`。建筑等级取值 0（未建造）~ 3。
- 哨所的 vanilla 语义本就是「提供巡逻队提升治安」，与本特性把巡逻加成绑到哨所完全契合。

### 1.2 现存 bug（本特性的前置修复）

Mod 现有三处建筑等级逻辑匹配的 StringId 是错的：

| 文件 | 方法 | 现匹配 | 正确应为 |
|---|---|---|---|
| `Lifecycle/PartyLifecycleManager.cs` | `ComputePatrolCapForHome` | `settlement_garrison` / `castle_barracks` | `building_settlement_barracks` / `building_castle_barracks`（且应改读哨所） |
| `Upgrades/GarrisonXpInjector.cs` | `ComputeXpFromBarracks` | `settlement_garrison` / `castle_barracks` | `building_settlement_barracks` / `building_castle_barracks` |
| `Patrol/PatrolDispatcher.cs` | `TryFindPatrolTemplate` | `settlement_garrison` | `building_settlement_guard_house` / `building_castle_guard_house` |

后果：三处永远匹配不到建筑、全部走 0 级兜底 —— 巡逻 / 征兵并发上限恒为 1、驻军每日 XP 恒为 5、巡逻模板恒为 level_1。**不修这个 bug，「按建筑等级加成」无从谈起。**

### 1.3 现状

- 队伍并发上限由 `PartyLifecycleManager.GetMaxFor(home, kind)` 决定：征兵 / 巡逻 → `ComputePatrolCapForHome`（建筑等级 + 1，但 ID 错）；调拨 / 出击 → 固定常量 `MaxTransfersPerTown = 1` / `MaxSallyForthPerTown = 1`。
- 所有建筑等级公式硬编码，无配置项。
- 本 Mod 巡逻队（`StPatrolPartyComponent`）绕过 vanilla `PatrolPartyComponent.CreatePatrolParty`，与 vanilla 巡逻队**共存、互不干涉**（`StPatrolPartyComponent.cs` 注释明示）。vanilla 哨所仍会为受管定居点生成自己的巡逻队。

---

## 2. 架构

四块改动，自底向上，遵守 CLAUDE.md 分层。

```
Layer 4  UI            : ControlPanelSpecs.cs / WebUI index.html — 新增「建筑加成」分组
Layer 3  Dispatchers   : PartyLifecycleManager.GetMaxFor / PatrolDispatcher / GarrisonXpInjector — 改读新 helper + 配置
Layer 2  —             : VanillaPatrolSuppressor（新增，与 VanillaSuppressionManager 并列）
Layer 1  Infrastructure: BuildingLevelReader（新增）；GlobalConfig.BuildingBonus（新增配置子对象）
```

### 2.1 `BuildingLevelReader`（新增，Layer 1）

单一职责：把 vanilla 建筑 StringId 集中到一处，提供安全的等级读取。

```
enum StBuilding { Barracks, GuardHouse }

static int GetLevel(Settlement settlement, StBuilding building)
    → 按 settlement.IsCastle 选 building_castle_* / building_settlement_* StringId
    → 遍历 settlement.Town.Buildings 精确匹配 BuildingType.StringId
    → 返回 CurrentLevel，钳制 [0, 3]
    → town == null / 未建造 / 任何异常 → 返回 0（绝不抛，遵守不变量 #5）
```

- StringId 字典是全 Mod 唯一真源；三处现有调用点（`ComputePatrolCapForHome`、`ComputeXpFromBarracks`、`TryFindPatrolTemplate`）改走它，删除各自重复的遍历代码。
- 语义差异：reader 返回**原始等级**（0~3）；「上限 = 基础值 + 等级 × 增量」由调用方算，不再像旧 `ComputePatrolCapForHome` 那样内嵌 `+1`。

### 2.2 `GlobalConfig.BuildingBonus`（新增配置子对象，Layer 1）

新增 `BuildingBonusConfig`，挂在 `GlobalConfig` 根下（与 `ClanPatrol` / `ClanRecruiter` 并列）。字段（适中方案，全部整数）：

| 字段 | 默认 | 含义 | 建筑 |
|---|---|---|---|
| `RecruiterBaseCap` | 1 | 征兵队并发上限基础值 | 军营 |
| `RecruiterCapPerBarracksLevel` | 1 | 军营每级 +N 征兵队上限 | 军营 |
| `TransferBaseCap` | 1 | 调拨队并发上限基础值 | 军营 |
| `TransferCapPerBarracksLevel` | 1 | 军营每级 +N 调拨队上限 | 军营 |
| `SallyBaseCap` | 1 | 出击队并发上限基础值 | 军营 |
| `SallyCapPerBarracksLevel` | 1 | 军营每级 +N 出击队上限 | 军营 |
| `PatrolBaseCap` | 1 | 巡逻队并发上限基础值 | 哨所 |
| `PatrolCapPerGuardHouseLevel` | 1 | 哨所每级 +N 巡逻队上限 | 哨所 |
| `GarrisonXpBasePerDay` | 5 | 驻军每兵每日 XP 基础值 | 军营 |
| `GarrisonXpPerBarracksLevel` | 5 | 军营每级 +N 驻军每日 XP | 军营 |

- 上限公式：`cap = Base + level × PerLevel`，结果钳制 ≥ 1。
- 默认值的设计意图：让 0 级建筑下 = 旧的固定行为（征兵/巡逻/调拨/出击上限均为 1、驻军 XP 5），3 级下 = 上限 4 / XP 20。**调拨与出击上限从固定常量改为建筑派生 —— 这是用户明确要的行为变更**，`MaxTransfersPerTown` / `MaxSallyForthPerTown` 常量删除。
- 巡逻模板等级直接 = 哨所等级，无独立旋钮。
- 读取一律 `ConfigurationManager.Current?.BuildingBonus?.X ?? <默认>`（config 早期可能为 null）。
- `ConfigVersion` bump → 旧 `global.json` 版本不匹配时 `TryLoadFromDisk` 回退默认（CLAUDE.md 允许，无需迁移代码）。

### 2.3 控制面板双端（Layer 4）

- `ControlPanelSpecs.cs` 的 `AllGroups` 新增一个 `SpecGroup`（key `building_bonus`，「建筑加成 / Building bonuses」），10 条 `SpecEntry`。需要新增一个 `Root` 取值 `"BuildingBonus"`，并在控制面板 VM 的 Root→对象映射里接上。
- WebUI `index.html` 同步等价 specs 分组（双端同源，遵守 memory `feedback_control_panel_dual_surface`）。
- `ConfigurationManager` 新增 `ValidateBuildingBonus`（与 `ValidateThresholds` 并列），给每个字段加上下界；上下界与控制面板 spec 的 min/max 对齐（遵守上一任务建立的「前后端对齐」惯例）。建议范围：上限类 Base [1,10] / PerLevel [0,5]；XP 类 Base [0,50] / PerLevel [0,50]。

### 2.4 哨所接管巡逻

- `PartyLifecycleManager.GetMaxFor`：征兵 / 调拨 / 出击 → 军营派生上限；巡逻 → 哨所派生上限。
- `PatrolDispatcher.TryFindPatrolTemplate` → 读哨所等级选 `settlement_patrol_template_level_{1,2,3}`（模板 StringId 本身已验证正确，只是选级依据从军营改哨所）。
- `GarrisonXpInjector.ComputeXpFromBarracks` → 读军营等级（仅修 ID + 接配置）。

### 2.5 `VanillaPatrolSuppressor`（新增，Layer 2）

禁用受管氏族的 vanilla 巡逻队。

- **范围**：与 `VanillaSuppressionManager` 一致 —— 受 `CapitalRegistry` 接管且有可用首府的氏族；离开受管范围则不再抑制。额外门控：仅当 `EnabledFeatures.AutoPatrol = true` 时启用（本 Mod 巡逻关闭时不该顺手砍掉 vanilla 巡逻）。
- **机制**：vanilla 无 `GarrisonAutoRecruitmentIsEnabled` 那样的 surgical flag 可关巡逻。候选方案在实现计划阶段定，倾向：Harmony patch vanilla 巡逻队创建入口，受管定居点跳过；并在 tick 中解散已绑定到受管定居点的存量 vanilla 巡逻队。Harmony patch 包 try/catch，失败 → 退回 vanilla 行为（fail-safe）。
- 具体 patch 目标需在计划阶段反编译确认 vanilla 巡逻生成点。

---

## 3. 数据流

```
每小时/每日 tick
  └─ PartyLifecycleManager.GetMaxFor(home, kind)
       └─ BuildingLevelReader.GetLevel(home, Barracks|GuardHouse) → 0..3
       └─ cap = Base + level × PerLevel（取自 BuildingBonusConfig）
  └─ GarrisonXpInjector：Xp = XpBase + barracksLevel × XpPerLevel
  └─ PatrolDispatcher.TryFindPatrolTemplate：guardHouseLevel → 模板级
  └─ VanillaPatrolSuppressor：受管定居点 → 拦截/解散 vanilla 巡逻队

控制面板（Gauntlet / WebUI）改值
  └─ ConfigurationManager 校验（ValidateBuildingBonus）→ 落盘 → 下个 tick 生效
```

## 4. 错误处理

- `BuildingLevelReader.GetLevel` 任何异常 → 返回 0（不变量 #5）。
- 配置读取一律 `?.` + `?? 默认`。
- Harmony patch try/catch，异常 → vanilla 原行为。
- 校验失败的配置 → `ConfigurationManager` 拒绝并保留旧值（沿用现有 `Validate*` 模式）。

## 5. 验证

无单元测试（CLAUDE.md）。启动游戏 + 看 `ModLogs/SovereignTowns/` 日志：

1. 在受管城镇把哨所建到 N 级 → 巡逻队并发上限 = `PatrolBaseCap + N × PatrolCapPerGuardHouseLevel`。
2. 军营 N 级 → 征兵/调拨/出击上限随之变化；驻军每日 XP = `XpBase + N × XpPerLevel`。
3. 受管定居点不再生成 vanilla 巡逻队；存量 vanilla 巡逻队被解散。
4. Gauntlet 面板与 WebUI 都能显示并保存「建筑加成」10 个旋钮；越界值被 `ValidateBuildingBonus` 拒绝。
5. 城堡（IsCastle）走 `building_castle_*` 分支正常。

## 6. 实现顺序

1. `BuildingLevelReader` + 修三处调用点的建筑 ID（前置 bug 修复）。
2. `BuildingBonusConfig` + 接入 `GetMaxFor` / `ComputeXpFromBarracks` / 巡逻模板;删除 `MaxTransfersPerTown` / `MaxSallyForthPerTown`。
3. 控制面板双端「建筑加成」分组 + `ValidateBuildingBonus`。
4. `VanillaPatrolSuppressor`。

## 7. 存档影响

- `BuildingBonusConfig` 是新配置对象 → `ConfigVersion` bump，旧 `global.json` 回退默认（可接受）。
- 队伍上限是运行期派生值、不持久化；无 `Saveable` 类型变更，不触及 `SaveBaseId` / `LocalSaveId`。

## 8. 范围外（已记录，本特性不处理）

- `PrisonerRecruitmentManager.ComputeConformityFromDungeon` 匹配 `settlement_dungeon` / `castle_dungeon` —— vanilla `DefaultBuildingTypes` 中**没有地牢建筑**（囚犯上限是 Fortifications / Barracks 的效果）。疑似同类 ID bug，但囚犯不属于「队伍」，留待单独修复。
