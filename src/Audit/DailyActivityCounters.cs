using System.Threading;

namespace SovereignTowns.Audit;

/// <summary>
/// B17.4 A2：DailyTick 间累加的"今日 N 件事"。基于 Interlocked 线程安全 — 决策可在
/// HourlyTick / OnHourlyTickParty / OnHourlyTickSettlement 等任意 vanilla 事件回调里 +1。
/// DailyTick 末尾读取 + 重置（顺序与 IG GarrisonDailyBehavior 一致：read → display → reset）。
/// </summary>
public static class DailyActivityCounters
{
    private static int _recruitedToday;
    private static int _transferredToday;
    private static int _patrolDispatchedToday;
    private static int _sallyDispatchedToday;
    private static int _prisonerRecruitedToday;

    public static int RecruitedToday => Volatile.Read(ref _recruitedToday);
    public static int TransferredToday => Volatile.Read(ref _transferredToday);
    public static int PatrolDispatchedToday => Volatile.Read(ref _patrolDispatchedToday);
    public static int SallyDispatchedToday => Volatile.Read(ref _sallyDispatchedToday);
    public static int PrisonerRecruitedToday => Volatile.Read(ref _prisonerRecruitedToday);

    public static void AddRecruited(int n) { if (n > 0) Interlocked.Add(ref _recruitedToday, n); }
    public static void AddTransferred(int n) { if (n > 0) Interlocked.Add(ref _transferredToday, n); }
    public static void AddPatrolDispatched(int n) { if (n > 0) Interlocked.Add(ref _patrolDispatchedToday, n); }
    public static void AddSallyDispatched(int n) { if (n > 0) Interlocked.Add(ref _sallyDispatchedToday, n); }
    public static void AddPrisonerRecruited(int n) { if (n > 0) Interlocked.Add(ref _prisonerRecruitedToday, n); }

    /// <summary>读取所有计数器为一个 snapshot 元组。原子性不保证(允许 +1 漏算),DailyTick 末尾使用 OK。</summary>
    public static (int recruited, int transferred, int patrols, int sallies, int prisoners) Snapshot()
        => (RecruitedToday, TransferredToday, PatrolDispatchedToday, SallyDispatchedToday, PrisonerRecruitedToday);

    /// <summary>清零所有计数器。必须在 DisplayMessage 之后调用。</summary>
    public static void ResetAll()
    {
        Interlocked.Exchange(ref _recruitedToday, 0);
        Interlocked.Exchange(ref _transferredToday, 0);
        Interlocked.Exchange(ref _patrolDispatchedToday, 0);
        Interlocked.Exchange(ref _sallyDispatchedToday, 0);
        Interlocked.Exchange(ref _prisonerRecruitedToday, 0);
    }
}
