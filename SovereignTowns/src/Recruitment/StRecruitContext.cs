using System;

namespace SovereignTowns.Recruitment;

/// <summary>
/// 重入标记：ST 自己的招兵代码进入 vanilla VolunteerModel 之前包一层 Enter()，让
/// <see cref="SovereignTowns.Models.STVolunteerModel"/> 能区分「调用源是 ST 自己」(放行) 还是
/// 「调用源是 vanilla RecruitmentCampaignBehavior」(对受管 AI clan 的 buyer 阻断)。
///
/// 单线程游戏循环原则上不需要 ThreadStatic，但 vanilla 的部分 model 调用可能从非主线程触发
/// （save thread / batch IO），保险起见用 [ThreadStatic]。计数器为 0 = 未进入，&gt; 0 = 处于 ST
/// 招兵上下文（嵌套 / 异常恢复友好）。
///
/// 用法（using 块自动 Dispose 即使内部抛异常也能正确退出）：
/// <code>
/// using (StRecruitContext.Enter())
/// {
///     int maxIdx = volunteerModel.MaximumIndexHeroCanRecruitFromHero(...);
/// }
/// </code>
/// </summary>
public static class StRecruitContext
{
    [ThreadStatic] private static int _depth;

    /// <summary>当前线程是否处于 ST 招兵上下文。STVolunteerModel 用这个判定放行。</summary>
    public static bool IsActive => _depth > 0;

    /// <summary>进入 ST 招兵上下文。返回的 IDisposable 在 Dispose 时退出一层。</summary>
    public static Scope Enter()
    {
        _depth++;
        return new Scope();
    }

    public readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            // 防御性：异常路径下不要把 _depth 减到负数（即便外部错配 Enter/Dispose 也不破坏 IsActive 语义）。
            if (_depth > 0) _depth--;
        }
    }
}
