using System;
using TaleWorlds.CampaignSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// 改 <c>Clan.Gold</c> 的统一入口。
///
/// 2026-05-23 反编译事实（CLAUDE.md #6）：
///   - vanilla <c>Clan.Gold</c> 是 expression-bodied get-only property：<c>=&gt; Leader?.Gold ?? 0</c>
///   - 没有 setter；Clan 类上不存在 <c>set_Gold</c> 方法
///   - 早期版本的"反射拿 Clan.Gold setter"路径必然 <c>GetSetMethod(true) == null</c> →
///     所有 Set/Change 都是 no-op（reflection 实测 PropertyInfo.CanWrite=False 已确认）
///
/// 正确实现：因 <c>Clan.Gold ≡ Leader.Gold</c>，直接走 vanilla 公开 API
/// <see cref="Hero.ChangeHeroGold(int)"/>，把 delta 应用到 leader 即可。
/// Hero.ChangeHeroGold 内部已 saturate 到 int.MaxValue 上限，并通过 Hero.Gold setter
/// clamp 到 0 下限（<c>_gold = MathF.Max(0, value)</c>）——与 vanilla
/// <c>GiveGoldAction.ApplyBetweenCharacters(null, leader, num)</c>（DailyTickClan 用的同一路径）
/// 行为一致。
/// </summary>
internal static class ClanGoldAccess
{
    /// <summary>
    /// 把 <paramref name="delta"/> 应用到 <paramref name="clan"/>.Leader.Gold（可负）。
    /// Hero.ChangeHeroGold 内部 saturate 上限到 int.MaxValue；下限被 Hero.Gold setter
    /// clamp 到 0（与 vanilla 行为一致：clan 不会真有负余额）。
    /// </summary>
    /// <returns>true 表示已应用到 leader（含 delta=0 短路）；false 表示 clan/Leader 为 null。</returns>
    public static bool Change(Clan? clan, int delta)
    {
        var leader = clan?.Leader;
        if (leader == null)
        {
            Logger.Warn($"ClanGoldAccess.Change: clan/Leader is null (delta={delta})");
            return false;
        }
        if (delta == 0) return true;
        try
        {
            leader.ChangeHeroGold(delta);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"ClanGoldAccess.Change failed (clan={clan?.StringId} leader={leader.StringId} delta={delta})", ex);
            return false;
        }
    }
}
