using System.Collections.Generic;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem.Settlements;

namespace SovereignTowns.Algorithm;

public abstract class DispatchInstruction
{
    protected DispatchInstruction(GenericTroopRole role, int count)
    {
        Role = role;
        Count = count < 0 ? 0 : count;
    }

    public GenericTroopRole Role { get; }
    public int Count { get; }
}

public sealed class InPlaceRecruitInstruction : DispatchInstruction
{
    public InPlaceRecruitInstruction(Settlement settlement, GenericTroopRole role, int count)
        : base(role, count)
    {
        Settlement = settlement;
    }

    public Settlement Settlement { get; }
}

public sealed class RecruiterPartyInstruction : DispatchInstruction
{
    public RecruiterPartyInstruction(
        Town town, Settlement returnSettlement, Settlement targetVillage, GenericTroopRole role, int count)
        : base(role, count)
    {
        Town = town;
        ReturnSettlement = returnSettlement;
        TargetVillage = targetVillage;
    }

    public Town Town { get; }
    public Settlement ReturnSettlement { get; }

    /// <summary>MCMF 选定的招募目标村。CapitalLogisticsManager 按 role 把多条指令的目标村打包成征兵队行程。</summary>
    public Settlement TargetVillage { get; }
}

public sealed class PrisonerConvertInstruction : DispatchInstruction
{
    public PrisonerConvertInstruction(Settlement settlement, GenericTroopRole role, int count)
        : base(role, count)
    {
        Settlement = settlement;
    }

    public Settlement Settlement { get; }
}

public sealed class TransferPartyInstruction : DispatchInstruction
{
    public TransferPartyInstruction(Settlement source, Settlement destination, GenericTroopRole role, int count)
        : base(role, count)
    {
        Source = source;
        Destination = destination;
    }

    public Settlement Source { get; }
    public Settlement Destination { get; }
}

public sealed class PatrolInstruction : DispatchInstruction
{
    /// <summary>巡逻队派发指令。<see cref="DispatchInstruction.Count"/> = MCMF 决定的本 tick
    /// 巡逻总头数(跨 role 求和);role 不参与巡逻语义,取 Infantry 占位。</summary>
    public PatrolInstruction(Settlement capital, int headcount)
        : base(GenericTroopRole.Infantry, headcount)
    {
        Capital = capital;
    }

    public Settlement Capital { get; }
}

/// <summary>
/// 卫队（Honor Guard）招募指令。MCMF decode 给出 (village, capital, role, tier, count, candidateTroopIds)：
/// 执行层从 candidateTroopIds 与 village.notable.VolunteerTypes 的交集中挑实际兵种 id 派
/// <see cref="SovereignTowns.Parties.HonorGuardRecruiterPartyComponent"/> 队。
/// candidateTroopIds 来自 HonorGuardTemplate 中 (role,tier) 与本指令匹配的 troopId 集合。
/// 每 MCMF 求解的 decode 至多生成多条；执行层按 lifecycle cap=1 守护，只接第一条 deficit 最大的。
/// </summary>
public sealed class HonorGuardRecruiterInstruction : DispatchInstruction
{
    public HonorGuardRecruiterInstruction(
        Settlement capital, Settlement targetVillage,
        GenericTroopRole role, int tier, int count,
        IReadOnlyList<string> candidateTroopIds)
        : base(role, count)
    {
        Capital = capital;
        TargetVillage = targetVillage;
        Tier = tier;
        CandidateTroopIds = candidateTroopIds;
    }

    public Settlement Capital { get; }
    public Settlement TargetVillage { get; }
    public int Tier { get; }
    public IReadOnlyList<string> CandidateTroopIds { get; }
}

public sealed class SallyInstruction : DispatchInstruction
{
    /// <summary>出击队派发指令。<see cref="DispatchInstruction.Count"/> = MCMF 决定的本 tick
    /// sally 总头数（跨 role 求和）；role 不参与 sally 语义，取 Infantry 占位。</summary>
    public SallyInstruction(Settlement source, int heads)
        : base(GenericTroopRole.Infantry, heads)
    {
        Source = source;
    }

    public Settlement Source { get; }
}
