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

/// <summary>
/// 招募指令两种模式：
/// <list type="bullet">
///   <item><b>GarrisonRole</b>：按 role 灌驻军（vanilla 模板/比例），多站行程，回家并入 Town.GarrisonParty。</item>
///   <item><b>HonorGuardPrecise</b>：按 troopId 精确招卫队池（含可升级链匹配），多站行程，回家转入 HonorGuard 池。</item>
/// </list>
/// 两模式共用 <see cref="SovereignTowns.Parties.StRecruiterPartyComponent"/> 与本指令；分支只在
/// 志愿者匹配函数（<see cref="TroopTemplateMatcher.IsAcceptableVolunteer"/>）与 OnArrivedHome 两处。
/// </summary>
public enum RecruiterMode
{
    GarrisonRole = 0,
    HonorGuardPrecise = 1,
}

public sealed class RecruiterPartyInstruction : DispatchInstruction
{
    public RecruiterPartyInstruction(
        Town town, Settlement returnSettlement, Settlement targetVillage,
        GenericTroopRole role, int count,
        RecruiterMode mode = RecruiterMode.GarrisonRole,
        IReadOnlyDictionary<string, int>? preciseTemplate = null)
        : base(role, count)
    {
        Town = town;
        ReturnSettlement = returnSettlement;
        TargetVillage = targetVillage;
        Mode = mode;
        PreciseTemplate = preciseTemplate;
    }

    public Town Town { get; }
    public Settlement ReturnSettlement { get; }

    /// <summary>MCMF 选定的招募目标村。CapitalLogisticsManager 按 (Town, ReturnSettlement, Role, Mode) 把
    /// 多条指令的目标村打包成征兵队行程。</summary>
    public Settlement TargetVillage { get; }

    public RecruiterMode Mode { get; }

    /// <summary>HonorGuardPrecise 模式下携带的完整精确模板（troopId → desiredCount）。
    /// 执行端 per-village 用模板算 deficit、做匹配（含可升级链）。GarrisonRole 模式下为 null。</summary>
    public IReadOnlyDictionary<string, int>? PreciseTemplate { get; }
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
