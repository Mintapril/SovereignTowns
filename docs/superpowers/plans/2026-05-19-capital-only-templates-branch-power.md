# 首府独占模板 + 非首府按兵力黑箱 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把"按 4 个 role 比例 / 模板"的驻军规则限定到**首府**；非首府改为"按 vanilla 兵力 + 低 tier 头数占比下限"的黑箱，删 per-town override，让出击队（Sally）计入驻军，非首府不再升级。

**Architecture:**
- `GlobalConfig` 字段一分为二：`GlobalDefaults`（首府用，原 `TownGarrisonRule` 全部字段保留）+ 新 `BranchDefaults: BranchRule { TargetPower, LowTierMinFraction }`。`PerSettlementOverrides` 字典整体删除（ConfigVersion bump → 19，旧 JSON 直接丢回默认）。
- `SupplyDemandGraph` 改为非对称 MCMF：首府节点维持原 4-role demand + role-surplus source；非首府节点只有 1 个 power demand + 1 个 lowTier-overflow source；两边通过现有 Garrison/Transfer 边互通。
- 兵力计算全程调 vanilla `Campaign.Current.Models.MilitaryPowerModel.GetTroopPower(...)`，AI 氏族目标 power 调 vanilla `FactionHelper.FindIdealGarrisonStrengthPerWalledCenter(...)`，不自造 tier 权重。
- 出击队（Sally）加入 `AccountInFlight` 视为"在外但回流"的驻军；巡逻队保持不计。非首府跳过 `TroopUpgradeService`。

**Tech Stack:** C# 10 / .NET Framework 4.7.2 / TaleWorlds.CampaignSystem (Bannerlord v1.3.15) / Newtonsoft.Json 13.x

**No unit tests in this project** — verification = `dotnet build` 通过 + 启动游戏看 `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\*.log` + 验证 `MinCostFlow.SelfTest` 风格的入口 self-check log。每个 task 完成后必须能独立 build，最后 Task 10 提供 playtest checklist。

---

## File Structure

**新增**：
- `SovereignTowns/src/Configuration/BranchRule.cs` — 非首府规则 POCO（2 个字段）
- `SovereignTowns/src/Evaluators/GarrisonPowerEvaluator.cs` — 兵力计算 + 低 tier head count 比例 + AI vanilla 目标查询 + branch rule 解析
- `SovereignTowns/src/Recruitment/BranchInPlaceRecruiter.cs` — 非首府本城招募（按 power 缺口、不按 role 比例、tier 优先）

**修改**：
- `Configuration/GlobalConfig.cs` — 删 `PerSettlementOverrides`、加 `BranchDefaults`
- `Configuration/ConfigurationManager.cs` — bump `CurrentConfigVersion = 19`、删 PerSettlement 分支、加 `GetBranchRuleFor(Town)`、删 `ApplySettlementOverrideFields`、`ValidateConfig` 简化
- `Algorithm/SupplyDemandGraph.cs` — 节点种类按 isCapital 分流；`AccountInFlight` 加 Sally
- `Managers/CapitalLogisticsManager.cs` — `ExecuteInPlaceRecruitment` 加 isCapital 分支
- `Upgrades/TroopUpgradeService.cs` — `TryUpgradeGarrison` 顶部 isCapital 早退
- `Configuration/AiCulturePresets.cs` — 文档化"preset 仅用于首府"
- `WebConfig/WebConfigEndpoints.cs` — 删 `PerSettlementOverrides` 兜底、`PutConfig` 接受 `BranchDefaults`、healthcheck 改报告 `branchTargetPower`
- `SovereignTowns/WebUI/index.html` — 删每城 override 整片表单、加 BranchDefaults 两字段表单

**不动**（确认）：
- `MatchPolicy.cs` — 现有 role-based API 仅首府路径调用，函数本身签名不变
- `Evaluators/RiskAssessmentService.cs` — 仅首府路径用
- `WebConfig/WebConfigServer.cs` 的 `/api/settlements/{id}/activities` 路由（诊断用，与 override 无关，保留）
- `MinCostFlow.cs` — 算法不动
- `SovereignTownsTypeDefiner.cs` — BranchRule 仅是 JSON POCO，不需要 SaveableField

---

## Task 1: 新增 `BranchRule` POCO

**Files:**
- Create: `SovereignTowns/src/Configuration/BranchRule.cs`

- [ ] **Step 1: Create BranchRule.cs**

```csharp
namespace SovereignTowns.Configuration;

/// <summary>
/// 非首府（城镇 / 城堡）驻军规则。极简：只关心"够多兵力"+"别全是高 tier 几个人"。
/// 不区分 role、不带模板、不限文化 / Tier 范围、不参与升级 — 非首府是 mod 内部的"黑箱"，
/// 只对首府的 role 缺口做兵力借调。
/// </summary>
public sealed class BranchRule
{
    /// <summary>目标兵力（vanilla strength 口径，调 MilitaryPowerModel.GetTroopPower 累加）。
    /// 玩家氏族用此固定值；AI 氏族用 GarrisonPowerEvaluator.ComputeAiVanillaTargetPower 动态算。
    /// 默认 150 ≈ 100 个 T3 步兵；城堡通常 ~80、大城镇 ~250 也合理。</summary>
    public int TargetPower { get; set; } = 150;

    /// <summary>低 tier（T1+T2）头数 / 驻军总头数 必须不低于此比例。
    /// 防御"3 个 T6 = 满 power"的退化局，确保有炮灰。默认 0.20。</summary>
    public float LowTierMinFraction { get; set; } = 0.20f;

    public static BranchRule CreateDefault() => new BranchRule();

    public BranchRule Clone() => new BranchRule
    {
        TargetPower = this.TargetPower,
        LowTierMinFraction = this.LowTierMinFraction,
    };
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded (BranchRule 不被任何代码引用，但 csproj 通过 glob 已包含)

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Configuration/BranchRule.cs
git commit -m "feat(config): add BranchRule POCO (TargetPower + LowTierMinFraction)"
```

---

## Task 2: 兵力计算工具 `GarrisonPowerEvaluator`

**Files:**
- Create: `SovereignTowns/src/Evaluators/GarrisonPowerEvaluator.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;
using Helpers;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Evaluators;

/// <summary>
/// 非首府兵力 / 低 tier 占比的统一计算入口。所有 power 数值都走 vanilla MilitaryPowerModel —
/// 不要在别处自己定义 tier→weight 映射，否则会与 RBM / vanilla 装备 mod 行为偏离。
/// </summary>
public static class GarrisonPowerEvaluator
{
    /// <summary>"低 tier" 阈值。Tier &lt;= 此值 计入 low tier 头数。默认 2 (T1+T2)。</summary>
    public const int LowTierMaxInclusive = 2;

    private static bool _selfTestLogged;

    /// <summary>
    /// 计算 roster 总兵力 (vanilla strength)。null/空 roster 返回 0。
    /// 与 PartyBase.CalculateCurrentStrength 同口径，但不需要 MobileParty / MapEvent context —
    /// 调用 GetTroopPower(side=Defender, context=Estimated, leaderModifier=0)。
    /// </summary>
    public static float ComputeRosterPower(TroopRoster? roster)
    {
        if (roster == null) return 0f;
        var model = Campaign.Current?.Models?.MilitaryPowerModel;
        if (model == null) return 0f;

        float total = 0f;
        for (int i = 0; i < roster.Count; i++)
        {
            var element = roster.GetElementCopyAtIndex(i);
            if (element.Character == null || element.Character.IsHero) continue;
            int alive = element.Number - element.WoundedNumber;
            if (alive <= 0) continue;
            float perTroop = model.GetTroopPower(
                element.Character,
                BattleSideEnum.Defender,
                MapEvent.PowerCalculationContext.Estimated,
                0f);
            total += alive * perTroop;
        }
        return total;
    }

    /// <summary>
    /// 低 tier head count 占比。规则：character.Tier &lt;= LowTierMaxInclusive 计入分子，
    /// 所有非 hero 计入分母。空 roster / 总数 0 返回 1.0（视为"满足约束"，避免空城被无谓阻塞）。
    /// </summary>
    public static float LowTierHeadCountFraction(TroopRoster? roster)
    {
        if (roster == null) return 1f;
        int lowTier = 0, total = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var element = roster.GetElementCopyAtIndex(i);
            if (element.Character == null || element.Character.IsHero) continue;
            total += element.Number;
            if (element.Character.Tier <= LowTierMaxInclusive) lowTier += element.Number;
        }
        return total <= 0 ? 1f : (float)lowTier / total;
    }

    /// <summary>
    /// AI 氏族非首府的目标 power — 复用 vanilla 自己的"理想驻军兵力"公式（FactionHelper），
    /// 让 AI 城镇 / 城堡的目标与 vanilla 期望持平，不让 mod 单方面拉高 / 拉低 AI 战力。
    /// 返回 0 表示无法计算（owner clan / kingdom 缺失 / 已 active=false 等）。
    /// </summary>
    public static int ComputeAiVanillaTargetPower(Town? town)
    {
        try
        {
            if (town == null || town.OwnerClan == null) return 0;
            var clan = town.OwnerClan;
            float baseline = FactionHelper.FindIdealGarrisonStrengthPerWalledCenter(clan.Kingdom, clan);
            if (baseline <= 0f) return 0;

            float economyMul = FactionHelper.OwnerClanEconomyEffectOnGarrisonSizeConstant(clan);
            float prosperityMul = FactionHelper.SettlementProsperityEffectOnGarrisonSizeConstant(town);
            float foodMul = FactionHelper.SettlementFoodPotentialEffectOnGarrisonSizeConstant(town.Settlement);
            float typeMul = town.IsTown ? 2f : 1f;

            float result = baseline * economyMul * prosperityMul * foodMul * typeMul;
            return result <= 0f ? 0 : (int)Math.Round(result);
        }
        catch (Exception ex)
        {
            Logger.Warn($"GarrisonPowerEvaluator.ComputeAiVanillaTargetPower threw for town '{town?.Settlement?.StringId}': {ex.Message}");
            return 0;
        }
    }

    /// <summary>启动期一次性自检：把公式应用到一个临时 roster 上，输出几个固定基准点供日志比对。
    /// 与 MinCostFlow.SelfTest 同套路（首次 EvaluateAll 时调用）。</summary>
    public static bool SelfTest(out string message)
    {
        if (_selfTestLogged) { message = "GarrisonPowerEvaluator: self-test already ran"; return true; }
        _selfTestLogged = true;
        try
        {
            var model = Campaign.Current?.Models?.MilitaryPowerModel;
            if (model == null) { message = "self-test skipped: MilitaryPowerModel is null"; return false; }

            float t1 = model.GetDefaultTroopPower(MakeStubTroop(1, mounted: false));
            float t3 = model.GetDefaultTroopPower(MakeStubTroop(3, mounted: false));
            float t6 = model.GetDefaultTroopPower(MakeStubTroop(6, mounted: false));
            message = $"GarrisonPowerEvaluator self-test: T1={t1:F2} T3={t3:F2} T6={t6:F2} (expected ≈ 0.66 / 1.30 / 2.56)";
            return true;
        }
        catch (Exception ex)
        {
            message = $"GarrisonPowerEvaluator self-test threw: {ex.Message}";
            return false;
        }
    }

    /// <summary>self-test 用：找一个 vanilla CharacterObject 当探针。若找不到匹配 Tier 的 fallback 第一个 non-hero。</summary>
    private static CharacterObject? MakeStubTroop(int targetTier, bool mounted)
    {
        foreach (var c in CharacterObject.All)
        {
            if (c == null || c.IsHero) continue;
            if (c.Tier == targetTier && c.IsMounted == mounted) return c;
        }
        foreach (var c in CharacterObject.All)
            if (c != null && !c.IsHero) return c;
        return null;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded. 注意 `Helpers` namespace 在 vanilla TaleWorlds.CampaignSystem.dll 中定义。

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Evaluators/GarrisonPowerEvaluator.cs
git commit -m "feat(eval): add GarrisonPowerEvaluator (vanilla strength + low-tier fraction + AI default)"
```

---

## Task 3: `AccountInFlight` 把 Sally 计入驻军

**Files:**
- Modify: `SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:281-336`

- [ ] **Step 1: 加 Sally 分支到 AccountInFlight**

定位 [SupplyDemandGraph.cs:281-336](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:281) 的 `AccountInFlight`。在 Recruiter 分支后追加 Sally 分支：

```csharp
// 在 `else if (party.PartyComponent is StRecruiterPartyComponent recruiter) { ... }` 后追加
else if (party.PartyComponent is StSallyPartyComponent sally)
{
    // Sally 是"短命瞬时部队"，战后无条件回 home — 计入 home 视为驻军在途回流。
    // 巡逻队不计（StPatrolPartyComponent 长期在外，按 mod 设计语义"已脱离驻军"）。
    var home = sally.HomeSettlementOrNull;
    if (home != null && bySettlement.TryGetValue(home, out var homeState))
        AddInbound(homeState, buckets);
}
```

完整修改后 foreach 体如下：

```csharp
foreach (var party in parties)
{
    if (party == null || !party.IsActive) continue;
    var partyClan = ResolvePartyClan(party);
    if (partyClan == null || partyClan != ownerClan) continue;

    var buckets = MatchPolicy.Bucketize(party.MemberRoster);
    if (buckets.Count == 0) continue;

    if (party.PartyComponent is StTransferPartyComponent transfer)
    {
        var source = transfer.Source;
        var destination = transfer.Destination;
        var target = party.TargetSettlement;

        if (source != null
            && destination != null
            && target == source
            && bySettlement.TryGetValue(source, out var returningSource))
        {
            AddInbound(returningSource, buckets);
            continue;
        }

        if (destination != null && bySettlement.TryGetValue(destination, out var destinationState))
            AddInbound(destinationState, buckets);
    }
    else if (party.PartyComponent is StRecruiterPartyComponent recruiter)
    {
        var home = recruiter.HomeSettlementOrNull;
        if (home != null && bySettlement.TryGetValue(home, out var homeState))
            AddInbound(homeState, buckets);
    }
    else if (party.PartyComponent is StSallyPartyComponent sally)
    {
        var home = sally.HomeSettlementOrNull;
        if (home != null && bySettlement.TryGetValue(home, out var homeState))
            AddInbound(homeState, buckets);
    }
}
```

`ResolvePartyClan` 也需要识别 sally 以避免外层 partyClan 判定漏掉 sally（当 `party.ActualClan == null` 但 home 存在时）：

```csharp
private static Clan? ResolvePartyClan(MobileParty party)
{
    try
    {
        if (party.ActualClan != null) return party.ActualClan;
        if (party.PartyComponent is StTransferPartyComponent transfer) return transfer.Source?.OwnerClan;
        if (party.PartyComponent is StRecruiterPartyComponent recruiter) return recruiter.HomeSettlementOrNull?.OwnerClan;
        if (party.PartyComponent is StSallyPartyComponent sally) return sally.HomeSettlementOrNull?.OwnerClan;
    }
    catch
    {
        return null;
    }
    return null;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Algorithm/SupplyDemandGraph.cs
git commit -m "feat(mcmf): account StSallyPartyComponent in flight (count toward home garrison)"
```

---

## Task 4: `GlobalConfig` 加 `BranchDefaults`，删 `PerSettlementOverrides`

**Files:**
- Modify: `SovereignTowns/src/Configuration/GlobalConfig.cs:17-21,59-69`

- [ ] **Step 1: GlobalConfig 字段改造**

修改 [GlobalConfig.cs:17-21](SovereignTowns/src/Configuration/GlobalConfig.cs:17)：

```csharp
    /// <summary>首府（capital town，由 CapitalRegistry 标记）的驻军规则。模板 / 比例 / Tier 等所有字段。</summary>
    public TownGarrisonRule GlobalDefaults { get; set; } = TownGarrisonRule.CreateDefault();

    /// <summary>所有非首府（branch town / castle）共享的极简规则。
    /// 玩家氏族用此字段的 TargetPower；AI 氏族走 GarrisonPowerEvaluator.ComputeAiVanillaTargetPower。</summary>
    public BranchRule BranchDefaults { get; set; } = BranchRule.CreateDefault();
```

**删除**原 21 行 `PerSettlementOverrides` 整字段。

修改 `CreateDefault` ([GlobalConfig.cs:59-69](SovereignTowns/src/Configuration/GlobalConfig.cs:59))：

```csharp
    public static GlobalConfig CreateDefault() => new GlobalConfig
    {
        ConfigVersion = ConfigurationManager.CurrentConfigVersion,
        LastModified = "",
        GlobalDefaults = TownGarrisonRule.CreateDefault(),
        BranchDefaults = BranchRule.CreateDefault(),
        EnabledFeatures = new EnabledFeatures(),
        ClanPatrol = new ClanPatrolConfig(),
        ClanRecruiter = new ClanRecruiterConfig(),
        Thresholds = new PartyThresholds(),
    };
```

- [ ] **Step 2: Build (会暂时失败 — ConfigurationManager / WebConfigEndpoints 仍引用 PerSettlementOverrides)**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 编译错误，引用 `PerSettlementOverrides` 的位置（`ConfigurationManager.cs`、`WebConfigEndpoints.cs`）报"does not contain a definition for 'PerSettlementOverrides'"。下一 task 修这些。**先不 commit**，留到 Task 5 一并 commit。

---

## Task 5: `ConfigurationManager` 清理 + bump ConfigVersion

**Files:**
- Modify: `SovereignTowns/src/Configuration/ConfigurationManager.cs:27,146,169-221,531-538,662-708`

- [ ] **Step 1: bump 版本号**

[ConfigurationManager.cs:27](SovereignTowns/src/Configuration/ConfigurationManager.cs:27):

```csharp
    public const int CurrentConfigVersion = 19;
```

- [ ] **Step 2: 删 PerSettlementOverrides 在 GetRuleFor / BuildBaseRuleFor / ApplySettlementOverrideFields 中的整条路径**

[ConfigurationManager.cs:169-221](SovereignTowns/src/Configuration/ConfigurationManager.cs:169) 整段替换为：

```csharp
    /// <summary>
    /// 为某 Town 取首府规则（CapitalRule）。仅供首府路径调用 —
    /// 非首府请用 <see cref="GetBranchRuleFor"/>。
    /// AI 城 + ApplyToAiSettlementsToo=true 走 <see cref="AiCulturePresets"/>，否则走 GlobalDefaults。
    /// </summary>
    public static TownGarrisonRule GetRuleFor(Town town)
    {
        try
        {
            lock (_gate)
            {
                if (town?.OwnerClan != null
                    && town.OwnerClan != Clan.PlayerClan
                    && _current.EnabledFeatures?.ApplyToAiSettlementsToo == true)
                {
                    var preset = AiCulturePresets.TryGet(town.OwnerClan.Culture?.StringId);
                    if (preset != null) return preset.Clone();
                }

                return (_current.GlobalDefaults ?? TownGarrisonRule.CreateDefault()).Clone();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetRuleFor failed; returning a fresh default rule", ex);
            return TownGarrisonRule.CreateDefault();
        }
    }

    /// <summary>
    /// 为某 Town 取非首府规则（BranchRule）。
    /// 玩家氏族返回 <see cref="GlobalConfig.BranchDefaults"/>。
    /// AI 氏族（启用 ApplyToAiSettlementsToo 时）调 vanilla 公式动态算 TargetPower；
    /// LowTierMinFraction 沿用全局 BranchDefaults。
    /// </summary>
    public static BranchRule GetBranchRuleFor(Town town)
    {
        try
        {
            lock (_gate)
            {
                var rule = (_current.BranchDefaults ?? BranchRule.CreateDefault()).Clone();

                if (town?.OwnerClan != null
                    && town.OwnerClan != Clan.PlayerClan
                    && _current.EnabledFeatures?.ApplyToAiSettlementsToo == true)
                {
                    int aiTarget = SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeAiVanillaTargetPower(town);
                    if (aiTarget > 0) rule.TargetPower = aiTarget;
                }

                return rule;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("GetBranchRuleFor failed; returning a fresh default branch rule", ex);
            return BranchRule.CreateDefault();
        }
    }
```

**删除**原 `BuildBaseRuleFor` 与 `ApplySettlementOverrideFields` 两个 private 方法（已被新 `GetRuleFor` 内联覆盖）。

- [ ] **Step 3: TryLoadFromDisk 兜底删 PerSettlementOverrides，加 BranchDefaults**

[ConfigurationManager.cs:531-538](SovereignTowns/src/Configuration/ConfigurationManager.cs:531) 改为：

```csharp
            parsed.GlobalDefaults ??= TownGarrisonRule.CreateDefault();
            parsed.BranchDefaults ??= BranchRule.CreateDefault();
            parsed.EnabledFeatures ??= new EnabledFeatures();
            parsed.ClanPatrol ??= new ClanPatrolConfig();
            parsed.ClanRecruiter ??= new ClanRecruiterConfig();
            parsed.Thresholds ??= new PartyThresholds();
            parsed.LastModified ??= "";
```

- [ ] **Step 4: ValidateConfig 简化 — 删 PerSettlementOverrides 遍历、加 BranchRule 校验**

[ConfigurationManager.cs:662-708](SovereignTowns/src/Configuration/ConfigurationManager.cs:662) 的 `ValidateConfig` 中：

**删除**：

```csharp
        foreach (var kv in config.PerSettlementOverrides)
        {
            if (kv.Value is null)
            {
                reason = $"PerSettlementOverrides['{kv.Key}'] is null";
                return false;
            }
            if (!ValidateRule(kv.Value, $"PerSettlementOverrides['{kv.Key}']", out reason))
            {
                return false;
            }
        }
```

**新增**（紧跟 `ValidateRule(config.GlobalDefaults, ...)` 之后）：

```csharp
        if (config.BranchDefaults is null)
        {
            reason = "BranchDefaults is null";
            return false;
        }
        if (!ValidateBranchRule(config.BranchDefaults, "BranchDefaults", out reason))
        {
            return false;
        }
```

在文件末尾、`ValidateRatio` 之前加 helper：

```csharp
    private static bool ValidateBranchRule(BranchRule rule, string ctx, out string reason)
    {
        if (rule.TargetPower < 0)
        { reason = $"{ctx}.TargetPower < 0"; return false; }
        if (rule.TargetPower > 100_000)
        { reason = $"{ctx}.TargetPower {rule.TargetPower} 超过上限 100000"; return false; }
        if (!IsRatio(rule.LowTierMinFraction))
        { reason = $"{ctx}.LowTierMinFraction {rule.LowTierMinFraction} 必须在 [0,1]"; return false; }
        reason = "";
        return true;
    }
```

- [ ] **Step 5: log 行删 overrides 计数引用**

[ConfigurationManager.cs:146](SovereignTowns/src/Configuration/ConfigurationManager.cs:146)：

```csharp
                        Logger.Info($"Config loaded: version={_current.ConfigVersion}");
```

[ConfigurationManager.cs:430,432](SovereignTowns/src/Configuration/ConfigurationManager.cs:430)：

```csharp
                if (changed)
                    Logger.Info($"Config reloaded: version={_current.ConfigVersion} (content changed → will broadcast OnConfigChanged)");
                else
                    Logger.Info($"Config reloaded: version={_current.ConfigVersion} (content identical → no broadcast)");
```

- [ ] **Step 6: Build (仍会失败 — WebConfigEndpoints / WebConfigServer 仍引用 PerSettlementOverrides)**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: 仅剩 WebConfig 相关编译错误。下一 task 修。

---

## Task 6: `WebConfigEndpoints` 清理

**Files:**
- Modify: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:110-117,260-265`

- [ ] **Step 1: PutConfig 兜底字段更新**

[WebConfigEndpoints.cs:111-117](SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:111) 改为：

```csharp
            parsed.GlobalDefaults ??= TownGarrisonRule.CreateDefault();
            parsed.BranchDefaults ??= BranchRule.CreateDefault();
            parsed.EnabledFeatures ??= new EnabledFeatures();
            parsed.ClanPatrol ??= new ClanPatrolConfig();
            parsed.ClanRecruiter ??= new ClanRecruiterConfig();
            parsed.Thresholds ??= new PartyThresholds();
            parsed.LastModified ??= "";
```

- [ ] **Step 2: healthcheck 字段改名**

定位 [WebConfigEndpoints.cs:263](SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:263) 的 `perSettlementOverrideCount = cfg.PerSettlementOverrides.Count` 行，改为：

```csharp
                branchTargetPower = cfg.BranchDefaults?.TargetPower ?? 0,
```

- [ ] **Step 3: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 4: Commit Task 4 + 5 + 6 一起**

```bash
git add SovereignTowns/src/Configuration/GlobalConfig.cs \
        SovereignTowns/src/Configuration/ConfigurationManager.cs \
        SovereignTowns/src/WebConfig/WebConfigEndpoints.cs
git commit -m "refactor(config): split GlobalDefaults into capital + BranchDefaults; drop PerSettlementOverrides (v19)"
```

---

## Task 7: `SupplyDemandGraph` 改为非对称图（首府按 role，非首府按 power）

**Files:**
- Modify: `SovereignTowns/src/Algorithm/SupplyDemandGraph.cs` 整体

这是本计划的核心 task。先列改动点，再分步实施。

**改动总览**：
- `SettlementState` 新增 `BranchRule? BranchRule` 字段（非首府用），原 `Rule`（TownGarrisonRule）仅首府用
- `BuildSettlementStates` 对每座 town 判定 isCapital 决定加载哪种规则
- `RunInternal` 中"对每个 settlement 按 4 role 计算 demand"的循环对**非首府**改为：
  - 单一 power demand：`max(0, TargetPower - CurrentPower - InboundPower)`
  - 单一 low-tier 头数缺口 demand：当 LowTierFraction < min 时，把缺额头数也注入 demand 节点
- 非首府 surplus（power 超出 TargetPower）作为 Garrison source — 与当前一致
- 首府路径保持现状

- [ ] **Step 1: 给 `SettlementState` 加 BranchRule 字段 + InboundPower 累计**

把 `SettlementState` 类 ([SupplyDemandGraph.cs:51-95](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:51)) 改成：

```csharp
    private sealed class SettlementState
    {
        public SettlementState(
            Town town, Settlement settlement,
            TownGarrisonRule? capitalRule, BranchRule? branchRule,
            int desiredTotal, int desiredPower, bool isCapital)
        {
            Town = town;
            Settlement = settlement;
            CapitalRule = capitalRule;
            BranchRule = branchRule;
            DesiredTotal = desiredTotal;
            DesiredPower = desiredPower;
            IsCapital = isCapital;
            Buckets = MatchPolicy.Bucketize(town.GarrisonParty?.MemberRoster);
            Inbound = new Dictionary<GenericTroopRole, int>();
            InboundPower = 0f;
            InboundHeadCount = 0;
            InboundLowTierHeadCount = 0;
        }

        public Town Town { get; }
        public Settlement Settlement { get; }
        public TownGarrisonRule? CapitalRule { get; }
        public BranchRule? BranchRule { get; }
        public int DesiredTotal { get; }
        public int DesiredPower { get; }
        public bool IsCapital { get; }
        public List<TroopBucket> Buckets { get; }
        public Dictionary<GenericTroopRole, int> Inbound { get; }
        public float InboundPower { get; private set; }
        public int InboundHeadCount { get; private set; }
        public int InboundLowTierHeadCount { get; private set; }

        public int Current(GenericTroopRole role)
            => Buckets.Where(b => b.Role == role).Sum(b => b.Count);

        public int Projected(GenericTroopRole role)
            => Math.Max(0, Current(role) + Count(Inbound, role));

        public int Available(GenericTroopRole role) => Current(role);

        public float CurrentPower
            => SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeRosterPower(Town.GarrisonParty?.MemberRoster);

        public float ProjectedPower => CurrentPower + InboundPower;

        public int CurrentHeadCount => Town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
        public int CurrentLowTierHeadCount
        {
            get
            {
                var roster = Town.GarrisonParty?.MemberRoster;
                if (roster == null) return 0;
                int sum = 0;
                for (int i = 0; i < roster.Count; i++)
                {
                    var el = roster.GetElementCopyAtIndex(i);
                    if (el.Character == null || el.Character.IsHero) continue;
                    if (el.Character.Tier <= SovereignTowns.Evaluators.GarrisonPowerEvaluator.LowTierMaxInclusive)
                        sum += el.Number;
                }
                return sum;
            }
        }

        public void AddInbound(GenericTroopRole role, int count)
            => AddCount(Inbound, role, count);

        public void AddInboundPower(float power, int totalHeads, int lowTierHeads)
        {
            if (power > 0f) InboundPower += power;
            if (totalHeads > 0) InboundHeadCount += totalHeads;
            if (lowTierHeads > 0) InboundLowTierHeadCount += lowTierHeads;
        }

        private static int Count(IReadOnlyDictionary<GenericTroopRole, int> values, GenericTroopRole role)
            => values.TryGetValue(role, out var count) ? count : 0;

        private static void AddCount(Dictionary<GenericTroopRole, int> values, GenericTroopRole role, int count)
        {
            if (role == GenericTroopRole.Unknown || count <= 0) return;
            values[role] = Count(values, role) + count;
        }

        public TroopBucket? Bucket(GenericTroopRole role)
            => Buckets.FirstOrDefault(b => b.Role == role && b.Count > 0);
    }
```

- [ ] **Step 2: `BuildSettlementStates` 用 GetRuleFor / GetBranchRuleFor 分流**

[SupplyDemandGraph.cs:262-279](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:262) 整段替换为：

```csharp
    private static List<SettlementState> BuildSettlementStates(CapitalManager manager, Settlement capitalSettlement)
    {
        var result = new List<SettlementState>();
        foreach (var town in Town.AllTowns)
        {
            if (town == null) continue;
            if (!(town.IsTown || town.IsCastle)) continue;
            if (town.OwnerClan == null || town.OwnerClan != manager.OwnerClan) continue;

            var settlement = town.Settlement;
            if (settlement == null || !settlement.IsActive) continue;

            bool isCapital = settlement == capitalSettlement;
            if (isCapital)
            {
                var rule = ConfigurationManager.GetRuleFor(town) ?? TownGarrisonRule.CreateDefault();
                int desired = ComputeDesiredTarget(rule, RiskAssessmentService.Assess(settlement));
                result.Add(new SettlementState(town, settlement, capitalRule: rule, branchRule: null,
                    desiredTotal: desired, desiredPower: 0, isCapital: true));
            }
            else
            {
                var branch = ConfigurationManager.GetBranchRuleFor(town) ?? BranchRule.CreateDefault();
                result.Add(new SettlementState(town, settlement, capitalRule: null, branchRule: branch,
                    desiredTotal: 0, desiredPower: branch.TargetPower, isCapital: false));
            }
        }
        return result;
    }
```

- [ ] **Step 3: `AccountInFlight` 同时累计 InboundPower + 头数（非首府用）**

紧跟 Task 3 那段 `AddInbound(homeState, buckets)` 调用之外，扩展 `AddInbound` 把 power 与头数也累计上来。`AddInbound` 改成：

```csharp
    private static void AddInbound(SettlementState state, IEnumerable<TroopBucket> buckets)
    {
        foreach (var bucket in buckets)
            state.AddInbound(bucket.Role, bucket.Count);
    }

    private static void AddInboundWithPower(SettlementState state, MobileParty party)
    {
        var roster = party?.MemberRoster;
        if (roster == null) return;

        var buckets = MatchPolicy.Bucketize(roster);
        foreach (var bucket in buckets)
            state.AddInbound(bucket.Role, bucket.Count);

        float power = SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeRosterPower(roster);
        int totalHeads = roster.TotalManCount;
        int lowTierHeads = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var el = roster.GetElementCopyAtIndex(i);
            if (el.Character == null || el.Character.IsHero) continue;
            if (el.Character.Tier <= SovereignTowns.Evaluators.GarrisonPowerEvaluator.LowTierMaxInclusive)
                lowTierHeads += el.Number;
        }
        state.AddInboundPower(power, totalHeads, lowTierHeads);
    }
```

把 `AccountInFlight` 内 4 处 `AddInbound(returningSource, buckets)` / `AddInbound(destinationState, buckets)` / `AddInbound(homeState, buckets)`（Recruiter + 上一 task 新增的 Sally）改为 `AddInboundWithPower(<state>, party)`。Transfer 的两个分支的 `AddInbound(returningSource, buckets)` / `AddInbound(destinationState, buckets)` 同样改。**保留** `AddInbound(SettlementState, IEnumerable<TroopBucket>)` 重载供 capital 路径需要 — 但实际不再被 AccountInFlight 直接调用，可保留作公共 helper。

- [ ] **Step 4: RunInternal 中 demand 节点拆首府 / 非首府**

[SupplyDemandGraph.cs:180-220](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:180) 整块 demand 构造改为：

```csharp
        foreach (var state in states)
        {
            if (state.IsCapital)
            {
                foreach (var role in MatchPolicy.Roles)
                {
                    int desired = MatchPolicy.DesiredCount(state.CapitalRule!, role, state.DesiredTotal);
                    int current = state.Projected(role);
                    int demand = Math.Max(0, desired - current);
                    if (demand <= 0) continue;

                    int demandNode = nextNodeId++;
                    var def = new DemandDef(demandNode, state, role, desired, current, demand, isRecruitmentStockpile: false);
                    demands[demandNode] = def;
                    graph.AddEdge(demandNode, superSink, demand, 0);
                }
            }
            else
            {
                int demand = Math.Max(0, state.DesiredPower - (int)Math.Round(state.ProjectedPower));
                if (demand <= 0) continue;

                // 非首府用 GenericTroopRole.Infantry 作为"占位 role"，仅为复用图节点结构；
                // CanConnect 里所有 source role 都能连到 branch demand（详见 Step 5）。
                int demandNode = nextNodeId++;
                var def = new DemandDef(demandNode, state, GenericTroopRole.Infantry, state.DesiredPower, (int)Math.Round(state.ProjectedPower), demand, isRecruitmentStockpile: false);
                demands[demandNode] = def;
                graph.AddEdge(demandNode, superSink, demand, 0);
            }
        }

        // 首府"招募囤兵"需求：把所有 branch 缺口的 role 总和注入 capital 招募 stockpile。
        // 非首府的 demand 已经记到 totalBranchDemand 里，这里只对 capital 仍然作为兵源囤兵。
        if (autoRecruitmentEnabled && capitalState != null && !capitalState.Settlement.IsUnderSiege)
        {
            int totalBranchDemand = states
                .Where(s => !s.IsCapital)
                .Sum(s => Math.Max(0, s.DesiredPower - (int)Math.Round(s.ProjectedPower)));
            if (totalBranchDemand > 0)
            {
                foreach (var role in MatchPolicy.Roles)
                {
                    if (!MatchPolicy.AllowsRole(capitalState.CapitalRule!, role)) continue;
                    int demandNode = nextNodeId++;
                    var def = new DemandDef(
                        demandNode,
                        capitalState,
                        role,
                        desired: totalBranchDemand,
                        current: 0,
                        demand: totalBranchDemand,
                        isRecruitmentStockpile: true);
                    demands[demandNode] = def;
                    graph.AddEdge(demandNode, superSink, totalBranchDemand, 0);
                }
            }
        }
```

**删除**原 `branchDemandByRole` Dictionary 累加逻辑（已被新结构吸收）。

- [ ] **Step 5: `CanConnect` 允许任意 role source 连到 branch demand**

[SupplyDemandGraph.cs:473-495](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:473) 改为：

```csharp
    private static bool CanConnect(SourceDef source, DemandDef demand)
    {
        if (source.Settlement.IsUnderSiege || demand.State.Settlement.IsUnderSiege) return false;

        // 非首府 demand：任意兵种皆可补，但 InPlace/Village 只能补本城（招募官只回首府）。
        if (!demand.State.IsCapital)
        {
            switch (source.Kind)
            {
                case SourceKind.InPlace:
                case SourceKind.Village:
                    return source.Settlement == demand.State.Settlement;
                case SourceKind.Garrison:
                    return source.Settlement != demand.State.Settlement;
                default:
                    return false;
            }
        }

        // 首府路径（含 capital 招募 stockpile）保持原行为
        if (source.Bucket.Role != demand.Role) return false;

        if (demand.IsRecruitmentStockpile)
        {
            return demand.State.IsCapital
                && source.Settlement == demand.State.Settlement
                && (source.Kind == SourceKind.InPlace || source.Kind == SourceKind.Village);
        }

        switch (source.Kind)
        {
            case SourceKind.InPlace:
            case SourceKind.Village:
                return source.Settlement == demand.State.Settlement;
            case SourceKind.Garrison:
                return source.Settlement != demand.State.Settlement;
            default:
                return false;
        }
    }
```

- [ ] **Step 6: `AddRosterSurplusSources` 对非首府改为按 power 超额抽兵**

[SupplyDemandGraph.cs:367-383](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:367) 改为：

```csharp
    private static void AddRosterSurplusSources(
        MinCostFlow graph,
        Dictionary<int, SourceDef> sources,
        ref int nextNodeId,
        int superSource,
        SettlementState state)
    {
        if (state.IsCapital)
        {
            // 首府：原行为 — 对每个 role 算 desired vs available 的超额头数
            foreach (var bucket in state.Buckets)
            {
                int desired = MatchPolicy.DesiredCount(state.CapitalRule!, bucket.Role, state.DesiredTotal);
                int surplus = Math.Max(0, state.Available(bucket.Role) - desired);
                if (surplus <= 0) continue;

                var sourceBucket = new TroopBucket(bucket.Role, surplus, bucket.MinTier, bucket.Representative);
                AddSource(graph, sources, ref nextNodeId, superSource, SourceKind.Garrison, state.Settlement, state.Town, sourceBucket);
            }
            return;
        }

        // 非首府：把整城驻军挂为"可抽兵源"，但每桶上限是 TotalCount —
        // (TotalPower / TargetPower) - 1) × 该桶头数（按比例匀分超额）。
        // 简化做法：power 超过 TargetPower 时，每桶可抽 0..bucket.Count 头数，
        // 实际抽多少由 MinCostFlow 解出来。
        float currentPower = state.CurrentPower;
        if (currentPower <= state.DesiredPower) return;

        float overshootRatio = (currentPower - state.DesiredPower) / Math.Max(1f, currentPower);
        foreach (var bucket in state.Buckets)
        {
            int abstractable = Math.Max(0, (int)Math.Round(bucket.Count * overshootRatio));
            if (abstractable <= 0) continue;

            var sourceBucket = new TroopBucket(bucket.Role, abstractable, bucket.MinTier, bucket.Representative);
            AddSource(graph, sources, ref nextNodeId, superSource, SourceKind.Garrison, state.Settlement, state.Town, sourceBucket);
        }
    }
```

- [ ] **Step 7: `Cost` 简化对 branch demand 的 penalty 计算**

[SupplyDemandGraph.cs:497-514](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:497) 改为：

```csharp
    private static int Cost(SourceDef source, DemandDef demand)
    {
        int overhead = source.Kind switch
        {
            SourceKind.Village => Thresholds.McmfRecruiterOverhead,
            SourceKind.Garrison => Thresholds.McmfTransferOverhead,
            _ => 0
        };

        // 非首府 demand：不考虑 tier 不符的硬罚，因 branch 是黑箱（任意 role 都接受）。
        // 仅按距离 + overhead 计 cost。
        int penalty = demand.State.IsCapital
            ? MatchPolicy.MatchPenalty(source.Bucket, demand.State.CapitalRule!, Thresholds.McmfHardPenalty, Thresholds.McmfTierPenalty)
            : 0;
        float distance = source.Kind == SourceKind.Garrison
            ? Distance(source.Settlement, demand.State.Settlement)
            : 0f;
        return MatchPolicy.EdgeCost(distance, overhead, penalty, demand.DeficitRatio, Thresholds.McmfLeniency);
    }
```

- [ ] **Step 8: `Decode` 给 branch in-place / branch transfer 加正确 instruction**

`Decode` ([SupplyDemandGraph.cs:522-562](SovereignTowns/src/Algorithm/SupplyDemandGraph.cs:522)) 已经正确：会把 `SourceKind.InPlace` 翻译成 `InPlaceRecruitInstruction`，`SourceKind.Garrison` 翻译成 `TransferPartyInstruction`，无需改。但注意：非首府 InPlace 招的"role"是 `GenericTroopRole.Infantry` 占位（Step 4 的设计），需在 `CapitalLogisticsManager.ExecuteInPlaceRecruitment` 里识别非首府并走"按 power"路径而非按 role。这交给 Task 8。

- [ ] **Step 9: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded. 若 `MobileParty` 在 `AddInboundWithPower` 处提示 namespace 缺失，确认顶部 `using TaleWorlds.CampaignSystem.Party;` 已包含。

- [ ] **Step 10: Commit**

```bash
git add SovereignTowns/src/Algorithm/SupplyDemandGraph.cs
git commit -m "refactor(mcmf): capital uses role demand, branch uses power demand; sally counted in flight"
```

---

## Task 8: 非首府 in-place 招募路径 `BranchInPlaceRecruiter`

**Files:**
- Create: `SovereignTowns/src/Recruitment/BranchInPlaceRecruiter.cs`
- Modify: `SovereignTowns/src/Managers/CapitalLogisticsManager.cs:197-215`

- [ ] **Step 1: Create BranchInPlaceRecruiter**

```csharp
using System;
using System.Linq;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Evaluators;
using SovereignTowns.Recruitment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Recruitment;

/// <summary>
/// 非首府（branch town / castle）"本城招募"。语义与 CapitalInPlaceRecruiter 类似但更简：
///   - 不按 role 配额、不按模板兵种过滤、不参与升级
///   - 当 LowTierHeadCountFraction 低于 BranchRule.LowTierMinFraction → **优先**招 tier 最低的志愿者
///   - 否则按 tier 高优先（更高单兵 power）
///   - power 缺口已达成 → no-op
/// 全方法 try-catch。
/// </summary>
public static class BranchInPlaceRecruiter
{
    public static int RecruitFromBranchNotables(Settlement? branch, int desiredPower, string reason = "")
    {
        int recruited = 0;
        try
        {
            if (branch == null || (!branch.IsTown && !branch.IsCastle)) return 0;
            var registry = SovereignTowns.Capital.CapitalRegistry.Instance;
            if (registry != null && !registry.IsManagedClan(branch.OwnerClan)) return 0;
            if (branch.IsUnderSiege) return 0;
            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment) return 0;

            var town = branch.Town;
            if (town == null) return 0;
            var garrison = town.GarrisonParty;
            if (garrison == null)
            {
                try { branch.AddGarrisonParty(); garrison = town.GarrisonParty; }
                catch (Exception ex) { Logger.Error($"BranchInPlace '{branch.Name}': AddGarrisonParty 失败", ex); return 0; }
            }
            if (garrison?.MemberRoster == null) return 0;

            var memberRoster = garrison.MemberRoster;
            int partySizeLimit = garrison.Party?.PartySizeLimit ?? int.MaxValue;
            if (memberRoster.TotalManCount >= partySizeLimit) return 0;

            var branchRule = ConfigurationManager.GetBranchRuleFor(town) ?? BranchRule.CreateDefault();
            float currentPower = GarrisonPowerEvaluator.ComputeRosterPower(memberRoster);
            if (currentPower >= desiredPower) return 0;

            var ownerHero = branch.OwnerClan?.Leader;
            if (ownerHero == null) return 0;
            var volunteerModel = Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) return 0;

            // 决定本轮策略：低 tier 不足 → 优先低 tier；否则优先高 tier。
            bool prioritizeLowTier =
                GarrisonPowerEvaluator.LowTierHeadCountFraction(memberRoster) < branchRule.LowTierMinFraction;

            var notables = branch.Notables;
            if (notables == null) return 0;

            // 收集所有合法候选（troop, notable, slotIdx）按 tier 排序
            var candidates = new System.Collections.Generic.List<(CharacterObject Troop, Hero Notable, int Idx)>();
            foreach (var notable in notables)
            {
                if (notable == null || !notable.CanHaveRecruits) continue;
                var slots = notable.VolunteerTypes;
                if (slots == null) continue;

                int maxIdx;
                try
                {
                    using (StRecruitContext.Enter())
                    {
                        maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
                    }
                }
                catch { continue; }
                if (maxIdx < 0) continue;

                int upper = Math.Min(slots.Length - 1, maxIdx);
                for (int i = 0; i <= upper; i++)
                {
                    var t = slots[i];
                    if (t == null) continue;
                    candidates.Add((t, notable, i));
                }
            }

            candidates.Sort((a, b) => prioritizeLowTier
                ? a.Troop.Tier.CompareTo(b.Troop.Tier)
                : b.Troop.Tier.CompareTo(a.Troop.Tier));

            foreach (var (troop, notable, idx) in candidates)
            {
                if (memberRoster.TotalManCount + 1 > partySizeLimit) break;
                if (GarrisonPowerEvaluator.ComputeRosterPower(memberRoster) >= desiredPower) break;

                bool shouldCharge = SovereignTowns.Capital.CapitalRegistry.ShouldChargeClan(branch.OwnerClan);
                if (shouldCharge && !ModTreasury.CanAfford(5)) break;
                if (shouldCharge && !ModTreasury.Charge(ExpenseCategory.RecruiterWage, 5, $"branch_in_place branch={branch.StringId} troop={troop.StringId}")) break;

                try { memberRoster.AddToCounts(troop, 1, false, 0, 0); }
                catch (Exception ex)
                {
                    Logger.Warn($"BranchInPlace '{branch.Name}': AddToCounts threw for '{troop.StringId}': {ex.Message}");
                    continue;
                }

                if (idx >= 0 && idx < notable.VolunteerTypes.Length) notable.VolunteerTypes[idx] = null;
                recruited++;
            }

            Logger.Info($"BranchInPlace '{branch.Name}': recruited={recruited} desiredPower={desiredPower} currentPower={currentPower:F1} → {GarrisonPowerEvaluator.ComputeRosterPower(memberRoster):F1} priorityLowTier={prioritizeLowTier} reason='{reason}'");
        }
        catch (Exception ex)
        {
            Logger.Error("BranchInPlaceRecruiter.RecruitFromBranchNotables failed", ex);
        }
        return recruited;
    }
}
```

- [ ] **Step 2: Wire CapitalLogisticsManager to call BranchInPlaceRecruiter**

[CapitalLogisticsManager.cs:197-215](SovereignTowns/src/Managers/CapitalLogisticsManager.cs:197) 的 `ExecuteInPlaceRecruitment` 改为：

```csharp
    private static bool ExecuteInPlaceRecruitment(InPlaceRecruitInstruction instruction)
    {
        var settlement = instruction.Settlement;
        if (settlement == null) return false;
        var capitalRegistry = SovereignTowns.Capital.CapitalRegistry.Instance;
        bool isCapital = capitalRegistry != null
            && settlement == capitalRegistry.GetCapitalForClan(settlement.OwnerClan);

        if (isCapital)
        {
            var garrison = settlement.Town?.GarrisonParty?.MemberRoster;
            int current = garrison?.TotalManCount ?? 0;
            string reason = $"mcmf in-place role={instruction.Role} count={instruction.Count}";
            int recruited = CapitalInPlaceRecruiter.RecruitFromCapitalNotables(
                settlement,
                current + instruction.Count,
                reason);
            if (recruited > 0)
            {
                Logger.Info($"CapitalLogistics MCMF: capital in-place recruited {recruited} troop(s) settlement='{settlement.Name}' requested={instruction.Count}");
                return true;
            }
            return false;
        }
        else
        {
            var branchRule = ConfigurationManager.GetBranchRuleFor(settlement.Town) ?? BranchRule.CreateDefault();
            int currentPower = (int)Math.Round(GarrisonPowerEvaluator.ComputeRosterPower(settlement.Town?.GarrisonParty?.MemberRoster));
            int desiredPower = currentPower + instruction.Count;  // instruction.Count 即 power deficit
            int recruited = BranchInPlaceRecruiter.RecruitFromBranchNotables(
                settlement,
                Math.Min(desiredPower, branchRule.TargetPower),
                $"mcmf branch in-place delta={instruction.Count}");
            if (recruited > 0)
            {
                Logger.Info($"CapitalLogistics MCMF: branch in-place recruited {recruited} troop(s) settlement='{settlement.Name}' targetPower={desiredPower}");
                return true;
            }
            return false;
        }
    }
```

需要在 CapitalLogisticsManager 顶部 using 加：

```csharp
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
```

- [ ] **Step 3: 确认 `CapitalRegistry.GetCapitalForClan(Clan?)` 签名（已验证存在于 [CapitalRegistry.cs:183](SovereignTowns/src/Capital/CapitalRegistry.cs:183)）**

Run: `grep -n "GetCapitalForClan" SovereignTowns/src/Capital/CapitalRegistry.cs`
Expected: `public Settlement? GetCapitalForClan(Clan? clan)` 一行。无需改 plan 中其他引用。

- [ ] **Step 4: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/Recruitment/BranchInPlaceRecruiter.cs \
        SovereignTowns/src/Managers/CapitalLogisticsManager.cs
git commit -m "feat(recruit): add BranchInPlaceRecruiter (power-based, tier-prioritized)"
```

---

## Task 9: `TroopUpgradeService` 非首府跳过

**Files:**
- Modify: `SovereignTowns/src/Upgrades/TroopUpgradeService.cs:64-80`

- [ ] **Step 1: 加 isCapital 早退**

定位 [TroopUpgradeService.cs:64](SovereignTowns/src/Upgrades/TroopUpgradeService.cs:64) 的 `TryUpgradeGarrison`，在 `if (homeTown?.Settlement == null)` 前加：

```csharp
    public static UpgradeReport TryUpgradeGarrison(
        Town homeTown,
        int budgetCap,
        int maxUpgradesPerCall = 20)
    {
        int upgraded = 0;
        int xpSpent = 0;
        int goldSpent = 0;
        int skipped = 0;

        try
        {
            if (homeTown?.Settlement == null)
            {
                Logger.Debug("TroopUpgradeService: homeTown or Settlement is null, skip");
                return new UpgradeReport(0, 0, 0, 0);
            }

            // 非首府不做升级 — 非首府按 BranchRule 黑箱处理，"够 power" 即可，
            // 升级行为由首府独占。即便有兵进来也保持原 tier，让 power 自然累积。
            var capitalRegistry = SovereignTowns.Capital.CapitalRegistry.Instance;
            if (capitalRegistry != null
                && homeTown.Settlement != capitalRegistry.GetCapitalForClan(homeTown.OwnerClan))
            {
                Logger.Debug($"TroopUpgradeService: '{homeTown.Settlement.Name}' is not a capital — skip upgrade per design");
                return new UpgradeReport(0, 0, 0, 0);
            }

            // 原逻辑继续...
```

（`GetCapitalForClan` 签名见 [CapitalRegistry.cs:183](SovereignTowns/src/Capital/CapitalRegistry.cs:183)，与 Task 8 同一调用形式。）

- [ ] **Step 2: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Upgrades/TroopUpgradeService.cs
git commit -m "feat(upgrade): skip non-capital settlements (branch is black-box per BranchRule)"
```

---

## Task 10: `AiCulturePresets` 文档化

**Files:**
- Modify: `SovereignTowns/src/Configuration/AiCulturePresets.cs` 顶部注释

- [ ] **Step 1: 更新顶层注释明确"仅首府使用"**

定位 `AiCulturePresets` 类的 doc comment（一般在类声明前），在描述中加一句：

```csharp
/// <remarks>
/// AI 氏族非首府不读这里 — 它们的 BranchRule.TargetPower 由
/// <see cref="SovereignTowns.Evaluators.GarrisonPowerEvaluator.ComputeAiVanillaTargetPower"/>
/// 动态算（复用 vanilla FactionHelper 公式），LowTierMinFraction 沿用全局 BranchDefaults。
/// preset 中所有字段只对 AI 首府生效。
/// </remarks>
```

- [ ] **Step 2: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Configuration/AiCulturePresets.cs
git commit -m "docs(ai): note presets are capital-only; branch power uses vanilla formula"
```

---

## Task 11: WebUI 删 per-town override 表单 + 加 BranchDefaults

**Files:**
- Modify: `SovereignTowns/SovereignTowns/WebUI/index.html`

WebUI 是单页 HTML+JS+vanilla DOM。改动两块：
1. 删 `PerSettlementOverrides` 渲染 / 表单 / 保存逻辑
2. 加 `BranchDefaults` 表单（TargetPower 数字输入 + LowTierMinFraction 滑条）

- [ ] **Step 1: 找到 PerSettlementOverrides 处理代码**

Run: `grep -n "PerSettlementOverrides\|perSettlement\|settlementOverride" SovereignTowns/SovereignTowns/WebUI/index.html`
Expected: 多处匹配 — 包括渲染 select / table / Save 时的字段组装。

- [ ] **Step 2: 删除 PerSettlementOverrides UI**

把所有匹配到的代码块整段删除：
- `<select id="settlement-picker">` / 相关 `<table>` / 相关 `renderPerSettlementForm()` JS 函数
- Save handler 中 `cfg.PerSettlementOverrides = ...` 行
- 任何引用 `PerSettlementOverrides` 的 fetchAndRender / mergeIntoCfg

- [ ] **Step 3: 加 BranchDefaults 表单**

在原 `GlobalDefaults` 表单卡片之后，加一段：

```html
<section class="card">
  <h2>非首府驻军（Branch defaults — 城堡 / 非首府城镇通用）</h2>
  <p class="hint">非首府是 mod 内部的黑箱，仅由"目标兵力"和"低 Tier 头数占比下限"约束。
    AI 氏族的目标兵力按 vanilla 公式动态计算（忽略此处的 TargetPower）。</p>
  <div class="row">
    <label>TargetPower（vanilla strength 口径，~150 ≈ 100 个 T3 步兵）</label>
    <input type="number" id="branch-target-power" min="0" max="100000" step="1" value="150" />
  </div>
  <div class="row">
    <label>LowTierMinFraction（T1+T2 头数 ÷ 总头数 必须 ≥ 此值，防"全 T6 几个人")</label>
    <input type="range" id="branch-low-tier-min" min="0" max="1" step="0.01" value="0.20" />
    <span id="branch-low-tier-min-out">0.20</span>
  </div>
</section>
```

- [ ] **Step 4: JS 同步双向绑定**

在 `renderFromCfg(cfg)` 等价的函数里加：

```javascript
document.getElementById('branch-target-power').value = cfg.BranchDefaults?.TargetPower ?? 150;
const lowTierEl = document.getElementById('branch-low-tier-min');
lowTierEl.value = cfg.BranchDefaults?.LowTierMinFraction ?? 0.20;
document.getElementById('branch-low-tier-min-out').textContent = (cfg.BranchDefaults?.LowTierMinFraction ?? 0.20).toFixed(2);
lowTierEl.oninput = (e) => { document.getElementById('branch-low-tier-min-out').textContent = (+e.target.value).toFixed(2); };
```

在 `collectCfg()` 等价函数（构造 PUT body）里加：

```javascript
cfg.BranchDefaults = {
    TargetPower: +document.getElementById('branch-target-power').value,
    LowTierMinFraction: +document.getElementById('branch-low-tier-min').value,
};
```

- [ ] **Step 5: Build (HTML 由 DeployToGame 拷贝)**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: Build succeeded（HTML 不被编译，但需要确保 DeployToGame 把它拷过去）。

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/SovereignTowns/WebUI/index.html
git commit -m "feat(webui): drop per-settlement overrides; add BranchDefaults form (TargetPower + LowTierMinFraction)"
```

---

## Task 12: Playtest 回归 checklist

**Files:**
- Test: 启动游戏 → 看 log + WebUI

- [ ] **Step 1: 全量 build (Release)**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Release`
Expected: Build succeeded, DeployToGame 完成。

- [ ] **Step 2: 删旧配置 + 启动游戏 + 进入存档**

```powershell
Remove-Item "$env:USERPROFILE\Documents\Mount and Blade II Bannerlord\Configs\SovereignTowns\global.json" -ErrorAction SilentlyContinue
Remove-Item "$env:USERPROFILE\Documents\Mount and Blade II Bannerlord\Configs\SovereignTowns\global.json.bak" -ErrorAction SilentlyContinue
```

Run the game with SovereignTowns + dependencies enabled, load an existing playtest save.

- [ ] **Step 3: 检查启动日志**

打开 `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\` 下最新 log。**Expected**：
- `Config file not found ... creating defaults` 或 `Config loaded: version=19`（**不能**出现 `version mismatch (file v18, expected v19)` 之外的版本号；旧 v18 应该被丢弃回默认）
- 首次 `EvaluateAll` 后看到 `GarrisonPowerEvaluator self-test: T1=0.66 T3=1.30 T6=2.56` —— **数值必须接近** vanilla 公式
- `MCMF clan=<id> settlements=N demand=... instructions=...` 仍正常打印

- [ ] **Step 4: 观察 30 分钟游戏内时间，逐项确认**

| 检查项 | 期望行为 | log 关键字 |
|---|---|---|
| 玩家首府按模板招募 | 与原行为一致 | `CapitalInPlace ... recruited=N` |
| 非首府按 power 招募 | `BranchInPlace ... recruited=N priorityLowTier=true/false` | `BranchInPlace` |
| 非首府不升级 | 无 `TroopUpgradeService` 输出 | `is not a capital — skip upgrade` |
| Sally 出击后回家途中不重复派单 | 沙盘观察：派 Sally 后 mod 不立刻补一队 | `MCMF` instruction count 在 Sally 在外时较少 |
| Branch power 超过 Target → Garrison 兵被抽走 | branch garrison 减少、capital / 其他 branch 收到 transfer | `MCMF Transfer src='<branch>' dst='<other>' count=N` |
| AI 氏族 power 目标合理 | `ComputeAiVanillaTargetPower` 返回 50-300 之间值（视繁荣度） | 启用 `VerboseLogging` 后 `[DIAG]` 行可见 |

- [ ] **Step 5: WebUI 检查**

打开 `http://localhost:<port>/` 看 BranchDefaults 字段能加载 / 编辑 / 保存。设 TargetPower=300 并 save，看 30 分钟后非首府兵力是否朝 300 收敛。

- [ ] **Step 6: 如果一切正常，最终 commit playtest 笔记**

```bash
# 若 playtest 中发现需要修的小问题，逐项 fix + commit；最后写一份玩测笔记到 git
git log --oneline -20  # 确认所有 commits 完整
```

---

## Self-Review

**Spec coverage**:
- ✅ 模板/比例只用于首府 — Task 4 + Task 7 Step 2 (GetRuleFor 只对 capital 路径返回) + SupplyDemandGraph 分流（Task 7 Step 4）
- ✅ 非首府"缺就调进、不匹配就推走" — Task 7 Step 5 (CanConnect 允许任意 role 接非首府 demand) + Step 6 (非首府 surplus 整体作 Garrison source)
- ✅ Sally 算驻军 — Task 3
- ✅ Patrol 不算 — 保持现状未修改
- ✅ 删每城覆盖 — Task 4 删字段 + Task 5 删代码路径 + Task 11 删 UI
- ✅ 新增"兵力"概念用 vanilla — Task 2 `GarrisonPowerEvaluator`
- ✅ 仅非首府用此规则 — Task 4 GlobalConfig 字段分离 + ConfigurationManager 二选一返回
- ✅ 低 tier 占比下限（head count）— `GarrisonPowerEvaluator.LowTierHeadCountFraction` + `BranchInPlaceRecruiter` 优先低 tier 分支
- ✅ 非首府不升级 — Task 9
- ✅ 招募官行为不变 — `RecruitmentDispatcher.TryDispatchRecruiter` 未触碰；ExecuteRecruiterDispatch 仅对 capital 路径走
- ✅ AI 用 vanilla 公式 — Task 2 `ComputeAiVanillaTargetPower` + Task 5 `GetBranchRuleFor` 调用

**Placeholder scan**: 已搜索本文档，未发现 "TBD" / "implement later" / "add appropriate error handling" 等。每个 Step 都有完整代码或具体 grep 指令。

**Type consistency**:
- `BranchRule.TargetPower : int`、`BranchRule.LowTierMinFraction : float` 在 Task 1 定义；Task 2 / 4 / 5 / 7 / 8 / 11 引用一致
- `GarrisonPowerEvaluator.ComputeRosterPower(TroopRoster?) : float`、`LowTierHeadCountFraction(TroopRoster?) : float`、`ComputeAiVanillaTargetPower(Town?) : int`、`LowTierMaxInclusive : int` 在 Task 2 定义；Task 5 / 7 / 8 / 9 引用一致
- `ConfigurationManager.GetBranchRuleFor(Town) : BranchRule` 在 Task 5 定义；Task 7 / 8 引用一致
- `BranchInPlaceRecruiter.RecruitFromBranchNotables(Settlement?, int, string) : int` 在 Task 8 定义；CapitalLogisticsManager 调用签名一致
- `SettlementState` 构造签名（Task 7 Step 1）与所有 callsite（Step 2 / 3）一致

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-05-19-capital-only-templates-branch-power.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - 主线程逐任务派 fresh subagent，每个 task 完成后人工审 + 推进下一个。MCMF 重构（Task 7）是核心风险点，分到独立 subagent 上下文里更稳。

**2. Inline Execution** - 在当前会话里按 superpowers:executing-plans 批执行，到关键 checkpoint（Task 7 / Task 12）暂停。

**Which approach?**
