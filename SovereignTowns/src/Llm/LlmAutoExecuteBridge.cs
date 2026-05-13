using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SovereignTowns.Audit;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Llm;

/// <summary>
/// MVP 6 自动执行桥接器。架构关键：
/// LLM 调用是 **异步、超时、fire-and-forget**；返回的 advice 写入 PendingAdvice 字典。
/// 下一轮 DailyTick 由 TownGarrisonManager 消费 advice（绝不在当轮等待 LLM）。
/// 用户原则严格遵守："LLM 因输出延迟禁用于即时路径"。
/// </summary>
public static class LlmAutoExecuteBridge
{
    private static readonly ConcurrentDictionary<string, LLMAdvice> _pendingAdvice =
        new ConcurrentDictionary<string, LLMAdvice>();

    private static readonly SemaphoreSlim _outstanding = new SemaphoreSlim(initialCount: 4, maxCount: 4);

    public static int PendingCount => _pendingAdvice.Count;

    /// <summary>
    /// 异步启动 LLM 推理。立即返回（fire-and-forget）。
    /// 结果通过 PendingAdvice 在下一轮 DailyTick 消费。
    /// 若 LLM 已禁用 / Provider 未配置 / 上一轮还没消费 → 跳过本次调用。
    /// </summary>
    public static void TryStartReasoning(LLMReasoningService? service, Settlement settlement, string situationSummary)
    {
        try
        {
            if (service == null || settlement == null) return;
            if (!ConfigurationManager.Current.EnabledFeatures.LlmAutoExecute) return;

            var key = settlement.StringId;
            if (string.IsNullOrEmpty(key)) return;

            // 已有 pending advice 未消费 → 不重复启动
            if (_pendingAdvice.ContainsKey(key)) return;

            if (!_outstanding.Wait(0))
            {
                Logger.Debug("LlmAutoExecuteBridge: concurrency cap reached, skip this settlement");
                return;
            }

            // fire-and-forget
            _ = Task.Run(async () =>
            {
                try
                {
                    var ctxLabel = "garrison_planning:" + key;
                    var advice = await service.SuggestForLongTermPlanningAsync(ctxLabel, situationSummary).ConfigureAwait(false);
                    if (advice != null)
                    {
                        _pendingAdvice[key] = advice;
                        Logger.Info($"LlmAutoExecuteBridge: advice queued for '{key}' action={advice.Action} mag={advice.MagnitudeSuggested}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("LlmAutoExecuteBridge fire-and-forget exception (swallowed): " + ex.Message);
                }
                finally
                {
                    try { _outstanding.Release(); } catch { }
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("LlmAutoExecuteBridge.TryStartReasoning failed", ex);
        }
    }

    /// <summary>
    /// 取出 settlement 的 pending advice（取出后清空）。
    /// 若 advice 通过三重校验返回；否则返回 null。
    /// </summary>
    public static LLMAdvice? ConsumePendingAdvice(Settlement settlement)
    {
        try
        {
            if (settlement == null) return null;
            var key = settlement.StringId;
            if (string.IsNullOrEmpty(key)) return null;
            if (!_pendingAdvice.TryRemove(key, out var advice)) return null;

            // 三重校验
            // (a) LLM 输出格式
            var errors = LLMDecisionValidator.Validate(advice);
            if (errors.Count > 0)
            {
                Logger.Warn($"LlmAutoExecuteBridge: advice for '{key}' rejected by validator: {string.Join("; ", errors)}");
                DecisionAuditLogger.LogRule("LLMAdviceRejected", $"settlement={key}",
                    $"{{\"errors\":\"{string.Join(",", errors)}\"}}",
                    accepted: false, rejectionReason: "validator");
                return null;
            }

            // (b) 安全规则：magnitudeSuggested 不能超过 settlement 当前驻军 × 2
            var town = settlement.Town;
            var currentGarrison = town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            if (advice.MagnitudeSuggested > Math.Max(20, currentGarrison * 2))
            {
                Logger.Warn($"LlmAutoExecuteBridge: advice mag={advice.MagnitudeSuggested} exceeds safety cap (currentGarrison={currentGarrison})");
                DecisionAuditLogger.LogRule("LLMAdviceRejected", $"settlement={key}",
                    $"{{\"mag\":{advice.MagnitudeSuggested},\"current\":{currentGarrison}}}",
                    accepted: false, rejectionReason: "magnitude exceeds 2x current garrison");
                return null;
            }

            // (c) 配置限制：feature 必须仍启用（用户可能在等待 LLM 期间禁用）
            if (!ConfigurationManager.Current.EnabledFeatures.LlmAutoExecute)
            {
                Logger.Info($"LlmAutoExecuteBridge: advice for '{key}' discarded — LlmAutoExecute now disabled");
                return null;
            }

            Logger.Info($"LlmAutoExecuteBridge: advice for '{key}' passed all checks, will execute");
            return advice;
        }
        catch (Exception ex)
        {
            Logger.Error("LlmAutoExecuteBridge.ConsumePendingAdvice failed", ex);
            return null;
        }
    }
}
