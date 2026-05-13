using System;
using SovereignTowns.Configuration;
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
/// 同步将 <c>VolunteerTypes[i]</c> 置 null（与 IG <c>RecruitFromSettlement</c> 行为一致）。
///
/// 与 <see cref="RecruitmentManager"/> 派去 village 招募不同：
///   - 不创建 MobileParty / 不寻路 / 不扣金（auto 模式无 UI 入口扣钱，IG 同样不在此处扣金）
///   - 不收 BudgetLimit 约束（不花钱）
///   - 受 PartySizeLimit + rule.TargetTotalCount 钳制
///   - 仅在 OnDailyTickSettlement(capital) 触发；与 village notable 24h 刷新节奏一致
///
/// 兵种归类：vanilla <see cref="CharacterObject.DefaultFormationClass"/> +
/// <see cref="CharacterObject.IsMounted"/> + <see cref="CharacterObject.FirstBattleEquipment"/>
/// 武器槽 <see cref="WeaponClass"/> 判别 Bow/Crossbow/投掷。
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
            if (capital == null || !capital.IsTown) return;
            if (capital.OwnerClan != Clan.PlayerClan) return;
            if (capital.IsUnderSiege) return;

            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment) return;

            var town = capital.Town;
            if (town == null) return;
            var garrison = town.GarrisonParty;
            if (garrison == null) return;
            var memberRoster = garrison.MemberRoster;
            if (memberRoster == null) return;

            int partySizeLimit = garrison.Party?.PartySizeLimit ?? int.MaxValue;
            int currentMen = memberRoster.TotalManCount;
            if (currentMen >= partySizeLimit) return;

            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule == null) return;
            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "CapitalInPlaceRecruiter"))
                return;
            if (currentMen >= rule.TargetTotalCount) return;

            var ownerHero = capital.OwnerClan?.Leader;
            if (ownerHero == null) return;

            var volunteerModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) return;

            int cap = Math.Min(partySizeLimit, rule.TargetTotalCount);

            int recruited = 0;
            int candidatesScanned = 0;

            var notables = capital.Notables;
            if (notables == null) return;

            foreach (var notable in notables)
            {
                if (notable == null) continue;
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

                int upper = Math.Min(volunteerTypes.Length - 1, maxIdx);
                for (int i = 0; i <= upper; i++)
                {
                    var troop = volunteerTypes[i];
                    if (troop == null) continue;

                    candidatesScanned++;

                    // 容量钳制：每招一个重新读 TotalManCount，避免漂移
                    if (memberRoster.TotalManCount + 1 > cap) return;

                    // 通用匹配：不看文化/阵营，只看兵种桶 + Tier 比例。
                    if (!TroopTemplateMatcher.MatchesRule(troop, rule)) continue;

                    try
                    {
                        memberRoster.AddToCounts(troop, 1, false, 0, 0);
                        // 仅在成功 Add 后清 slot；过滤跳过的保留供下个 tick / 玩家手动招
                        volunteerTypes[i] = null;
                        recruited++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"  CapitalInPlace '{capital.Name}': AddToCounts threw for '{troop.StringId}': {ex.Message}");
                        continue;
                    }
                }
            }

            if (recruited > 0)
            {
                Logger.Debug($"CapitalInPlace: capital='{capital.Name}' in-place recruited {recruited} (scanned {candidatesScanned}, garrison now {memberRoster.TotalManCount}/{cap})");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CapitalInPlaceRecruiter.RecruitFromCapitalNotables failed", ex);
        }
    }

    /// <summary>
    /// 按 rule 的 5 个 ratio（非零=允许）过滤 troop。归类规则：
    ///   - <see cref="CharacterObject.IsMounted"/> → cavalry（含 HorseArcher，vanilla 也归骑兵）
    ///   - <see cref="FormationClass.Skirmisher"/> → thrower
    ///   - <see cref="CharacterObject.IsRanged"/> 且非骑非投 → 看武器槽 WeaponClass：
    ///       Bow → archer，Crossbow → crossbow，Javelin/Throwing* → thrower，未知 → archer 兜底
    ///   - 否则 → infantry
    /// </summary>
    private static bool IsTroopAllowedByRatios(CharacterObject troop, TownGarrisonRule rule)
    {
        try
        {
            var bucket = ClassifyTroop(troop);
            return bucket switch
            {
                TroopBucket.Cavalry => rule.CavalryRatio > 0f,
                TroopBucket.Infantry => rule.InfantryRatio > 0f,
                TroopBucket.Archer => rule.ArcherRatio > 0f,
                TroopBucket.Crossbow => rule.CrossbowRatio > 0f,
                TroopBucket.Thrower => rule.ThrowerRatio > 0f,
                _ => true // 未分类不阻挡
            };
        }
        catch
        {
            // 任何异常按"允许"处理，保持容错
            return true;
        }
    }

    private enum TroopBucket { Unknown, Infantry, Archer, Crossbow, Thrower, Cavalry }

    private static TroopBucket ClassifyTroop(CharacterObject troop)
    {
        // 1) 骑兵优先（HorseArcher 也归这里）
        try
        {
            if (troop.IsMounted) return TroopBucket.Cavalry;
        }
        catch { }

        // 2) Skirmisher formation 直接归投掷
        try
        {
            if (troop.DefaultFormationClass == FormationClass.Skirmisher) return TroopBucket.Thrower;
        }
        catch { }

        // 3) Ranged 进一步按主武器细分
        bool isRanged = false;
        try { isRanged = troop.IsRanged; } catch { }
        if (isRanged)
        {
            var weaponBucket = InspectRangedWeaponClass(troop);
            if (weaponBucket != TroopBucket.Unknown) return weaponBucket;
            return TroopBucket.Archer; // 未识别的 ranged 兜底当弓手
        }

        // 4) 默认步兵
        return TroopBucket.Infantry;
    }

    /// <summary>
    /// 扫描 troop 的 FirstBattleEquipment 4 个武器槽，返回首个识别到的远程武器类别。
    /// 找不到任何远程武器 → Unknown（调用方按 ranged 兜底处理）。
    /// </summary>
    private static TroopBucket InspectRangedWeaponClass(CharacterObject troop)
    {
        try
        {
            var equipment = troop.FirstBattleEquipment;
            if (equipment == null) return TroopBucket.Unknown;

            // EquipmentIndex.Weapon0 .. Weapon3
            for (int slot = (int)EquipmentIndex.Weapon0; slot <= (int)EquipmentIndex.Weapon3; slot++)
            {
                EquipmentElement elem;
                try { elem = equipment[(EquipmentIndex)slot]; }
                catch { continue; }

                var item = elem.Item;
                if (item == null) continue;
                var weapon = item.PrimaryWeapon;
                if (weapon == null) continue;

                switch (weapon.WeaponClass)
                {
                    case WeaponClass.Bow:
                        return TroopBucket.Archer;
                    case WeaponClass.Crossbow:
                        return TroopBucket.Crossbow;
                    case WeaponClass.Javelin:
                    case WeaponClass.ThrowingAxe:
                    case WeaponClass.ThrowingKnife:
                        return TroopBucket.Thrower;
                }
            }
        }
        catch { }
        return TroopBucket.Unknown;
    }
}
