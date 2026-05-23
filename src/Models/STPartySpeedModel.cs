using System;
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
/// 提供 +20% 移动速度加成，加快回程减少卡死；其它 party fall-through。
///
/// v1.3.15 真实签名（reflection 验证）：
///   <c>public override ExplainedNumber CalculateFinalSpeed(MobileParty mobileParty, ExplainedNumber finalSpeed)</c>
///
/// 重要：使用 <see cref="ExplainedNumber.AddFactor"/> 叠加 +20%，
/// **不**用 <c>new ExplainedNumber(...)</c> 替换，否则会丢掉 vanilla 所有 modifier
/// （载货 / 坐骑 / 地形 / 技能…）。
/// </summary>
public sealed class STPartySpeedModel : DefaultPartySpeedCalculatingModel
{
    private const float SpeedBonusFactor = 0.2f; // +20%

    public override ExplainedNumber CalculateFinalSpeed(MobileParty mobileParty, ExplainedNumber finalSpeed)
    {
        ExplainedNumber result;
        try
        {
            result = base.CalculateFinalSpeed(mobileParty, finalSpeed);
        }
        catch (Exception ex)
        {
            Logger.Error("STPartySpeedModel base.CalculateFinalSpeed failed", ex);
            return finalSpeed;
        }

        try
        {
            var comp = mobileParty?.PartyComponent;
            // R7：4 种 ST party 行为统一 — Patrol 与其他三种一样享受 +20% 速度，避免巡逻队
            // 应援/回防慢于 sally / transfer 造成的"巡逻队总是赶不上"问题。
            if (comp is StPartyComponent)
            {
                result.AddFactor(
                    SpeedBonusFactor,
                    new TextObject("{=ST_PartySpeedBonus}Sovereign Towns town-funded party speed bonus"));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("STPartySpeedModel speed bonus apply failed", ex);
        }

        return result;
    }
}
