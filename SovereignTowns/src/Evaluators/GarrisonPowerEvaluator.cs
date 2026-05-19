using System;
using Helpers;
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
        var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MilitaryPowerModel;
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
            Logger.Error($"GarrisonPowerEvaluator.ComputeAiVanillaTargetPower threw for town '{town?.Settlement?.StringId}'", ex);
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
            var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MilitaryPowerModel;
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
