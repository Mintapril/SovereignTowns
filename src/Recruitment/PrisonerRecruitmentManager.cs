using System;
using System.Collections.Generic;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
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
/// 每日把玩家归属城镇地牢里的俘虏逐步转化为驻军成员（基于 vanilla
/// PrisonerRecruitmentCalculationModel + 累加 conformity XP）。
///
/// 触发：<see cref="Campaign.SovereignTownsCampaignBehavior.OnDailyTickSettlement"/>。
/// 不创建任何 MobileParty，仅在 settlement.Party.PrisonRoster 与 town.GarrisonParty.MemberRoster 之间转移。
/// </summary>
public sealed class PrisonerRecruitmentManager
{
    private readonly CapitalRegistry? _capitalRegistry;

    public PrisonerRecruitmentManager(CapitalRegistry? capitalRegistry = null)
    {
        _capitalRegistry = capitalRegistry;
    }

    /// <summary>
    /// 由主 CampaignBehavior 每个 settlement 调用一次（DailyTickSettlement）。
    /// 仅处理 *受管 clan* 的所属城镇（B7.15 起含 AI），跳过围城、跳过英雄俘虏。
    /// </summary>
    public void OnDailyTickSettlement(Settlement? settlement)
    {
        try
        {
            // 用户明确："训练在首府" — 俘虏招募仅 town（外层 OnDailyTickSettlement 已加首府判定，此处冗余防御）
            if (settlement == null) return;
            if (!settlement.IsTown) return;
            // B7.15 multi-clan：广义到受管 clan
            if (_capitalRegistry != null)
            {
                if (!_capitalRegistry.IsManagedClanWithCapital(settlement.OwnerClan)) return;
            }
            else
            {
                // 兼容：registry 缺失时退回玩家氏族路径
                if (settlement.OwnerClan != Clan.PlayerClan) return;
            }

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
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "PrisonerRecruitmentManager"))
                return;

            var settlementParty = settlement.Party;
            if (settlementParty == null) return;

            var prisonRoster = settlementParty.PrisonRoster;
            if (prisonRoster == null) return;

            var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PrisonerRecruitmentCalculationModel;
            if (model == null) return;

            // B7.20：每日 conformity 数值改由城镇地牢建筑等级派生（不再可手动调整）。
            // settlement_dungeon（Town）/ castle_dungeon（Castle）等级 → (level + 1) × 5
            // 即 lvl 0→5, lvl 1→10, lvl 2→15, lvl 3→20。
            int dailyConformity = ComputeConformityFromDungeon(settlement);

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

            // 通用匹配文化过滤：解析一次玩家面板的文化策略（null = 不过滤）。
            string? requiredCultureId = GenericTroopMatcher.ResolveRequiredCultureId(rule, settlement.Town);

            foreach (var character in characters)
            {
                try
                {
                    if (character == null || character.IsHero) continue;
                    if (!TroopTemplateMatcher.MatchesRule(character, rule)) continue;
                    // 玩家面板的文化过滤策略（玩家文化 / 首府文化 / 不过滤）
                    if (!GenericTroopMatcher.CultureFilterAllows(character, requiredCultureId)) continue;
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

                    // 若当前可招数 < 全量俘虏数，则继续累加 conformity XP（推动剩余俘虏向可招募阈值靠拢）
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

                    // R1：原子转化 — garrison +N 先做（失败→无副作用），再 prison RemoveTroop。
                    // RemoveTroop 失败 → 回滚 garrison -N，防止"驻军和俘虏双份存在"。
                    bool garrisonAdded = false;
                    try
                    {
                        garrisonMembers.AddToCounts(character, recruitable, false, 0, 0);
                        garrisonAdded = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': garrison.AddToCounts threw: {ex.Message}");
                        continue;
                    }

                    try
                    {
                        // 先扣 XP（标记招走部分已结算）再 RemoveTroop
                        prisonRoster.AddXpToTroop(character, -1 * conformityNeeded * recruitable);
                        prisonRoster.RemoveTroop(character, recruitable, default(UniqueTroopDescriptor), 0);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  PrisonerRecruitment '{settlement.Name}': prison roster XP/Remove threw — rolling back garrison: {ex.Message}");
                        if (garrisonAdded)
                        {
                            try { garrisonMembers.AddToCounts(character, -recruitable, false, 0, 0); }
                            catch (Exception undoEx) { Logger.Error("  PrisonerRecruitment: garrison rollback failed — DUPLICATE TROOPS may exist", undoEx); }
                        }
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

    /// <summary>
    /// B7.20：按地牢建筑等级派生每日 conformity XP。Town 用 <c>settlement_dungeon</c>、
    /// Castle 用 <c>castle_dungeon</c> 的 CurrentLevel（0..3），返回 (level + 1) × 5。
    /// 找不到建筑或异常时返回 5（最低保底，对应 lvl 0）。
    /// </summary>
    private static int ComputeConformityFromDungeon(Settlement? settlement)
    {
        const int ConformityPerLevel = 5;
        const int FallbackConformity = ConformityPerLevel;
        try
        {
            var town = settlement?.Town;
            if (town?.Buildings == null) return FallbackConformity;
            string targetId = (settlement != null && settlement.IsCastle) ? "castle_dungeon" : "settlement_dungeon";
            foreach (var b in town.Buildings)
            {
                if (b?.BuildingType == null) continue;
                string id;
                try { id = b.BuildingType.StringId ?? ""; } catch { continue; }
                if (string.Equals(id, targetId, StringComparison.Ordinal))
                {
                    int level;
                    try { level = b.CurrentLevel; } catch { level = 0; }
                    if (level < 0) level = 0;
                    if (level > 3) level = 3;
                    return (level + 1) * ConformityPerLevel;
                }
            }
            return FallbackConformity; // 地牢未建造
        }
        catch (Exception ex)
        {
            Logger.Error($"ComputeConformityFromDungeon failed for '{settlement?.Name}'", ex);
            return FallbackConformity;
        }
    }
}
