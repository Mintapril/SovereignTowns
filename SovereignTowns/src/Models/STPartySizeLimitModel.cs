using System;
using SovereignTowns.Parties;
using SovereignTowns.SallyForth;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

/// <summary>
/// 为 Mod 自定义的 3 类 MobileParty
/// (<see cref="RecruitingPartyComponent"/> / <see cref="TransferPartyComponent"/> / <see cref="SallyForthPartyComponent"/>)
/// 提供独立的兵员上限；其它 party 一律 fall-through 到 vanilla
/// <see cref="DefaultPartySizeLimitModel"/>。
///
/// v1.3.15 真实签名（reflection 验证）：
///   <c>public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool includeDescriptions = false)</c>
/// </summary>
public sealed class STPartySizeLimitModel : DefaultPartySizeLimitModel
{
    // 各类自定义 party 的上限（loose policy；可以未来挂配置）
    private const int RecruitingPartyLimit = 60;
    private const int TransferPartyLimit   = 80;
    // SallyForth 使用 SallyForthManager.MaxSallyPartySize (=100) 作为单一来源

    public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool includeDescriptions = false)
    {
        try
        {
            // 必须先 null-guard：vanilla 有些 PartyBase 没有 MobileParty（settlement party 等）
            var mp = party?.MobileParty;
            var comp = mp?.PartyComponent;

            if (comp is RecruitingPartyComponent)
            {
                return new ExplainedNumber(
                    RecruitingPartyLimit,
                    includeDescriptions,
                    new TextObject("{=ST_PartySizeLimit_Recruit}主权城镇 征兵队容量上限"));
            }

            if (comp is TransferPartyComponent)
            {
                return new ExplainedNumber(
                    TransferPartyLimit,
                    includeDescriptions,
                    new TextObject("{=ST_PartySizeLimit_Transfer}主权城镇 调拨队容量上限"));
            }

            if (comp is SallyForthPartyComponent)
            {
                return new ExplainedNumber(
                    SallyForthManager.MaxSallyPartySize,
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
}
