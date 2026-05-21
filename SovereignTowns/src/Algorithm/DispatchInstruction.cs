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
