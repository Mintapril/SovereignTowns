using System;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui;

/// <summary>
/// B7.22：拦截玩家与本 mod 自家队伍（征兵队 / 调拨队 / 出击队 / Patrol）的 encounter。
/// 在 vanilla 对话图的 "start" 根节点上注册一条高优先级对话：
///   - 条件：encountered party 属于本 mod 自家 component 类型
///   - NPC 文本：「[城名] 的 X 队伍向你致意。」
///   - 玩家选项：「祝你顺利。」 → consequence 设 PlayerEncounter.LeaveEncounter=true
/// 这样玩家点击自家 party 时只看到友好寒暄，不会出现"攻击"或"勒索"等敌对选项。
///
/// vanilla 对话图基于 input/output token：
///   - input "start"：encounter 开场
///   - output "close_window"：关闭对话回 map
///   - 中间的 "st_party_greeting_player_choice" 是我们自定的中转 token，让玩家选项接得上
/// </summary>
internal static class STPartyDialogRegistration
{
    public static void Register(CampaignGameStarter starter)
    {
        if (starter == null) return;

        try
        {
            // NPC 致意 — 触发条件：encountered party 是我们自家
            starter.AddDialogLine(
                id: "st_party_greeting_npc",
                inputToken: "start",
                outputToken: "st_party_greeting_player_choice",
                text: "{=!}{ST_PARTY_GREETING}",
                conditionDelegate: new ConversationSentence.OnConditionDelegate(IsOurPartyEncounter),
                consequenceDelegate: null,
                priority: 200);  // 高优先级压过 vanilla 默认 encounter line

            // 玩家选项 — 唯一选项即「祝你顺利」，关闭对话并设 LeaveEncounter=true
            starter.AddPlayerLine(
                id: "st_party_greeting_player_close",
                inputToken: "st_party_greeting_player_choice",
                outputToken: "close_window",
                text: "{=!}祝你顺利。",
                conditionDelegate: null,
                consequenceDelegate: new ConversationSentence.OnConsequenceDelegate(LeaveOurPartyEncounter),
                priority: 100);

            Logger.Info("STPartyDialogRegistration: 已注册自家 party encounter 对话拦截");
        }
        catch (Exception ex)
        {
            Logger.Error("STPartyDialogRegistration.Register failed", ex);
        }
    }

    private static bool IsOurPartyEncounter()
    {
        try
        {
            var encountered = PlayerEncounter.EncounteredParty;
            if (encountered == null) return false;
            var mp = encountered.MobileParty;
            if (mp == null) return false;
            var comp = mp.PartyComponent;
            if (comp is null) return false;
            if (comp is RecruitingPartyComponent
                || comp is StTransferPartyComponent
                || comp is StSallyPartyComponent)
            {
                // 设置 NPC 文本变量（{ST_PARTY_GREETING}）
                string kindZh = comp switch
                {
                    RecruitingPartyComponent => "征兵队",
                    StTransferPartyComponent => "调拨队",
                    StSallyPartyComponent    => "出击队",
                    _                        => "队伍"
                };
                string homeName;
                try { homeName = mp.HomeSettlement?.Name?.ToString() ?? "未知"; }
                catch { homeName = "未知"; }
                string greeting = $"我们是 {homeName} 派出的{kindZh}，向您致意。";
                try { TaleWorlds.Localization.MBTextManager.SetTextVariable("ST_PARTY_GREETING", greeting, false); }
                catch { /* SetTextVariable 偶尔抛 */ }
                return true;
            }
            // vanilla PatrolPartyComponent 也算（玩家自家城的）
            if (comp is PatrolPartyComponent pp && pp.HomeSettlement?.OwnerClan == Clan.PlayerClan)
            {
                string homeName;
                try { homeName = pp.HomeSettlement?.Name?.ToString() ?? "未知"; }
                catch { homeName = "未知"; }
                string greeting = $"我们是 {homeName} 的巡逻队，向您致意。";
                try { TaleWorlds.Localization.MBTextManager.SetTextVariable("ST_PARTY_GREETING", greeting, false); }
                catch { }
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("STPartyDialogRegistration.IsOurPartyEncounter failed", ex);
            return false;
        }
    }

    private static void LeaveOurPartyEncounter()
    {
        try
        {
            // IG 验证过的写法：先标记 LeaveEncounter，vanilla 的 dialog close 流程会真正离开
            PlayerEncounter.LeaveEncounter = true;
        }
        catch (Exception ex)
        {
            Logger.Error("STPartyDialogRegistration.LeaveOurPartyEncounter failed", ex);
        }
    }
}
