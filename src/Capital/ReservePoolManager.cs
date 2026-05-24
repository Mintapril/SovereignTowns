using System;
using System.Collections.Generic;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Capital;

/// <summary>
/// PR-6：B 池（Capital Reserve Pool）生命周期管理器。
/// 每次 <see cref="OnSessionLaunched"/> 调用时：
///   1. 扫描每座受管首府的 <see cref="Settlement.Parties"/>，找到存量 B 池 party（恢复自存档）；
///   2. 没有存量 B 池的首府 → 调 <see cref="StGarrisonReservePartyComponent.CreateForCapital"/> 新建。
/// 维护一张 <c>Settlement → MobileParty</c> 字典，供上层（未来 MCMF 调度器 PR-7）按首府查 B 池。
///
/// 不追踪进 <see cref="SovereignTowns.Lifecycle.PartyLifecycleManager"/>：B 池永驻首府，不参与
/// 空闲检测或 force-return 逻辑（详见 <see cref="StGarrisonReservePartyComponent"/> 注释）。
/// </summary>
public sealed class ReservePoolManager
{
    private readonly CapitalRegistry _capitalRegistry;

    /// <summary>首府 → B 池 party 映射（运行时缓存，非持久化）。</summary>
    private readonly Dictionary<Settlement, MobileParty> _pools = new Dictionary<Settlement, MobileParty>();

    public ReservePoolManager(CapitalRegistry capitalRegistry)
    {
        _capitalRegistry = capitalRegistry ?? throw new ArgumentNullException(nameof(capitalRegistry));
    }

    // ── 生命周期 ──

    /// <summary>
    /// OnSessionLaunched 时调用：扫描所有受管首府，为缺失 B 池的首府新建 party。
    /// 存档加载场景：B 池 party 已由 vanilla SaveSystem 通过 [SaveableField] 反序列化进
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
                    Logger.Error($"ReservePoolManager.Initialize: EnsurePoolForManager failed for clan '{mgr.OwnerClan?.StringId}'", ex);
                }
            }
            Logger.Info($"ReservePoolManager: initialized — {_pools.Count} B-pool(s) active");
        }
        catch (Exception ex)
        {
            Logger.Error("ReservePoolManager.Initialize failed", ex);
        }
    }

    /// <summary>
    /// <see cref="CapitalRegistry.OnSettlementOwnerChanged"/> 后调用：新首府若没有 B 池则建立；
    /// 旧首府的 B 池留给新 owner（battle loot 自然转移），不主动销毁（PR-8 再处理）。
    /// 此处仅保证"当前所有受管首府均有 B 池"。
    /// </summary>
    public void OnSettlementOwnerChanged()
    {
        try
        {
            foreach (var mgr in _capitalRegistry.AllManagers)
            {
                try { EnsurePoolForManager(mgr); }
                catch (Exception ex)
                {
                    Logger.Warn($"ReservePoolManager.OnSettlementOwnerChanged: EnsurePoolForManager failed for clan '{mgr.OwnerClan?.StringId}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ReservePoolManager.OnSettlementOwnerChanged failed", ex);
        }
    }

    /// <summary>
    /// 卸载：清空映射（party 实例本身由 vanilla 持久化管理，不主动销毁）。
    /// </summary>
    public void Uninstall()
    {
        _pools.Clear();
        Logger.Info("ReservePoolManager: uninstalled");
    }

    // ── 查询 API（供 PR-7 MCMF 调度器使用） ──

    /// <summary>按首府 settlement 获取对应 B 池 party，不存在返回 null。</summary>
    public MobileParty? GetPool(Settlement capital)
    {
        if (capital == null) return null;
        _pools.TryGetValue(capital, out var party);
        return party;
    }

    /// <summary>
    /// PR-7：从 <see cref="ReserveRecruiterPartyComponent.OnArrivedHome"/> 的静态上下文访问。
    /// OnArrivedHome 只有 MobileParty self 引用，无 dispatcher 上下文；用 CampaignBehavior 的
    /// 静态 Instance accessor 路由到当前活跃 ReservePoolManager。
    /// </summary>
    internal static MobileParty? GetPoolStatic(Settlement capital)
    {
        try
        {
            return SovereignTowns.Campaign.SovereignTownsCampaignBehavior.Instance
                       ?._reservePoolManager?.GetPool(capital);
        }
        catch { return null; }
    }

    // ── 内部 ──

    /// <summary>
    /// 确保指定 CapitalManager 管辖的首府有且仅有一个 B 池 party。
    /// 优先从 settlement.Parties 中找到存量（存档恢复场景），找不到则新建。
    /// </summary>
    private void EnsurePoolForManager(CapitalManager mgr)
    {
        var capital = mgr.GetCapitalSettlement();
        if (capital == null) return;

        // 若字典中已有此首府的 B 池且仍活跃，直接返回。
        if (_pools.TryGetValue(capital, out var existing) && existing?.IsActive == true)
            return;

        // 从 settlement.Parties 扫描存量 B 池（存档加载场景）。
        MobileParty? found = null;
        foreach (var party in capital.Parties)
        {
            if (party?.PartyComponent is StGarrisonReservePartyComponent)
            {
                found = party;
                break;
            }
        }

        if (found != null)
        {
            _pools[capital] = found;
            Logger.Info($"ReservePoolManager: restored B-pool '{found.StringId}' for capital '{capital.StringId}'");
            return;
        }

        // 新建 B 池 party。
        var newPool = StGarrisonReservePartyComponent.CreateForCapital(capital);
        if (newPool != null)
        {
            _pools[capital] = newPool;
            Logger.Info($"ReservePoolManager: created B-pool '{newPool.StringId}' for capital '{capital.StringId}'");
        }
        else
        {
            Logger.Error($"ReservePoolManager: failed to create B-pool for capital '{capital.StringId}'");
        }
    }
}
