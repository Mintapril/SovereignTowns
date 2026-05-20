using System;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace SovereignTowns.Audit;

/// <summary>
/// 决策叙述层 — 把 DecisionAuditLogger 的技术决策(decisionType + decisionJson)
/// 翻译成 Bannerlord 玩家能看懂的一句话 + 色调。供 ActivityFeed 使用。
///
/// 在 DecisionAuditLogger.LogRule 内同步调用 → 主线程,因此可安全解析 settlement.StringId
/// 为城市名(MBObjectManager.GetObject)。首府事件的 decisionJson 直接带城市名,无需解析。
/// 任何异常都吞掉返回 null(跳过该条) — 叙述是辅助功能,绝不影响审计主路径。
/// </summary>
internal static class ActivityNarrator
{
    private static bool? _isZh;

    private static bool IsChinese
    {
        get
        {
            if (_isZh == null)
            {
                try { _isZh = new TextObject("{=ST_WebUiLang}en").ToString() == "zh"; }
                catch { _isZh = false; }
            }
            return _isZh.Value;
        }
    }

    private static string Tr(string zh, string en) => IsChinese ? zh : en;

    /// <summary>
    /// 翻译一条已执行决策。返回 (色调, 文本);返回 null 表示此决策不进活动流
    /// (如 mod_expense/mod_refund 属财务、未知类型)。
    /// </summary>
    public static (string tone, string text)? Narrate(string decisionType, string decisionJson)
    {
        try
        {
            JObject j = JObject.Parse(string.IsNullOrEmpty(decisionJson) ? "{}" : decisionJson);

            switch (decisionType)
            {
                case "RecruitFromVillage":
                {
                    string village = Place((string?)j["village"]);
                    int n = Int(j, "recruited");
                    return ("ok", Tr($"在 {village} 招募了 {n} 名士兵",
                                     $"Recruited {n} troops at {village}"));
                }
                case "DispatchRecruiter":
                {
                    string home = Place((string?)j["home"]);
                    string target = Place((string?)j["target"]);
                    return ("info", Tr($"从 {home} 派出招募队前往 {target}",
                                       $"Sent a recruiter party from {home} to {target}"));
                }
                case "DispatchTransfer":
                {
                    string src = Place((string?)j["source"]);
                    string dst = Place((string?)j["dest"]);
                    int n = Int(j, "extracted");
                    return ("info", Tr($"从 {src} 调运 {n} 名士兵到 {dst}",
                                       $"Transferred {n} troops from {src} to {dst}"));
                }
                case "CapitalLogisticsMcmfTransfer":
                {
                    string src = Place((string?)j["src"]);
                    string dst = Place((string?)j["dest"]);
                    int n = Int(j, "amount");
                    return ("info", Tr($"首府调度:{n} 名士兵从 {src} 调往 {dst}",
                                       $"Capital logistics: {n} troops from {src} to {dst}"));
                }
                case "create_patrol_party":
                {
                    string home = Place((string?)j["home"]);
                    int n = Int(j, "moved");
                    return ("info", Tr($"{home} 派出一支巡逻队({n} 名士兵)",
                                       $"{home} dispatched a patrol ({n} troops)"));
                }
                case "create_sally_party":
                {
                    string home = Place((string?)j["home"]);
                    int n = Int(j, "moved");
                    return ("info", Tr($"{home} 出击迎敌({n} 名士兵)",
                                       $"{home} sallied out to engage the enemy ({n} troops)"));
                }
                case "RecruitPrisoner":
                {
                    string town = Place((string?)j["town"]);
                    int n = Int(j, "recruited");
                    return ("ok", Tr($"在 {town} 策反了 {n} 名俘虏入驻军",
                                     $"Recruited {n} prisoners into the garrison at {town}"));
                }
                case "UpgradeGarrison":
                {
                    string home = Place((string?)j["home"]);
                    int n = Int(j, "upgraded");
                    return ("ok", Tr($"在 {home} 升级了 {n} 名驻军士兵",
                                     $"Upgraded {n} garrison troops at {home}"));
                }
                case "capital_lost":
                {
                    string lost = Place((string?)j["lost"]);
                    string newCap = Place((string?)j["newCapital"]);
                    string text = Tr($"首府 {lost} 已失守", $"Capital {lost} has fallen");
                    if (!string.IsNullOrEmpty((string?)j["newCapital"]))
                        text += Tr($",首府迁至 {newCap}", $", capital moved to {newCap}");
                    return ("err", text);
                }
                case "capital_restored":
                {
                    string newCap = Place((string?)j["newCapital"]);
                    return ("info", Tr($"已重新指定首府:{newCap}",
                                       $"New capital designated: {newCap}"));
                }
                case "manual_set_capital":
                {
                    string newCap = Place((string?)j["new"]);
                    return ("info", Tr($"首府已手动设为 {newCap}",
                                       $"Capital manually set to {newCap}"));
                }
                default:
                    // mod_expense / mod_refund(财务,另有 Finance 标签页)及未知类型 — 不进活动流。
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把一个 token 解析成玩家可读的地名:先当作 settlement.StringId 查名;
    /// 查不到则原样返回(首府事件传入的本就是城市名)。空值返回占位符。
    /// </summary>
    private static string Place(string? idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return Tr("某地", "somewhere");
        try
        {
            var s = MBObjectManager.Instance?.GetObject<Settlement>(idOrName);
            string? name = s?.Name?.ToString();
            if (!string.IsNullOrEmpty(name)) return name!;
        }
        catch { /* swallow — fall through to raw token */ }
        return idOrName!;
    }

    private static int Int(JObject j, string key)
    {
        try { return (int?)j[key] ?? 0; }
        catch { return 0; }
    }
}
