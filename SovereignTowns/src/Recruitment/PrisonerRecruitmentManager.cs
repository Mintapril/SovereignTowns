using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using Logger = SovereignTowns.Logging.Logger;
using SovereignTowns.Configuration;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Recruitment;

/// <summary>
/// 每日把玩家归属城镇地牢里的俘虏逐步转化为驻军成员，
/// 仿 ImprovedGarrisons.Recruitment.GarrisonRecruitmentLogic.RecruitPrisonersInSettlement 主路径。
///
/// 触发：<see cref="Campaign.SovereignTownsCampaignBehavior.OnDailyTickSettlement"/>。
/// 不创建任何 MobileParty，仅在 settlement.Party.PrisonRoster 与 town.GarrisonParty.MemberRoster 之间转移。
/// </summary>
public sealed class PrisonerRecruitmentManager
{
    /// <summary>
    /// 由主 CampaignBehavior 每个 settlement 调用一次（DailyTickSettlement）。
    /// 仅处理玩家所属城镇，跳过围城、跳过英雄俘虏。
    /// </summary>
    public void OnDailyTickSettlement(Settlement? settlement)
    {
        try
        {
            // 用户明确："训练在首府" — 俘虏招募仅 town（外层 OnDailyTickSettlement 已加首府判定，此处冗余防御）
            if (settlement == null) return;
            if (!settlement.IsTown) return;
            if (settlement.OwnerClan != Clan.PlayerClan) return;

            var features = ConfigurationManager.Current?.EnabledFeatures;
            if (features == null || !features.AutoRecruitment) return;

            if (settlement.IsUnderSiege)
            {
                Logger.Debug($"  PrisonerRecruitment: 跳过 '{settlement.Name}' — IsUnderSiege");
                return;
            }

            var town = settlement.Town;
            if (town == null) return;
            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule == null || !rule.AllowPrisonerConversion) return;

            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "PrisonerRecruitment"))
                return;

            var settlementParty = settlement.Party;
            if (settlementParty == null) return;

            var prisonRoster = settlementParty.PrisonRoster;
            if (prisonRoster == null) return;

            var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PrisonerRecruitmentCalculationModel;
            if (model == null) return;

            int dailyConformity = Math.Max(0, ConfigurationManager.Current?.DailyPrisonerConformityAmount ?? 5);

            // GetTroopRoster() 返回快照副本 — 但我们仍把 character 单独拷贝到 list，避免在遍历过程中改 roster
            var characters = new List<CharacterObject>();
            try
            {
                foreach (var elem in prisonRoster.GetTroopRoster())
                {
                    if (elem.Character != null) characters.Add(elem.Character);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': enumerate prison roster threw: {ex.Message}");
                return;
            }

            int totalRecruited = 0;
            int distinctTypesRecruited = 0;

            foreach (var character in characters)
            {
                try
                {
                    if (character == null || character.IsHero) continue;
                    if (!TroopTemplateMatcher.MatchesRule(character, rule)) continue;
                    if (!prisonRoster.Contains(character)) continue;

                    int troopCount = prisonRoster.GetTroopCount(character);
                    if (troopCount <= 0) continue;

                    int recruitable;
                    try
                    {
                        recruitable = model.CalculateRecruitableNumber(settlementParty, character);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': CalculateRecruitableNumber threw for '{character.StringId}': {ex.Message}");
                        continue;
                    }

                    // 仿 IG：若当前可招数 < 全量俘虏数，则继续累加 conformity XP
                    if (recruitable < troopCount)
                    {
                        try
                        {
                            prisonRoster.AddXpToTroop(character, dailyConformity * troopCount);
                            recruitable = model.CalculateRecruitableNumber(settlementParty, character);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': AddXpToTroop threw for '{character.StringId}': {ex.Message}");
                        }
                    }

                    if (recruitable <= 0) continue;

                    var garrison = town.GarrisonParty;
                    if (garrison == null)
                    {
                        try
                        {
                            settlement.AddGarrisonParty();
                            garrison = town.GarrisonParty;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': AddGarrisonParty threw: {ex.Message}");
                            continue;
                        }
                    }
                    if (garrison == null) continue;

                    // 按 vanilla PartySizeLimit 钳制，避免溢出
                    try
                    {
                        int partySizeLimit = garrison.Party?.PartySizeLimit ?? int.MaxValue;
                        int currentMen = garrison.MemberRoster?.TotalManCount ?? 0;
                        if (currentMen + recruitable > partySizeLimit)
                        {
                            recruitable = partySizeLimit - currentMen;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': PartySizeLimit clamp threw: {ex.Message}");
                    }

                    if (recruitable <= 0) continue;

                    int conformityNeeded;
                    try
                    {
                        conformityNeeded = model.GetConformityNeededToRecruitPrisoner(character);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': GetConformityNeededToRecruitPrisoner threw: {ex.Message}");
                        continue;
                    }

                    var garrisonMembers = garrison.MemberRoster;
                    if (garrisonMembers == null) continue;

                    try
                    {
                        garrisonMembers.AddToCounts(character, recruitable, false, 0, 0);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': garrison.AddToCounts threw: {ex.Message}");
                        continue;
                    }

                    try
                    {
                        // IG 行为：先扣 XP（标记招走部分已结算）再 RemoveTroop
                        prisonRoster.AddXpToTroop(character, -1 * conformityNeeded * recruitable);
                        prisonRoster.RemoveTroop(character, recruitable, default(UniqueTroopDescriptor), 0);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': prison roster XP/Remove threw: {ex.Message}");
                    }

                    totalRecruited += recruitable;
                    distinctTypesRecruited++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"PrisonerRecruitment '{settlement.Name}': inner loop failure for '{character?.StringId}'", ex);
                }
            }

            if (totalRecruited > 0 || distinctTypesRecruited > 0)
            {
                DecisionAuditLogger.LogRule(
                    decisionType: "RecruitPrisoner",
                    inputSummary: $"town={settlement.StringId} prisonerTypes={characters.Count} dailyConformity={dailyConformity}",
                    decisionJson: $"{{\"town\":\"{settlement.StringId}\",\"recruited\":{totalRecruited},\"distinctTypes\":{distinctTypesRecruited}}}",
                    accepted: totalRecruited > 0);

                Logger.Info($"  PrisonerRecruitment '{settlement.Name}': 转化 {totalRecruited} 名俘虏入驻军 ({distinctTypesRecruited} 种兵种)");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"PrisonerRecruitmentManager.OnDailyTickSettlement failed (settlement='{settlement?.Name}')", ex);
        }
    }
}
