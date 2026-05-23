using System;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.SettlementManagement;

/// <summary>
/// B7.14: 关闭 vanilla 在我们接管的城镇/城堡上的「自动招募 + 自动壮大驻军党」行为，
/// 让 RecruitingParty 成为本 Mod 范围内驻军唯一的兵源。
///
/// 实际机制：把 <see cref="Town.GarrisonAutoRecruitmentIsEnabled"/> 设为 <c>false</c>。
/// 该 vanilla flag 是关闭驻军自动招募的 surgical switch（见 vanilla 反编译
/// <c>RecruitmentCampaignBehavior.HourlyTickParty</c> 中的早期检查）。
/// 该 flag 关闭后：
///   - vanilla <c>RecruitmentCampaignBehavior</c> 不再把 notable 的 <c>VolunteerTypes</c> 自动拉进 GarrisonParty；
///   - vanilla 也不再自动把 GarrisonParty 内的兵种升级。
/// 不影响：notable 自身的 VolunteerTypes 仍正常每小时刷新（这是玩家手动招兵 + 本 Mod RecruitingParty 的供给源）、
/// 以及 vanilla 民兵每日生成。
///
/// **AI lord 招兵阻断**（2026-05-19 配套）：开启 <see cref="EnabledFeatures.ApplyToAiSettlementsToo"/> 时
/// 本类只处理 settlement 端的 garrison flag。AI lord 个人在 settlement 内通过 vanilla
/// <c>RecruitmentCampaignBehavior.RecruitVolunteersFromNotable</c> 招兵的路径由
/// <see cref="SovereignTowns.Models.STVolunteerModel"/> 接管阻断 — 后者基于 buyerHero 的 clan 判断，
/// 与本类的 settlement-side flag 形成两段防线（settlement → garrison；lord → buyer-side）。
/// 阻断后受管 AI clan 唯有 ST mod 渠道（CapitalInPlaceRecruiter / RecruitingParty /
/// PrisonerRecruitmentManager）能补兵。
///
/// 覆盖范围由 <see cref="EnabledFeatures.SuppressVanillaGarrisonRecruitment"/> 和
/// <see cref="EnabledFeatures.ApplyToAiSettlementsToo"/> 联合决定：
///   - SuppressVanillaGarrisonRecruitment = false 或 AutoRecruitment = false → 还原 vanilla flag；
///   - ApplyToAiSettlementsToo = false  → 仅玩家氏族且有可用首府时，覆盖玩家 Town / Castle；
///   - ApplyToAiSettlementsToo = true   → 所有已由 CapitalRegistry 接管且有可用首府的 clan。
/// 没有首府的 clan 不 suppress vanilla，否则城堡-only/流亡状态会失去所有补兵来源。
///
/// 当某城镇易主：
///   - 落到受管氏族手上 → 立刻按规则禁用 vanilla flag；
///   - 离开受管范围 → 把 flag 还原为 true，避免 AI 阵营被永久阉割。
/// </summary>
public sealed class VanillaSuppressionManager
{
    private bool _initialized;

    /// <summary>
    /// 全局单例引用，让 WebConfigEndpoints 在 PUT /api/config / POST /api/reload 后能调到
    /// <see cref="ApplyToAllSettlements"/>，避免 toggle 后需要重启游戏。
    /// CampaignBehavior 析构后置 null。
    /// </summary>
    public static VanillaSuppressionManager? Instance { get; private set; }

    /// <summary>OnSessionLaunched 时由 CampaignBehavior 调用：扫描一遍当前所有 Settlement 应用 flag。</summary>
    public void Initialize()
    {
        try
        {
            if (_initialized) return;

            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(
                this, OnSettlementOwnerChanged);

            ApplyToAllSettlements();
            Instance = this;
            _initialized = true;

            var feat = ConfigurationManager.Current?.EnabledFeatures;
            Logger.Info(
                $"VanillaSuppressionManager: initialized " +
                $"(Suppress={feat?.SuppressVanillaGarrisonRecruitment} " +
                $"ApplyToAi={feat?.ApplyToAiSettlementsToo})");
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaSuppressionManager.Initialize failed", ex);
        }
    }

    /// <summary>遍历 Settlement.All，按当前配置把 flag 设为合适值。供 Initialize + 手动 reapply 用。</summary>
    public void ApplyToAllSettlements()
    {
        try
        {
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat is null) return;
            if (!ShouldSuppressVanilla(feat))
            {
                // 用户把抑制特性关了，或 AutoRecruitment 未启用：把曾经被我们关掉的 flag 恢复。
                if (feat.SuppressVanillaGarrisonRecruitment && !feat.AutoRecruitment)
                {
                    Logger.Info("VanillaSuppressionManager: AutoRecruitment=false, restoring vanilla garrison auto recruitment");
                }
                RestoreAll();
                return;
            }

            // AI 接管范围与玩家一致：首府 / 驻军 / 招募 / 调拨 / 出击 / 俘虏 / 巡逻。
            if (feat.ApplyToAiSettlementsToo)
            {
                string msg = new TextObject(
                    "{=ST_Msg_AiOptIn}[Sovereign Towns] AI clans that own a capital are now managed by Sovereign Towns for capital / garrison / recruitment / transfer / sally / prisoner / patrol; AI recruitment is restricted to same-culture troops.").ToString();
                Logger.Info(msg);
                try { InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Green)); }
                catch { /* swallow — 启动早期 InformationManager 不一定可用 */ }
            }

            int affected = 0;
            int restored = 0;
            foreach (var settlement in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
            {
                if (settlement is null) continue;
                if (!(settlement.IsTown || settlement.IsCastle)) continue;
                var town = settlement.Town;
                if (town is null) continue;

                bool inScope = ShouldSuppressSettlement(feat, settlement);

                if (inScope)
                {
                    if (town.GarrisonAutoRecruitmentIsEnabled)
                    {
                        town.GarrisonAutoRecruitmentIsEnabled = false;
                        affected++;
                    }
                }
                else
                {
                    // 不在范围内（AI 拥有 + 不全图覆盖）→ 确保 flag 是 true，
                    // 防止之前曾经覆盖全图、然后用户关掉 ApplyToAi 导致 AI 永久残废。
                    if (!town.GarrisonAutoRecruitmentIsEnabled)
                    {
                        town.GarrisonAutoRecruitmentIsEnabled = true;
                        restored++;
                    }
                }
            }
            Logger.Info($"VanillaSuppressionManager.ApplyToAllSettlements: suppressed={affected} restored={restored}");
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaSuppressionManager.ApplyToAllSettlements failed", ex);
        }
    }

    /// <summary>用户在网页面板里关掉 SuppressVanillaGarrisonRecruitment 后调用，把全图 flag 还原。</summary>
    public void RestoreAll()
    {
        try
        {
            int restored = 0;
            foreach (var settlement in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
            {
                if (settlement is null) continue;
                if (!(settlement.IsTown || settlement.IsCastle)) continue;
                var town = settlement.Town;
                if (town is null) continue;
                if (!town.GarrisonAutoRecruitmentIsEnabled)
                {
                    town.GarrisonAutoRecruitmentIsEnabled = true;
                    restored++;
                }
            }
            Logger.Info($"VanillaSuppressionManager.RestoreAll: restored={restored}");
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaSuppressionManager.RestoreAll failed", ex);
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
            if (settlement is null) return;
            if (!(settlement.IsTown || settlement.IsCastle)) return;
            var town = settlement.Town;
            if (town is null) return;

            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat is null) return;
            if (!ShouldSuppressVanilla(feat))
            {
                if (!town.GarrisonAutoRecruitmentIsEnabled)
                {
                    town.GarrisonAutoRecruitmentIsEnabled = true;
                    Logger.Info($"VanillaSuppressionManager: '{settlement.StringId}' 当前未启用 ST 自动招募，还原 vanilla 自动招募");
                }
                return;
            }

            bool inScope = ShouldSuppressSettlement(feat, settlement);

            if (inScope)
            {
                if (town.GarrisonAutoRecruitmentIsEnabled)
                {
                    town.GarrisonAutoRecruitmentIsEnabled = false;
                    Logger.Info($"VanillaSuppressionManager: '{settlement.StringId}' 易主进入范围，禁用 vanilla 自动招募");
                }
            }
            else
            {
                if (!town.GarrisonAutoRecruitmentIsEnabled)
                {
                    town.GarrisonAutoRecruitmentIsEnabled = true;
                    Logger.Info($"VanillaSuppressionManager: '{settlement.StringId}' 离开范围（落入 AI），还原 vanilla 自动招募");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaSuppressionManager.OnSettlementOwnerChanged failed", ex);
        }
    }

    private static bool IsPlayerOwned(Settlement settlement)
    {
        try
        {
            var clan = settlement?.OwnerClan;
            if (clan is null) return false;
            return clan == Clan.PlayerClan;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldSuppressVanilla(EnabledFeatures feat)
        => feat.SuppressVanillaGarrisonRecruitment && feat.AutoRecruitment;

    private static bool ShouldSuppressSettlement(EnabledFeatures feat, Settlement settlement)
    {
        try
        {
            if (!ShouldSuppressVanilla(feat)) return false;
            var ownerClan = settlement?.OwnerClan;
            if (ownerClan == null) return false;

            if (!feat.ApplyToAiSettlementsToo && ownerClan != Clan.PlayerClan)
            {
                return false;
            }

            return HasUsableCapital(ownerClan);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasUsableCapital(Clan clan)
    {
        try
        {
            var registry = CapitalRegistry.Instance;
            if (registry != null)
            {
                return registry.IsManagedClanWithCapital(clan);
            }

            // Early fallback for defensive use before registry.Instance is assigned.
            return clan == Clan.PlayerClan && ClanOwnsAnyTown(clan);
        }
        catch
        {
            return false;
        }
    }

    private static bool ClanOwnsAnyTown(Clan clan)
    {
        try
        {
            foreach (var town in Town.AllTowns)
            {
                if (town != null && town.IsTown && town.OwnerClan == clan) return true;
            }
        }
        catch { }
        return false;
    }
}
