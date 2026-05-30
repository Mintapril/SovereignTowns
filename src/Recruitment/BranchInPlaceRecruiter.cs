using System;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Evaluators;
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
    public static int RecruitFromBranchNotables(Settlement? branch, int desiredPower, int maxRecruitCount = int.MaxValue, string reason = "")
    {
        int recruited = 0;
        try
        {
            if (branch == null || (!branch.IsTown && !branch.IsCastle)) return 0;
            if (desiredPower <= 0 || maxRecruitCount <= 0) return 0;
            var registry = SovereignTowns.Capital.CapitalRegistry.Instance;
            if (registry != null)
            {
                if (!registry.IsManagedClan(branch.OwnerClan)) return 0;
                // Vanilla 不存在 "clan 内 hero 各自持城" 概念（CLAUDE.md #6）：clan.Fiefs 全部
                // 归 Clan.Leader 一个账户。所以 branch 一律走 mod 招募，无需按 "is the capital
                // owner's personal fief vs another lord's fief" 区分。
            }
            else if (branch.OwnerClan != Clan.PlayerClan) return 0;
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

            // BranchRule 不带 FoodSafetyThreshold（极简），与 TownGarrisonRule 默认 -2.0 对齐做硬阈值兜底。
            const float branchFoodSafetyThreshold = -2.0f;
            if (town.FoodChange < branchFoodSafetyThreshold)
            {
                Logger.Info($"BranchInPlace '{branch.Name}': 跳过 — town.FoodChange={town.FoodChange:F1} < {branchFoodSafetyThreshold:F1}");
                return 0;
            }

            // PR-5'(2026-05-24): BranchRule removed; use TownGarrisonRule via GetRuleFor.
            // LowTierMinFraction had no equivalent in TownGarrisonRule; hardcode 0.3f (former BranchRule default).
            const float lowTierMinFraction = 0.3f;
            float currentPower = GarrisonPowerEvaluator.ComputeRosterPower(memberRoster);
            if (currentPower >= desiredPower)
            {
                Logger.Info($"BranchInPlace '{branch.Name}': 跳过 — currentPower={currentPower:F1} >= desiredPower={desiredPower}");
                return 0;
            }

            var ownerHero = branch.OwnerClan?.Leader;
            if (ownerHero == null) return 0;
            var volunteerModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) return 0;

            // 决定本轮策略：低 tier 不足 → 优先低 tier；否则优先高 tier。
            bool prioritizeLowTier =
                GarrisonPowerEvaluator.LowTierHeadCountFraction(memberRoster) < lowTierMinFraction;

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
                    maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"BranchInPlace '{branch.Name}': MaximumIndexHeroCanRecruitFromHero threw for notable '{notable.Name}': {ex.Message}");
                    continue;
                }
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

            bool shouldCharge = SovereignTowns.Capital.CapitalRegistry.ShouldChargeClan(branch.OwnerClan);

            foreach (var (troop, notable, idx) in candidates)
            {
                if (recruited >= maxRecruitCount) break;
                if (memberRoster.TotalManCount + 1 > partySizeLimit) break;
                if (GarrisonPowerEvaluator.ComputeRosterPower(memberRoster) >= desiredPower) break;

                if (shouldCharge && !ModTreasury.CanAfford(branch.OwnerClan, 5)) break;
                bool charged = false;
                if (shouldCharge)
                {
                    if (!ModTreasury.Charge(branch.OwnerClan, ExpenseCategory.RecruiterWage, 5, $"branch_in_place branch={branch.StringId} troop={troop.StringId}")) break;
                    charged = true;
                }

                try { memberRoster.AddToCounts(troop, 1, false, 0, 0); }
                catch (Exception ex)
                {
                    if (charged)
                    {
                        try
                        {
                            ModTreasury.Refund(branch.OwnerClan, ExpenseCategory.RecruiterWage, 5,
                                $"rollback branch_in_place add failed branch={branch.StringId} troop={troop.StringId}");
                        }
                        catch (Exception refundEx)
                        {
                            Logger.Warn($"BranchInPlace '{branch.Name}': refund failed after AddToCounts failure for '{troop.StringId}': {refundEx.Message}");
                        }
                    }
                    Logger.Warn($"BranchInPlace '{branch.Name}': AddToCounts threw for '{troop.StringId}': {ex.Message}");
                    continue;
                }

                if (idx >= 0 && idx < notable.VolunteerTypes.Length) notable.VolunteerTypes[idx] = null;
                recruited++;
            }

            Logger.Info($"BranchInPlace '{branch.Name}': recruited={recruited} maxRecruitCount={maxRecruitCount} desiredPower={desiredPower} currentPower={currentPower:F1} → {GarrisonPowerEvaluator.ComputeRosterPower(memberRoster):F1} priorityLowTier={prioritizeLowTier} reason='{reason}'");
        }
        catch (Exception ex)
        {
            Logger.Error("BranchInPlaceRecruiter.RecruitFromBranchNotables failed", ex);
        }
        return recruited;
    }
}
