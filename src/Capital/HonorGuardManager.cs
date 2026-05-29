using System;
using System.Collections.Generic;
using SovereignTowns.Common;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Capital;

/// <summary>
/// PR-6：卫队（Capital Reserve Pool）生命周期管理器。
/// 每次 <see cref="OnSessionLaunched"/> 调用时：
///   1. 扫描每座受管首府的 <see cref="Settlement.Parties"/>，找到存量 卫队 party（恢复自存档）；
///   2. 没有存量 卫队的首府 → 调 <see cref="HonorGuardPartyComponent.CreateForCapital"/> 新建。
/// 维护一张 <c>Settlement → MobileParty</c> 字典，供上层（未来 MCMF 调度器 PR-7）按首府查 卫队。
///
/// 不追踪进 <see cref="SovereignTowns.Lifecycle.PartyLifecycleManager"/>：卫队永驻首府，不参与
/// 空闲检测或 force-return 逻辑（详见 <see cref="HonorGuardPartyComponent"/> 注释）。
/// </summary>
public sealed class HonorGuardManager
{
    private readonly CapitalRegistry _capitalRegistry;

    /// <summary>首府 → 卫队 party 映射（运行时缓存，非持久化）。</summary>
    private readonly Dictionary<Settlement, MobileParty> _pools = new Dictionary<Settlement, MobileParty>();

    /// <summary>卫队食物缓冲天数：每日把库存补足到这么多天用量，确保 <c>PartyBase.IsStarving</c> 恒为 false
    /// → vanilla 对驻扎要塞的卫队按 +5（基础）+10（In Settlement）/日自动痊愈伤兵（见 <see cref="OnDailyTick"/>）。</summary>
    private const float HonorGuardFoodBufferDays = 5f;

    public HonorGuardManager(CapitalRegistry capitalRegistry)
    {
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
    }

    // ── 生命周期 ──

    /// <summary>
    /// OnSessionLaunched 时调用：扫描所有受管首府，为缺失 卫队的首府新建 party。
    /// 存档加载场景：卫队 party 已由 vanilla SaveSystem 通过 [SaveableField] 反序列化进
    /// settlement.Parties，本方法扫描后直接注册，不重复创建。
    /// </summary>
    public void Initialize()
    {
        try
        {
            _pools.Clear();
            foreach (var mgr in _capitalRegistry.AllManagers)
            {
                try
                {
                    EnsurePoolForManager(mgr);
                }
                catch (Exception ex)
                {
                    Logger.Error($"HonorGuardManager.Initialize: EnsurePoolForManager failed for clan '{mgr.OwnerClan?.StringId}'", ex);
                }
            }
            Logger.Info($"HonorGuardManager: initialized — {_pools.Count} honor-guard(s) active");
        }
        catch (Exception ex)
        {
            Logger.Error("HonorGuardManager.Initialize failed", ex);
        }
    }

    /// <summary>
    /// 每日维护（由 <see cref="SovereignTowns.Campaign.SovereignTownsCampaignBehavior.OnDailyTick"/> 调用）：
    /// 给每座首府的卫队**免费**补足食物。卫队是常驻首府的「驻军等价」独立 MobileParty，刻意不继承
    /// <c>GarrisonPartyComponent</c>（规避军饷 / vanilla AI），因此失去了 vanilla 驻军「由 settlement 免费供养、
    /// 永不饿死」的待遇。若不喂食，<c>PartyBase.IsStarving</c> 为真 → <c>DefaultPartyHealingModel</c> 对非驻军
    /// 返回负 healing → <c>PartyHealCampaignBehavior</c> 把健康兵逐日转成伤兵且永不痊愈（2026-05-30 反编译实证）。
    /// 喂饱后卫队驻扎要塞 → vanilla 自动按 +5+10/日痊愈，无需本 mod 手动 heal。
    /// </summary>
    public void OnDailyTick()
    {
        try
        {
            foreach (var kv in _pools)
            {
                var party = kv.Value;
                if (party == null || !party.IsActive) continue;
                try { PartyEconomyHelper.TopUpFoodFree(party, HonorGuardFoodBufferDays); }
                catch (Exception ex)
                {
                    Logger.Warn($"HonorGuardManager.OnDailyTick: feed failed for '{party.StringId}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HonorGuardManager.OnDailyTick failed", ex);
        }
    }

    /// <summary>
    /// <see cref="CapitalRegistry.OnSettlementOwnerChanged"/> 后调用：
    ///   1. 销毁所有"key 不再是任何受管首府"的孤立 卫队（覆盖围城失守、礼赠、交换等所有易主路径）。
    ///   2. 为当前所有受管首府确保 卫队存在。
    ///
    /// ── PR-8 (2026-05-24) 反编译核实 ──
    /// vanilla Town.GetDefenderParties 通过 Settlement.Parties 枚举防御方，MapFaction 取自
    /// HomeSettlement.OwnerClan.MapFaction。卫队被攻克后若不销毁，MapFaction 会随新 OwnerClan
    /// 翻转 → 卫队部队自动效忠攻击方，语义错误。故失守时必须销毁旧 卫队。
    /// </summary>
    public void OnSettlementOwnerChanged()
    {
        try
        {
            // 步骤 1：收集当前受管首府集合。
            var managedCapitals = new HashSet<Settlement>();
            foreach (var mgr in _capitalRegistry.AllManagers)
            {
                var cap = mgr.GetCapitalSettlement();
                if (cap != null) managedCapitals.Add(cap);
            }

            // 步骤 2：销毁已不属于任何受管首府的孤立 卫队。
            var toDestroy = new List<Settlement>();
            foreach (var kv in _pools)
            {
                if (!managedCapitals.Contains(kv.Key))
                    toDestroy.Add(kv.Key);
            }
            foreach (var orphan in toDestroy)
            {
                DestroyPool(orphan);
            }

            // 步骤 3：为当前所有受管首府确保 卫队存在。
            foreach (var mgr in _capitalRegistry.AllManagers)
            {
                try { EnsurePoolForManager(mgr); }
                catch (Exception ex)
                {
                    Logger.Warn($"HonorGuardManager.OnSettlementOwnerChanged: EnsurePoolForManager failed for clan '{mgr.OwnerClan?.StringId}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HonorGuardManager.OnSettlementOwnerChanged failed", ex);
        }
    }

    /// <summary>
    /// PR-8：销毁指定首府的 卫队 party（LeaveSettlement → DestroyPartyAction）。
    /// 幂等：若该首府没有已注册 卫队则静默返回。
    /// </summary>
    public void DestroyPool(Settlement capital)
    {
        if (capital == null) return;
        if (!_pools.TryGetValue(capital, out var party))
        {
            Logger.Info($"HonorGuardManager.DestroyPool: no registered pool for '{capital.StringId}', nothing to do");
            return;
        }

        _pools.Remove(capital);

        if (party == null || !party.IsActive)
        {
            Logger.Info($"HonorGuardManager.DestroyPool: pool party for '{capital.StringId}' already inactive, removed from registry only");
            return;
        }

        try
        {
            // 卫队永驻首府（CurrentSettlement = capital）；DestroyPartyAction 不自动 Leave → 先显式 LeaveSettlement。
            if (party.CurrentSettlement != null)
            {
                try { LeaveSettlementAction.ApplyForParty(party); }
                catch (Exception leaveEx) { Logger.Warn($"HonorGuardManager.DestroyPool: LeaveSettlementAction failed for '{capital.StringId}' (continuing): {leaveEx.Message}"); }
            }
            DestroyPartyAction.Apply(null, party);
            Logger.Info($"HonorGuardManager: honor-guard destroyed for capital '{capital.StringId}' (owner changed)");
        }
        catch (Exception ex)
        {
            Logger.Error($"HonorGuardManager.DestroyPool: DestroyPartyAction failed for '{capital.StringId}'", ex);
        }
    }

    /// <summary>
    /// 卸载：清空映射（party 实例本身由 vanilla 持久化管理，不主动销毁）。
    /// </summary>
    public void Uninstall()
    {
        _pools.Clear();
        Logger.Info("HonorGuardManager: uninstalled");
    }

    // ── 查询 API（供 PR-7 MCMF 调度器使用） ──

    /// <summary>按首府 settlement 获取对应 卫队 party，不存在返回 null。</summary>
    public MobileParty? GetPool(Settlement capital)
    {
        if (capital == null) return null;
        _pools.TryGetValue(capital, out var party);
        return party;
    }

    /// <summary>
    /// 从 <see cref="HonorGuardRecruiterPartyComponent.OnArrivedHome"/> 的静态上下文访问。
    /// OnArrivedHome 只有 MobileParty self 引用，无 dispatcher 上下文；用 CampaignBehavior 的
    /// 静态 Instance accessor 路由到当前活跃 HonorGuardManager。
    /// </summary>
    internal static MobileParty? GetPoolStatic(Settlement capital)
    {
        try
        {
            return SovereignTowns.Campaign.SovereignTownsCampaignBehavior.Instance
                       ?._honorGuardManager?.GetPool(capital);
        }
        catch { return null; }
    }

    // ── 内部 ──

    /// <summary>
    /// 确保指定 CapitalManager 管辖的首府有且仅有一个 卫队 party。
    /// 优先从 settlement.Parties 中找到存量（存档恢复场景），找不到则新建。
    /// </summary>
    private void EnsurePoolForManager(CapitalManager mgr)
    {
        var capital = mgr.GetCapitalSettlement();
        if (capital == null) return;

        // 若字典中已有此首府的 卫队且仍活跃，直接返回。
        if (_pools.TryGetValue(capital, out var existing) && existing?.IsActive == true)
            return;

        // 从 settlement.Parties 扫描存量 卫队（存档加载场景）。
        MobileParty? found = null;
        foreach (var party in capital.Parties)
        {
            if (party?.PartyComponent is HonorGuardPartyComponent)
            {
                found = party;
                break;
            }
        }

        if (found != null)
        {
            _pools[capital] = found;
            Logger.Info($"HonorGuardManager: restored honor-guard '{found.StringId}' for capital '{capital.StringId}'");
            return;
        }

        // 新建 卫队 party。
        var newPool = HonorGuardPartyComponent.CreateForCapital(capital);
        if (newPool != null)
        {
            _pools[capital] = newPool;
            Logger.Info($"HonorGuardManager: created honor-guard '{newPool.StringId}' for capital '{capital.StringId}'");
        }
        else
        {
            Logger.Error($"HonorGuardManager: failed to create honor-guard for capital '{capital.StringId}'");
        }
    }
}
