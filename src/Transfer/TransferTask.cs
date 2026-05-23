using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem.Settlements;

namespace SovereignTowns.Transfer;

/// <summary>
/// 一个跨 settlement 的兵员调拨任务（纯数据：不持有 MobileParty，不执行调拨）。
/// 由首府级调度器产出，下游 <see cref="TransferDispatcher"/> 负责创建真实调拨队伍。
/// </summary>
public sealed class TransferTask
{
    public TransferTask(
        Settlement source,
        Settlement destination,
        int requestedTroops,
        int priority,
        string reason,
        GenericTroopRole? role = null)
    {
        Source = source;
        Destination = destination;
        RequestedTroops = requestedTroops;
        Priority = priority;
        Reason = reason ?? string.Empty;
        Role = role;
    }

    public Settlement Source { get; }

    public Settlement Destination { get; }

    public int RequestedTroops { get; }

    public int Priority { get; }

    public string Reason { get; }

    public GenericTroopRole? Role { get; }
}
