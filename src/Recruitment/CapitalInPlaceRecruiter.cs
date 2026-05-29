using System;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using SovereignTowns.Evaluators;
using SovereignTowns.Templates;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
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
/// 与 <see cref="RecruitmentDispatcher"/> 派去 village 招募不同：
///   - 不创建 MobileParty / 不寻路
///   - 玩家氏族按固定单兵费用扣款，AI 氏族免费
///   - 不收 BudgetLimit 约束
///   - 受 PartySizeLimit 钳制（目标人数由 MCMF 在预算约束下决定，不依赖手设字段）
///   - 由 CapitalLogisticsManager 在每日首府调度中触发；与 village notable 24h 刷新节奏一致
///
/// 兵种归类：优先使用 vanilla <see cref="CharacterObject.DefaultFormationClass"/>；骑射独立，
/// 投掷兵按游戏默认编队归入步兵 / 射手 / 骑兵 / 骑射。
/// </summary>
public static class CapitalInPlaceRecruiter
{
    /// <summary>
    /// 对首府执行一次"本城招募",招 <paramref name="count"/> 名可服务于 <paramref name="role"/> 的
    /// 志愿兵。role/count 由 CapitalLogisticsManager 从 MCMF in-place 指令直接透传 —— 招募器不再
    /// 自行重算 role 配额。失败、被围、未启用 → no-op。全方法 try-catch,绝不抛。
    ///
    /// "可服务于 role"判定:精确模板模式 = 能升级进某个 role 相符的模板目标兵(与
    /// <see cref="TroopTemplateMatcher"/> 升级路径同口径);通用模式 = 志愿兵当前 role 即该 role。
    /// </summary>
    public static int RecruitFromCapitalNotables(Settlement? capital, GenericTroopRole role, int count)
    {
        int recruited = 0;
        try
        {
            if (capital == null || !capital.IsTown) { Logger.Info("CapitalInPlace: 跳过 — null/non-town"); return recruited; }
            if (role == GenericTroopRole.Unknown || count <= 0)
            { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — 无效 role/count ({role}/{count})"); return recruited; }
            // B7.15 multi-clan：广义到受管 clan；外层 OnDailyTickSettlement 已按"settlement == 该 clan 首府"路由
            var registry = SovereignTowns.Capital.CapitalRegistry.Instance;
            if (registry != null)
            {
                if (!registry.IsManagedClan(capital.OwnerClan)) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — clan {capital.OwnerClan?.StringId} 不在 registry"); return recruited; }
            }
            else if (capital.OwnerClan != Clan.PlayerClan) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — 非玩家 + registry 未就绪"); return recruited; }
            if (capital.IsUnderSiege) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — IsUnderSiege"); return recruited; }

            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — AutoRecruitment 已关闭"); return recruited; }

            var town = capital.Town;
            if (town == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — town==null"); return recruited; }
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
                catch (Exception ex) { Logger.Error($"CapitalInPlace '{capital.Name}': AddGarrisonParty 失败", ex); return recruited; }
            }
            if (garrison == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — AddGarrisonParty 后仍 null"); return recruited; }
            var memberRoster = garrison.MemberRoster;
            if (memberRoster == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — MemberRoster==null"); return recruited; }

            int partySizeLimit = garrison.Party?.PartySizeLimit ?? int.MaxValue;
            int currentMen = memberRoster.TotalManCount;
            if (currentMen >= partySizeLimit) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — currentMen({currentMen}) >= partySizeLimit({partySizeLimit})"); return recruited; }

            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — rule==null"); return recruited; }
            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "CapitalInPlaceRecruiter"))
            { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — FoodGuard 触发（FoodChange={town.FoodChange:F1} threshold={rule.FoodSafetyThreshold:F1}）"); return recruited; }

            var ownerHero = capital.OwnerClan?.Leader;
            if (ownerHero == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — ownerClan.Leader==null"); return recruited; }

            var volunteerModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — volunteerModel==null"); return recruited; }

            int candidatesScanned = 0;
            int notablesScanned = 0;
            int notablesEligible = 0;

            var notables = capital.Notables;
            if (notables == null) { Logger.Info($"CapitalInPlace '{capital.Name}': 跳过 — Notables==null"); return recruited; }
            Logger.Info($"CapitalInPlace '{capital.Name}': 开始扫描 {notables.Count} 个 notable,garrison={currentMen}/{partySizeLimit}, owner.Gold={ownerHero.Gold}, 目标 role={role} count={count}");

            // 通用匹配文化过滤：解析一次玩家面板的文化策略 → 必须匹配的文化 id（null = 不过滤）。
            string? requiredCultureId = GenericTroopMatcher.ResolveRequiredCultureId(rule, capital.Town);

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
                    // ST 自身招兵：进入 StRecruitContext 让 STVolunteerModel 放行（否则被管 AI clan 首府
                    // 调本方法时会被自己的 model 阻断 → 自锁）。
                    using (StRecruitContext.Enter())
                    {
                        maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(ownerHero, notable, -101);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  CapitalInPlace '{capital.Name}': MaximumIndexHeroCanRecruitFromHero threw for notable '{notable.Name}': {ex.Message}");
                    continue;
                }
                if (maxIdx < 0) continue;
                notablesEligible++;

                // 2026-05-29 fix: 与 village 招募队同口径放宽 slot 上限（VolunteerMul=2.0）。
                // 此前 in-place 只扫 [0, maxIdx]；village 队（StRecruiterPartyComponent）扫 [0, effectiveMaxIdx]（约 2×）。
                // owner 与本城 notable 关系一般 → maxIdx 偏小；志愿兵若落在更高 slot，in-place 会整段漏扫 → 候选扫描=0。
                const float VolunteerMul = 2.0f;  // 镜像 StRecruiterPartyComponent.VolunteerMul
                int effectiveMaxIdx = Math.Min(
                    volunteerTypes.Length - 1,
                    Math.Max(maxIdx, (int)Math.Round((maxIdx + 1) * VolunteerMul) - 1));

                // [GARRISON-DIAG] 每 notable slot 占用 —— 定位"候选扫描=0"是 slot 全空，还是被 maxIdx 卡窄。
                int nonNullSlots = 0;
                for (int s = 0; s < volunteerTypes.Length; s++) if (volunteerTypes[s] != null) nonNullSlots++;
                Logger.Info($"  [GARRISON-DIAG] CapitalInPlace notable '{notable.Name}' maxIdx={maxIdx} effMaxIdx={effectiveMaxIdx} slotsFilled={nonNullSlots}/{volunteerTypes.Length}");

                for (int i = 0; i < volunteerTypes.Length && i <= effectiveMaxIdx; i++)
                {
                    var troop = volunteerTypes[i];
                    if (troop == null) continue;

                    candidatesScanned++;

                    // 容量钳制：每招一个重新读 TotalManCount，避免漂移
                    if (memberRoster.TotalManCount + 1 > partySizeLimit) return recruited;

                    // 通用匹配：按规则过滤文化/贵族/禁用项，再看兵种桶 + Tier 范围 + 比例。
                    if (!TroopTemplateMatcher.MatchesRule(troop, rule)) continue;
                    // 玩家面板的文化过滤策略（玩家文化 / 首府文化 / 不过滤）。
                    // PR-5'(2026-05-24): UseGenericMatching removed; culture filter always applies.
                    if (!GenericTroopMatcher.CultureFilterAllows(troop, requiredCultureId)) continue;

                    // MCMF 指定了 role:只招能服务该 role 的志愿兵(精确模式认升级路径,
                    // 与 MatchesRule 同口径 —— T1 新兵 role=Infantry,但若可升级成 sharpshooter
                    // 即服务于 Ranged)。招满 MCMF 请求量即止,不自行重算 role 配额。
                    if (!TroopTemplateMatcher.CanServeRole(troop, rule, role)) continue;

                    // B7.27：原地招募也要扣费（与外派对齐）。玩家氏族扣 5 denar，AI clan 免费。
                    // 顺序：先 Charge → 再 AddToCounts。AddToCounts 失败时必须退款，避免"扣费成功但兵没进驻军"。
                    bool shouldCharge = SovereignTowns.Capital.CapitalRegistry.ShouldChargeClan(capital.OwnerClan);
                    if (shouldCharge && !ModTreasury.CanAfford(capital.OwnerClan, 5))
                    {
                        Logger.Info($"CapitalInPlace '{capital.Name}': 资金不足 — 终止本次招募（已招 {recruited} 人）");
                        return recruited;
                    }

                    bool charged = false;
                    if (shouldCharge && !ModTreasury.Charge(capital.OwnerClan, ExpenseCategory.RecruiterWage, 5, $"in_place capital={capital.StringId} troop={troop.StringId}"))
                    {
                        Logger.Info($"CapitalInPlace '{capital.Name}': ModTreasury.Charge 失败 — 终止本次招募（已招 {recruited} 人）");
                        return recruited;
                    }
                    charged = shouldCharge;

                    try
                    {
                        memberRoster.AddToCounts(troop, 1, false, 0, 0);
                    }
                    catch (Exception ex)
                    {
                        if (charged)
                        {
                            try
                            {
                                ModTreasury.Refund(capital.OwnerClan, ExpenseCategory.RecruiterWage, 5,
                                    $"rollback in_place add failed capital={capital.StringId} troop={troop.StringId}");
                            }
                            catch (Exception refundEx)
                            {
                                Logger.Warn($"  CapitalInPlace '{capital.Name}': refund failed after AddToCounts failure for '{troop.StringId}': {refundEx.Message}");
                            }
                        }
                        Logger.Warn($"  CapitalInPlace '{capital.Name}': AddToCounts threw for '{troop.StringId}' after charge; refunded={charged}: {ex.Message}");
                        continue;
                    }

                    // 仅在成功 Add 后清 slot；过滤跳过的保留供下个 tick / 玩家手动招
                    volunteerTypes[i] = null;
                    recruited++;
                    if (recruited >= count) return recruited; // 招满 MCMF 请求量
                }
            }

            Logger.Info($"CapitalInPlace '{capital.Name}': recruited={recruited}/{count} role={role} 候选扫描={candidatesScanned} notables {notablesEligible}/{notablesScanned} eligible (现 garrison={memberRoster.TotalManCount})");

            // 玩家可见预警 — 扫到候选但 0 招募 = 无志愿兵能服务该 role(被 Tier 过滤,或没有兵种
            // 能升级到该 role 的模板目标)。MCMF 会持续请求该 role → 反复刷此行即说明 in-place 对该 role 无解。
            if (recruited == 0 && candidatesScanned > 0)
            {
                // PR-5'(2026-05-24): MinTier/MaxTier removed; simplified log message.
                Logger.Warn($"CapitalInPlace '{capital.Name}': 扫到 {candidatesScanned} 个候选但 0 招募 — 无志愿兵可服务 role={role}（文化过滤 / role 不匹配）。");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalInPlaceRecruiter.RecruitFromCapitalNotables failed", ex);
        }
        return recruited;
    }
}
