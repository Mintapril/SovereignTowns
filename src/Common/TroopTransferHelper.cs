using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

/// <summary>
/// 共享：从一个 TroopRoster 抽兵到另一个 TroopRoster + 反向归并的 helper。
///
/// 抽兵：SallyForth / Patrol / Recruitment (escort) 3 处共享逻辑，原有 95% 重复实现已合并。
/// 归并：SallyForth 出击失败回滚 / Recruitment 派遣失败回滚共享。
///
/// 不变量（R1：原子语义）：
///   - skip heroes（IsHero == true）—— heroes 永远不通过 TroopRoster.AddToCounts 转移；
///   - 单元素双写采用 "target-first + 失败回滚 target"：
///       1) target.AddToCounts(+take) 若抛 → 全无副作用；
///       2) source.AddToCounts(-take) 若抛 → 立即对 target -take 回滚；
///     保证失败时兵员留在 source roster，**绝不蒸发也不复制**；
///   - 不抛异常（所有 vanilla API 调用 try/catch）；
///   - 排序方向参数化（HighestTierFirst / LowestTierFirst），生产测试过。
/// </summary>
public static class TroopTransferHelper
{
    public enum SortStrategy
    {
        /// <summary>优先抽低 Tier（Patrol / Recruitment 护卫：保留城内精锐）。</summary>
        LowestTierFirst,
        /// <summary>优先抽高 Tier（SallyForth：精锐先出击）。</summary>
        HighestTierFirst,
    }

    /// <summary>
    /// 从 <paramref name="source"/> roster 抽最多 <paramref name="desiredCount"/> 名非 Hero 兵员到
    /// <paramref name="target"/> roster。返回实际转移人数。
    /// 失败时兵员留在 source 不蒸发。
    /// </summary>
    /// <param name="source">兵源 roster（通常为 GarrisonParty.MemberRoster）。</param>
    /// <param name="target">目标 roster（通常为 sally/patrol/escort 的 MemberRoster 或 dummy TroopRoster）。</param>
    /// <param name="desiredCount">期望抽取人数；&lt;= 0 直接返回 0。</param>
    /// <param name="sort">兵种排序策略（高 Tier 先 / 低 Tier 先）。</param>
    /// <param name="filter">可选附加过滤：返回 true 才参与抽取。null = 无附加过滤。Hero 已硬过滤。</param>
    public static int TransferFromGarrison(
        TroopRoster source,
        TroopRoster target,
        int desiredCount,
        SortStrategy sort = SortStrategy.LowestTierFirst,
        Func<CharacterObject, bool>? filter = null)
    {
        try
        {
            if (source == null || target == null || desiredCount <= 0) return 0;

            int remaining = desiredCount;
            int moved = 0;

            // 拷贝快照，避免迭代时修改
            var snapshot = new List<TroopRosterElement>(source.GetTroopRoster());
            // 排序：HighestTierFirst → 高 Tier 优先；LowestTierFirst → 低 Tier 优先
            snapshot.Sort((a, b) =>
            {
                var ta = a.Character?.Tier ?? 0;
                var tb = b.Character?.Tier ?? 0;
                return sort == SortStrategy.HighestTierFirst
                    ? tb.CompareTo(ta)
                    : ta.CompareTo(tb);
            });

            foreach (var elem in snapshot)
            {
                if (remaining <= 0) break;
                if (elem.Character == null || elem.Character.IsHero) continue;
                if (elem.Number <= 0) continue;
                if (filter != null && !filter(elem.Character)) continue;

                int take = Math.Min(elem.Number, remaining);
                // R1：先 target +take（失败→无副作用），再 source -take（失败→target -take 回滚）。
                bool targetAdded = false;
                try
                {
                    target.AddToCounts(elem.Character, take, false, 0, 0);
                    targetAdded = true;

                    source.AddToCounts(elem.Character, -take, false, 0, 0);

                    moved += take;
                    remaining -= take;
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"[TroopTransferHelper.TransferFromGarrison] per-element transfer failed (char='{elem.Character?.StringId}', n={take})", oneEx);
                    if (targetAdded)
                    {
                        try { target.AddToCounts(elem.Character, -take, false, 0, 0); }
                        catch (Exception undoEx)
                        {
                            Logger.Error($"[TroopTransferHelper.TransferFromGarrison] target rollback failed for '{elem.Character?.StringId}' — DUPLICATE TROOPS may exist", undoEx);
                        }
                    }
                }
            }
            return moved;
        }
        catch (Exception ex)
        {
            Logger.Error("[TroopTransferHelper.TransferFromGarrison] failed", ex);
            return 0;
        }
    }

    /// <summary>
    /// 把 <paramref name="source"/> roster 的全部非 Hero 兵员归并回 <paramref name="target"/> roster。
    /// 返回实际转移人数。失败时兵员留在 source 不蒸发。
    /// </summary>
    public static int TransferBackToGarrison(TroopRoster source, TroopRoster target)
    {
        try
        {
            if (source == null || target == null) return 0;

            int moved = 0;
            var snapshot = new List<TroopRosterElement>(source.GetTroopRoster());
            foreach (var elem in snapshot)
            {
                if (elem.Character == null || elem.Character.IsHero || elem.Number <= 0) continue;
                // R1：先 target += n（失败→无副作用），再 source -= n（失败→target 回滚 -n）。
                bool targetAdded = false;
                try
                {
                    target.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                    targetAdded = true;

                    source.AddToCounts(elem.Character, -elem.Number, false, 0, 0);

                    moved += elem.Number;
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"[TroopTransferHelper.TransferBackToGarrison] per-element rollback failed (char='{elem.Character?.StringId}')", oneEx);
                    if (targetAdded)
                    {
                        try { target.AddToCounts(elem.Character, -elem.Number, false, -elem.WoundedNumber, 0); }
                        catch (Exception undoEx)
                        {
                            Logger.Error($"[TroopTransferHelper.TransferBackToGarrison] target rollback failed for '{elem.Character?.StringId}' — DUPLICATE TROOPS may exist", undoEx);
                        }
                    }
                }
            }
            return moved;
        }
        catch (Exception ex)
        {
            Logger.Error("[TroopTransferHelper.TransferBackToGarrison] failed", ex);
            return 0;
        }
    }
}
