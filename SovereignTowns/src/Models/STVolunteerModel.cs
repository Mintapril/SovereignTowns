using System;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Recruitment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

/// <summary>
/// 当三联开关同时为 true（<see cref="EnabledFeatures.ApplyToAiSettlementsToo"/>
/// + <see cref="EnabledFeatures.AutoRecruitment"/>
/// + <see cref="EnabledFeatures.SuppressVanillaGarrisonRecruitment"/>）时，
/// 对所有 ST 已接管的 AI clan 的 hero（lord / leader / companion）作为 buyer 的 volunteer 招募调用
/// 返回 -1，等价于「该 hero 这次能招的 volunteer 索引上限为负」——
/// vanilla <c>RecruitmentCampaignBehavior.RecruitVolunteersFromNotable</c> 的内部 for 循环遇到 maxIdx&lt;0
/// 直接跳过，等价于阻断 AI 通过 vanilla settlement volunteer 渠道招兵。
///
/// IL 验证（2026-05-19）：vanilla v1.3.15 中 <c>RecruitmentCampaignBehavior</c> 唯一调用
/// <see cref="VolunteerModel.MaximumIndexHeroCanRecruitFromHero"/> 的方法是 RecruitVolunteersFromNotable；
/// 它的两条入口（HourlyTickParty + OnBeforeSettlementEntered）都经 CheckRecruiting 汇聚到此处。
/// 因此覆盖此一 model 方法即可同时截断这两条 AI 招兵主路。Mercenary 团 (ApplyRecruitMercenary)
/// 不走 VolunteerModel，本 model 不影响 — 但雇佣兵是付费且有限量，作为副渠道可接受。
///
/// 不阻断玩家氏族：buyer.Clan == Clan.PlayerClan 时直接走 base — 玩家手动在 town/village 招兵
/// 依然 100% 走 vanilla 流程。
///
/// 不阻断 ST 自己的招兵：ST 的 <see cref="Recruitment.CapitalInPlaceRecruiter"/> 与
/// <see cref="Parties.StRecruiterPartyComponent"/>.RecruitFromTargetVillage 在调用 vanilla model
/// 之前包 <see cref="StRecruitContext.Enter"/>；本 model 检测到上下文 active 时无条件 fall-through
/// 到 base 实现 — 避免「自家受管 AI 的首府想招兵也被自己 ban 掉」的自锁。
///
/// 注意 gate 是基于 *buyer hero 所属 clan* 是否被 ST 管理，**不**是基于 seller 所在 settlement
/// 的 OwnerClan。因此被管 AI clan 的 lord 跑到第三方 / 玩家 / 未管 AI 的村庄招兵也照样阻断；
/// 反之，未管 AI clan 的 lord 跑到被管 AI clan 的 settlement 招兵不受影响。这是有意的：禁的是
/// 「受管 AI 的招兵能力」，不是「受管 settlement 的可招兵性」（settlement 这一侧由
/// <see cref="SovereignTowns.SettlementManagement.VanillaSuppressionManager"/> 处理 GarrisonAutoRecruitment）。
/// </summary>
public sealed class STVolunteerModel : DefaultVolunteerModel
{
    public override int MaximumIndexHeroCanRecruitFromHero(
        Hero buyerHero,
        Hero sellerHero,
        int useValueAsRelation)
    {
        try
        {
            // 1. ST 自己的招兵 → 无条件放行
            if (StRecruitContext.IsActive)
            {
                return base.MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, useValueAsRelation);
            }

            // 2. 防御性 null
            var buyerClan = buyerHero?.Clan;
            if (buyerClan == null)
            {
                return base.MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, useValueAsRelation);
            }

            // 3. 玩家氏族 → 不阻断
            if (buyerClan == Clan.PlayerClan)
            {
                return base.MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, useValueAsRelation);
            }

            // 4. 三联开关：任一关 → 走 vanilla
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat == null
                || !feat.ApplyToAiSettlementsToo
                || !feat.AutoRecruitment
                || !feat.SuppressVanillaGarrisonRecruitment)
            {
                return base.MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, useValueAsRelation);
            }

            // 5. buyerClan 必须是 ST 已接管的 AI clan（CapitalRegistry 注册的非玩家 clan）
            var registry = CapitalRegistry.Instance;
            if (registry == null || !registry.IsManagedClan(buyerClan))
            {
                return base.MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, useValueAsRelation);
            }

            // 6. 阻断：返回 -1。vanilla RecruitVolunteersFromNotable 内的 for (i=0; i<=maxIdx; i++)
            //    遇到 -1 直接跳过 — 不消费 notable.VolunteerTypes，不扣金币，不加兵。
            return -1;
        }
        catch (Exception ex)
        {
            // 任何意外都退回 base — 我们的 model override 绝不能让 vanilla 招兵循环抛异常。
            Logger.Error("STVolunteerModel.MaximumIndexHeroCanRecruitFromHero failed; falling through to base", ex);
            try
            {
                return base.MaximumIndexHeroCanRecruitFromHero(buyerHero, sellerHero, useValueAsRelation);
            }
            catch
            {
                // base 也炸了 — 等价于 vanilla 行为「无法招兵」。
                return -1;
            }
        }
    }

    /// <summary>
    /// Belt-and-suspenders：garrison 端 vanilla 招兵的 model gate。
    /// 现有 <see cref="SovereignTowns.SettlementManagement.VanillaSuppressionManager"/> 已通过
    /// 设置 <c>Town.GarrisonAutoRecruitmentIsEnabled=false</c> 截断了 garrison 招兵的上层 gate
    /// (vanilla <c>GarrisonRecruitmentCampaignBehavior.CanSettlementAutoRecruit</c>)，
    /// 但这里再加一层 model-side 检查：万一未来 vanilla 代码绕过 flag 直接调 model，本 override
    /// 仍能阻断「受管 AI clan settlement 的 garrison 从 notable 招兵」。
    /// </summary>
    public override int MaximumIndexGarrisonCanRecruitFromHero(Settlement settlement, Hero sellerHero)
    {
        try
        {
            if (StRecruitContext.IsActive)
            {
                return base.MaximumIndexGarrisonCanRecruitFromHero(settlement, sellerHero);
            }

            var ownerClan = settlement?.OwnerClan;
            if (ownerClan == null || ownerClan == Clan.PlayerClan)
            {
                return base.MaximumIndexGarrisonCanRecruitFromHero(settlement, sellerHero);
            }

            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat == null
                || !feat.ApplyToAiSettlementsToo
                || !feat.AutoRecruitment
                || !feat.SuppressVanillaGarrisonRecruitment)
            {
                return base.MaximumIndexGarrisonCanRecruitFromHero(settlement, sellerHero);
            }

            var registry = CapitalRegistry.Instance;
            if (registry == null || !registry.IsManagedClan(ownerClan))
            {
                return base.MaximumIndexGarrisonCanRecruitFromHero(settlement, sellerHero);
            }

            return -1;
        }
        catch (Exception ex)
        {
            Logger.Error("STVolunteerModel.MaximumIndexGarrisonCanRecruitFromHero failed; falling through to base", ex);
            try { return base.MaximumIndexGarrisonCanRecruitFromHero(settlement, sellerHero); }
            catch { return -1; }
        }
    }
}
