using System;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Recruitment;

/// <summary>
/// 首府"本城自动招募"：直接消费 capital settlement 自身 Notable.VolunteerTypes，
/// 把符合 rule 兵种比例（非零 ratio）的志愿者塞入 GarrisonParty.MemberRoster，
/// 同步将 <c>VolunteerTypes[i]</c> 置 null（与 vanilla 玩家手动从 notable 招兵后的清 slot 行为一致）。
///
/// 与 <see cref="RecruitmentManager"/> 派去 village 招募不同：
///   - 不创建 MobileParty / 不寻路 / 不扣金（auto 模式无 UI 入口扣钱）
///   - 不收 BudgetLimit 约束（不花钱）
///   - 受 PartySizeLimit + rule.TargetTotalCount 钳制
///   - 仅在 OnDailyTickSettlement(capital) 触发；与 village notable 24h 刷新节奏一致
///
/// 兵种归类：优先使用 vanilla <see cref="CharacterObject.DefaultFormationClass"/>；骑射独立，
/// 投掷兵按游戏默认编队归入步兵 / 射手 / 骑兵 / 骑射。
/// </summary>
public static class CapitalInPlaceRecruiter
{
    /// <summary>
    /// 在 daily tick 上对首府执行一次"本城招募"。失败、被围、未启用 → no-op。
    /// 全方法 try-catch，绝不抛。
    /// </summary>
    public static void RecruitFromCapitalNotables(Settlement? capital)
    {
        try
        {
            if (capital == null || !capital.IsTown) { Logger.Info("CapitalInPlace: 跳过 — null/non-town"); return; }
            // B7.15 multi-clan：广义到受管 clan；外层 OnDailyTickSettlement 已按"settlement == 该 clan 首府"路由
            var registry = SovereignTowns.Capital.CapitalRegistry.Instance;
            if (registry != null)
            {
                if (!registry.IsManagedClan(capital.OwnerClan)) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — clan {capital.OwnerClan?.StringId} 不在 registry"); return; }
            }
            else if (capital.OwnerClan != Clan.PlayerClan) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — 非玩家 + registry 未就绪"); return; }
            if (capital.IsUnderSiege) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — IsUnderSiege"); return; }

            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — AutoRecruitment 已关闭"); return; }

            var town = capital.Town;
            if (town == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — town==null"); return; }
            var garrison = town.GarrisonParty;
            // B7.21 Fix A：vanilla 在 garrison 清 0 后会移除 GarrisonParty，导致后续 tick 永远走不到招募路径。
            // 主动调 AddGarrisonParty 重建（与 PrisonerRecruitmentManager 同套路）。
            if (garrison == null)
            {
                try
                {
                    capital.AddGarrisonParty();
                    garrison = town.GarrisonParty;
                    Logger.Info($"CapitalInPlace '{capital.Name}': 检测到 GarrisonParty==null，已重建空 party");
                }
                catch (Exception ex) { Logger.Error($"CapitalInPlace '{capital.Name}': AddGarrisonParty 失败", ex); return; }
            }
            if (garrison == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — AddGarrisonParty 后仍 null"); return; }
            var memberRoster = garrison.MemberRoster;
            if (memberRoster == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — MemberRoster==null"); return; }

            int partySizeLimit = garrison.Party?.PartySizeLimit ?? int.MaxValue;
            int currentMen = memberRoster.TotalManCount;
            if (currentMen >= partySizeLimit) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — currentMen({currentMen}) >= partySizeLimit({partySizeLimit})"); return; }

            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — rule==null"); return; }
            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "CapitalInPlaceRecruiter"))
            { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — FoodGuard 触发（FoodChange={town.FoodChange:F1} threshold={rule.FoodSafetyThreshold:F1}）"); return; }
            if (currentMen >= rule.TargetTotalCount) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — 已达目标 {currentMen}/{rule.TargetTotalCount}"); return; }

            var ownerHero = capital.OwnerClan?.Leader;
            if (ownerHero == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — ownerClan.Leader==null"); return; }

            var volunteerModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — volunteerModel==null"); return; }

            int cap = Math.Min(partySizeLimit, rule.TargetTotalCount);

            // B7.23：撤紧急模式 — mod 不应自作主张突破玩家配置的 Tier 过滤。
            // 招不到合规候选时只 Warn 提示玩家放宽规则。

            // B7.22 Fix per-role 饱和：先快照当前 garrison 各 role 人数，每招一个就预测下一次
            // 是否仍有缺口，避免「远程 40/20 还在继续招」。
            var snap = GenericTroopMatcher.Snapshot(memberRoster);
            int targetCav = (int)Math.Round(rule.CavalryRatio  * cap);
            int targetHa  = (int)Math.Round(rule.HorseArcherRatio * cap);
            int targetInf = (int)Math.Round(rule.InfantryRatio * cap);
            int targetRng = (int)Math.Round(rule.RangedRatio * cap);
            int gainedCav = 0, gainedHa = 0, gainedInf = 0, gainedRng = 0;

            int recruited = 0;
            int candidatesScanned = 0;
            int notablesScanned = 0;
            int notablesEligible = 0;

            var notables = capital.Notables;
            if (notables == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — Notables==null"); return; }
            Logger.Info($"CapitalInPlace '{capital.Name}': 开始扫描 {notables.Count} 个 notable，garrison={currentMen}/{cap}, owner.Gold={ownerHero.Gold}, role 配额 cav/ha/inf/rng={targetCav}/{targetHa}/{targetInf}/{targetRng}");

            foreach (var notable in notables)
            {
                if (notable == null) continue;
                notablesScanned++;
                if (!notable.CanHaveRecruits) continue;

                var volunteerTypes = notable.VolunteerTypes;
                if (volunteerTypes == null || volunteerTypes.Length == 0) continue;

                int maxIdx;
                try
                {
                    maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  CapitalInPlace '{capital.Name}': MaximumIndexHeroCanRecruitFromHero threw for notable '{notable.Name}': {ex.Message}");
                    continue;
                }
                if (maxIdx < 0) continue;
                notablesEligible++;

                int upper = Math.Min(volunteerTypes.Length - 1, maxIdx);
                for (int i = 0; i <= upper; i++)
                {
                    var troop = volunteerTypes[i];
                    if (troop == null) continue;

                    candidatesScanned++;

                    // 容量钳制：每招一个重新读 TotalManCount，避免漂移
                    if (memberRoster.TotalManCount + 1 > cap) return;

                    // 通用匹配：按规则过滤文化/贵族/禁用项，再看兵种桶 + Tier 范围 + 比例。
                    if (!TroopTemplateMatcher.MatchesRule(troop, rule)) continue;

                    // B7.22 Fix per-role 饱和：检查该 role 是否已达模板配额。
                    var roleOfTroop = GenericTroopMatcher.GetRole(troop);
                    int currentRole, targetRole;
                    switch (roleOfTroop)
                    {
                        case GenericTroopRole.Cavalry:  currentRole = snap.Cavalry  + gainedCav; targetRole = targetCav; break;
                        case GenericTroopRole.HorseArcher: currentRole = snap.HorseArcher + gainedHa; targetRole = targetHa; break;
                        case GenericTroopRole.Infantry: currentRole = snap.Infantry + gainedInf; targetRole = targetInf; break;
                        case GenericTroopRole.Ranged: currentRole = snap.Ranged + gainedRng; targetRole = targetRng; break;
                        default: continue; // 未知 role 跳过
                    }
                    if (currentRole >= targetRole) continue; // 该 role 已饱和 → 跳过此候选

                    // B7.27：原地招募也要扣费（与外派对齐）。玩家氏族扣 5 denar，AI clan 免费。
                    if (capital.OwnerClan == Clan.PlayerClan)
                    {
                        if (!ModTreasury.Charge(ExpenseCategory.RecruiterWage, 5, $"in_place capital={capital.StringId} troop={troop.StringId}"))
                        {
                            // 扣费失败（金币不足且 PauseSpendingWhenBroke=true）→ 终止本次首府招募 tick
                            Logger.Info($"CapitalInPlace '{capital.Name}': ModTreasury.Charge 失败 — 资金不足，终止本次招募（已招 {recruited} 人）");
                            return;
                        }
                    }

                    try
                    {
                        memberRoster.AddToCounts(troop, 1, false, 0, 0);
                        // 仅在成功 Add 后清 slot；过滤跳过的保留供下个 tick / 玩家手动招
                        volunteerTypes[i] = null;
                        recruited++;
                        // 同步 per-role 计数，下个候选基于新值判定
                        switch (roleOfTroop)
                        {
                            case GenericTroopRole.Cavalry:  gainedCav++; break;
                            case GenericTroopRole.HorseArcher: gainedHa++; break;
                            case GenericTroopRole.Infantry: gainedInf++; break;
                            case GenericTroopRole.Ranged: gainedRng++; break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  CapitalInPlace '{capital.Name}': AddToCounts threw for '{troop.StringId}': {ex.Message}");
                        continue;
                    }
                }
            }

            Logger.Info($"CapitalInPlace '{capital.Name}': recruited={recruited} 候选扫描={candidatesScanned} notables {notablesEligible}/{notablesScanned} eligible (现 garrison={memberRoster.TotalManCount}/{cap})");

            // B7.23：玩家可见的预警 — 招募 0 但候选 > 0 = 全被规则过滤掉了，提示玩家放宽
            if (recruited == 0 && candidatesScanned > 0)
            {
                Logger.Warn($"CapitalInPlace '{capital.Name}': 扫到 {candidatesScanned} 个候选但 0 招募 — 兵种被规则过滤（Tier {rule.MinTier}-{rule.MaxTier} / 比例为 0 的兵种 / 已饱和 role）。考虑放宽 MinTier 或调整兵种比例。");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalInPlaceRecruiter.RecruitFromCapitalNotables failed", ex);
        }
    }
}
