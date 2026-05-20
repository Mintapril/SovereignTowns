using System.Collections.Concurrent;
using TaleWorlds.CampaignSystem;

namespace SovereignTowns.Evaluators;

/// <summary>
/// 会话级 memoize 缓存，缓存对 <see cref="CharacterObject"/> 的纯函数判定结果，避免在调度热路径上
/// 对相同兵种重复进行升级树 DFS / FormationClass 派生。
///
/// 适用前提：CharacterObject 引用在一局游戏内由 vanilla MBObjectManager 静态注册后保持稳定，
/// 因此 ReferenceEqualityComparer（ConcurrentDictionary 默认对引用类型的比较即基于 Equals/HashCode，
/// CharacterObject 走默认 object 引用等价）天然吻合。
///
/// 生命周期：必须由 <c>SovereignTownsSubModule.OnGameStart</c> 在新游戏 / 读档前 <see cref="Reset"/>。
/// 跨局复用旧引用会导致旧 CharacterObject 永远 stale。
///
/// 线程安全：使用 <see cref="ConcurrentDictionary{TKey,TValue}"/>。Campaign 主循环虽然单线程，但
/// 极少数 GameModel 钩子可能在异步上下文中触发评估；ConcurrentDictionary 在 net472 上无显著性能
/// 损耗，比手动 lock 更稳。
/// </summary>
public static class EvaluatorCache
{
    /// <summary><see cref="TroopClassifier.IsNoble"/> 的 memoize 结果。</summary>
    public static readonly ConcurrentDictionary<CharacterObject, bool> IsNobleCache
        = new ConcurrentDictionary<CharacterObject, bool>();

    /// <summary>
    /// <see cref="TroopTemplateMatcher.CanUpgradeToTarget(CharacterObject, CharacterObject)"/> 的 memoize 结果。
    /// key 用 (source, target) 元组——避免显式 hashcode 编码，元组的内置 Equals/GetHashCode 已是 reference-equality
    /// 友好（CharacterObject 走默认 object equality）。
    /// </summary>
    public static readonly ConcurrentDictionary<(CharacterObject Source, CharacterObject Target), bool> CanUpgradeCache
        = new ConcurrentDictionary<(CharacterObject, CharacterObject), bool>();

    /// <summary><see cref="GenericTroopMatcher.GetRole"/> 的 memoize 结果。</summary>
    public static readonly ConcurrentDictionary<CharacterObject, GenericTroopRole> RoleCache
        = new ConcurrentDictionary<CharacterObject, GenericTroopRole>();

    /// <summary><see cref="GenericTroopMatcher.GetTierBucket"/> 的 memoize 结果（1..6 桶值）。</summary>
    public static readonly ConcurrentDictionary<CharacterObject, int> TierCache
        = new ConcurrentDictionary<CharacterObject, int>();

    /// <summary>
    /// 清空所有缓存。**必须** 在每次 <c>OnGameStart</c> 起点调用——切换游戏 / 读档时旧 CharacterObject
    /// 引用会被回收，新一局静态注册产生不同对象集合，旧缓存条目永远 stale。
    /// </summary>
    public static void Reset()
    {
        IsNobleCache.Clear();
        CanUpgradeCache.Clear();
        RoleCache.Clear();
        TierCache.Clear();
    }
}
