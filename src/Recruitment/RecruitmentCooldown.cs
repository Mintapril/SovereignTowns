using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.Recruitment;

/// <summary>
/// 进程内瞬态冷却表：记录每个 village settlement 最近一次被招募的 CampaignTime。
/// 不入存档（任务规格未要求；重启后冷却归零是可接受的次优行为，避免引入新 SaveableField 风险）。
/// 线程：仅在主线程（campaign tick）调用。Settlement 引用作 key 比 Village 更稳定（Village 是 component）。
/// </summary>
public static class RecruitmentCooldown
{
    private static readonly Dictionary<Settlement, CampaignTime> _lastRecruitAt = new();

    /// <summary>登记一次实际发生的招募（&gt;0 名）。失败/空跑不要登记。</summary>
    public static void MarkRecruited(Settlement villageSettlement)
    {
        if (villageSettlement == null) return;
        try
        {
            _lastRecruitAt[villageSettlement] = CampaignTime.Now;
            Logger.Debug($"RecruitmentCooldown.MarkRecruited: '{villageSettlement.Name}' at {CampaignTime.Now}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"RecruitmentCooldown.MarkRecruited failed for '{villageSettlement?.StringId}': {ex.Message}");
        }
    }

    /// <summary>
    /// 该 village 是否仍在冷却中（最近招募 &lt; <see cref="GlobalConfig.VillageCooldownHours"/>）。
    /// </summary>
    public static bool IsOnCooldown(Settlement villageSettlement)
    {
        if (villageSettlement == null) return false;
        try
        {
            if (!_lastRecruitAt.TryGetValue(villageSettlement, out var last)) return false;
            var cooldownHours = Math.Max(0, ConfigurationManager.Current?.VillageCooldownHours ?? 0);
            if (cooldownHours <= 0) return false;
            return last.ElapsedHoursUntilNow < cooldownHours;
        }
        catch (Exception ex)
        {
            Logger.Warn($"RecruitmentCooldown.IsOnCooldown failed for '{villageSettlement?.StringId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>清空全部冷却（CapitalManager 切换首府时可调用以避免跨首府误判）。</summary>
    public static void Clear()
    {
        try { _lastRecruitAt.Clear(); }
        catch (Exception ex) { Logger.Warn($"RecruitmentCooldown.Clear failed: {ex.Message}"); }
    }
}
