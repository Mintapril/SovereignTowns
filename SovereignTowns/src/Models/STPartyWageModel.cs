using System;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

/// <summary>
/// 为 Mod 自定义的 3 类 MobileParty
/// (<see cref="RecruitingPartyComponent"/> / <see cref="StTransferPartyComponent"/> / <see cref="SallyForthPartyComponent"/>)
/// 强制 clan-wage = 0：这些队伍由 town 出资，不应再扣家族金币。
/// SallyForth 短命 12h 内通常无 wage tick，但跨整点会扣 → 这里强制 0。
/// 其它 party fall-through 到 vanilla <see cref="DefaultPartyWageModel"/>。
///
/// v1.3.15 真实签名（reflection 验证）：
///   <c>public override ExplainedNumber GetTotalWage(MobileParty mobileParty, TroopRoster troopRoster, bool includeDescriptions)</c>
/// 注意：任务描述里写的是 2 参签名，实际是 3 参（多了 troopRoster），以 reflection 为准。
/// </summary>
public sealed class STPartyWageModel : DefaultPartyWageModel
{
    public override ExplainedNumber GetTotalWage(
        MobileParty mobileParty,
        TroopRoster troopRoster,
        bool includeDescriptions)
    {
        try
        {
            var comp = mobileParty?.PartyComponent;
            if (comp is RecruitingPartyComponent
                || comp is StTransferPartyComponent
                || comp is SallyForthPartyComponent)
            {
                return new ExplainedNumber(
                    0f,
                    includeDescriptions,
                    new TextObject("{=ST_PartyWageZero}主权城镇 城镇拨款队伍（免氏族军饷）"));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("STPartyWageModel.GetTotalWage component check failed", ex);
            // fall through to base
        }

        try
        {
            return base.GetTotalWage(mobileParty, troopRoster, includeDescriptions);
        }
        catch (Exception ex)
        {
            Logger.Error("STPartyWageModel.GetTotalWage base call failed", ex);
            return new ExplainedNumber(0f, includeDescriptions, null);
        }
    }
}
