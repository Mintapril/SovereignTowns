using System;
using SovereignTowns.Configuration;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

/// <summary>
/// 为 Mod 自定义的 3 类 MobileParty
/// (<see cref="StRecruiterPartyComponent"/> / <see cref="StTransferPartyComponent"/> / <see cref="StSallyPartyComponent"/>)
/// 提供独立的兵员上限；其它 party 一律 fall-through 到 vanilla
/// <see cref="DefaultPartySizeLimitModel"/>。
///
/// v1.3.15 真实签名（reflection 验证）：
///   <c>public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool includeDescriptions = false)</c>
/// </summary>
public sealed class STPartySizeLimitModel : DefaultPartySizeLimitModel
{
    public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool includeDescriptions = false)
    {
        try
        {
            // 必须先 null-guard：vanilla 有些 PartyBase 没有 MobileParty（settlement party 等）
            var mp = party?.MobileParty;
            var comp = mp?.PartyComponent;

            if (comp is StRecruiterPartyComponent)
            {
                return new ExplainedNumber(
                    ComputeRecruiterLimit(mp),
                    includeDescriptions,
                    new TextObject("{=ST_PartySizeLimit_Recruit}主权城镇 征兵队容量上限"));
            }

            if (comp is StTransferPartyComponent transfer)
            {
                return new ExplainedNumber(
                    ComputeTransferLimit(mp, transfer),
                    includeDescriptions,
                    new TextObject("{=ST_PartySizeLimit_Transfer}主权城镇 调拨队容量上限"));
            }

            if (comp is StSallyPartyComponent sally)
            {
                return new ExplainedNumber(
                    ComputeSallyLimit(mp, sally),
                    includeDescriptions,
                    new TextObject("{=ST_PartySizeLimit_Sally}主权城镇 出击队容量上限"));
            }

            return base.GetPartyMemberSizeLimit(party!, includeDescriptions);
        }
        catch (Exception ex)
        {
            Logger.Error("STPartySizeLimitModel.GetPartyMemberSizeLimit failed", ex);
            // 异常时回退 vanilla，保持稳定
            try
            {
                return base.GetPartyMemberSizeLimit(party!, includeDescriptions);
            }
            catch
            {
                return new ExplainedNumber(30f, includeDescriptions, null);
            }
        }
    }

    private static int ComputeRecruiterLimit(MobileParty? party)
    {
        int currentMembers = party?.MemberRoster?.TotalManCount ?? 0;
        var comp = party?.PartyComponent as StRecruiterPartyComponent;
        int recruitedThreshold = Math.Max(1, ConfigurationManager.Current?.Thresholds?.RecruiterReturnRecruitedCount ?? 50);
        int remainingRecruitCapacity = Math.Max(0, recruitedThreshold - (comp?.RecruitedThisTrip ?? 0));
        return Math.Max(1, currentMembers + remainingRecruitCapacity);
    }

    private static int ComputeTransferLimit(MobileParty? party, StTransferPartyComponent transfer)
    {
        int currentMembers = party?.MemberRoster?.TotalManCount ?? 0;
        int baseGarrison = GarrisonThresholdMath.ActualGarrisonCount(transfer.Source) + currentMembers;
        int byRatio = GarrisonThresholdMath.CountFromRatio(
            baseGarrison,
            ConfigurationManager.Current?.Thresholds?.TransferMaxTroopsPerTaskRatio ?? 0.67f,
            minimumWhenPositive: 1);
        return Math.Max(1, Math.Max(currentMembers, byRatio));
    }

    private static int ComputeSallyLimit(MobileParty? party, StSallyPartyComponent sally)
    {
        int currentMembers = party?.MemberRoster?.TotalManCount ?? 0;
        int baseGarrison = GarrisonThresholdMath.ActualGarrisonCount(sally.HomeSettlementOrNull) + currentMembers;
        int byGarrisonRatio = GarrisonThresholdMath.CountFromRatio(
            baseGarrison,
            ConfigurationManager.Current?.Thresholds?.SallyExtractionRatio ?? 0.60f,
            minimumWhenPositive: 1);
        int targetMen = Math.Max(0, sally.TargetParty?.MemberRoster?.TotalManCount ?? 0);
        int byTarget = Math.Max(0, (int)Math.Ceiling(targetMen * (ConfigurationManager.Current?.Thresholds?.SallyTargetPartySizeMultiplier ?? 2.0f)));
        int limit = byTarget > 0 ? Math.Min(byTarget, byGarrisonRatio) : byGarrisonRatio;
        return Math.Max(1, Math.Max(currentMembers, limit));
    }
}
