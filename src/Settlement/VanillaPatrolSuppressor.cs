using System;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.SettlementManagement;

/// <summary>
/// 禁用受管氏族定居点的 vanilla 巡逻队(哨所自带巡逻)。
/// 创建端由 <see cref="SovereignTowns.Patches.PatrolSpawnSuppressionPatch"/>(Harmony 前缀)拦截;
/// 本类负责存量清理 + 易主时清理,并提供 <see cref="ShouldSuppressPatrolFor"/> 供该前缀判定。
/// 范围与 VanillaSuppressionManager 一致(受管 clan + 有可用首府),额外要求 EnabledFeatures.AutoPatrol。
/// 全部方法 try-catch,绝不抛回 vanilla。
/// </summary>
public sealed class VanillaPatrolSuppressor
{
    private bool _initialized;

    /// <summary>全局单例,供 WebConfig 热应用时调 <see cref="DissolveAllManagedVanillaPatrols"/>。</summary>
    public static VanillaPatrolSuppressor? Instance { get; private set; }

    /// <summary>OnSessionLaunched 时由 CampaignBehavior 调用。</summary>
    public void Initialize()
    {
        try
        {
            if (_initialized) return;
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            DissolveAllManagedVanillaPatrols();
            Instance = this;
            _initialized = true;
            Logger.Info("VanillaPatrolSuppressor: initialized");
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaPatrolSuppressor.Initialize failed", ex);
        }
    }

    /// <summary>
    /// Harmony 前缀调用:该 settlement 是否应禁止 vanilla 生成新巡逻队。
    /// 跑在 vanilla 热路径上 — 保持轻量,绝不抛。
    /// </summary>
    public static bool ShouldSuppressPatrolFor(Settlement? settlement)
    {
        try
        {
            if (settlement == null) return false;
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat == null || !feat.AutoPatrol) return false;
            return IsManagedSettlement(settlement);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>遍历所有 vanilla 巡逻队,解散归属受管定居点的。供 Initialize + 配置热应用用。</summary>
    public void DissolveAllManagedVanillaPatrols()
    {
        try
        {
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat == null || !feat.AutoPatrol) return;

            int dissolved = 0;
            foreach (var mp in MobileParty.AllPatrolParties.ToList())
            {
                try
                {
                    var home = mp?.HomeSettlement;
                    if (home == null || !IsManagedSettlement(home)) continue;
                    DissolveParty(mp);
                    dissolved++;
                }
                catch (Exception inner)
                {
                    Logger.Error("VanillaPatrolSuppressor: dissolving one patrol failed", inner);
                }
            }
            if (dissolved > 0)
                Logger.Info($"VanillaPatrolSuppressor: dissolved {dissolved} vanilla patrol parties on managed settlements");
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaPatrolSuppressor.DissolveAllManagedVanillaPatrols failed", ex);
        }
    }

    private void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero newOwner,
        Hero oldOwner,
        Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        try
        {
            if (settlement == null) return;
            if (!ShouldSuppressPatrolFor(settlement)) return;
            // 易主进入受管范围:解散该定居点现存的 vanilla 巡逻队。
            foreach (var mp in MobileParty.AllPatrolParties.ToList())
            {
                try
                {
                    if (mp?.HomeSettlement == settlement) DissolveParty(mp);
                }
                catch (Exception inner)
                {
                    Logger.Error("VanillaPatrolSuppressor: post-ownership dissolve failed", inner);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaPatrolSuppressor.OnSettlementOwnerChanged failed", ex);
        }
    }

    /// <summary>仿 vanilla PatrolPartiesCampaignBehavior.RemoveSettlementParties 的安全解散。</summary>
    private static void DissolveParty(MobileParty mp)
    {
        if (mp == null) return;
        mp.MapEventSide = null;
        if (mp.IsActive)
            DestroyPartyAction.Apply(null, mp);
    }

    private static bool IsManagedSettlement(Settlement settlement)
    {
        try
        {
            if (!(settlement.IsTown || settlement.IsCastle)) return false;
            var ownerClan = settlement.OwnerClan;
            if (ownerClan == null) return false;

            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat != null && !feat.ApplyToAiSettlementsToo && ownerClan != Clan.PlayerClan)
                return false;

            var registry = CapitalRegistry.Instance;
            if (registry != null)
                return registry.IsManagedClanWithCapital(ownerClan);
            return ownerClan == Clan.PlayerClan;
        }
        catch
        {
            return false;
        }
    }
}
