namespace SovereignTowns.Configuration;

/// <summary>
/// 建筑等级 → 队伍加成系数。军营(Barracks)驱动征兵 / 调拨 / 出击队并发上限与驻军每日 XP;
/// 哨所(Guard House)驱动巡逻队并发上限。
/// 并发上限公式:cap = Base + 建筑等级 × PerLevel,结果钳制 ≥ 1。
/// 默认值令 0 级建筑下 = 旧的固定行为(各类队伍上限 1、驻军 XP 5),3 级下 = 上限 4 / XP 20。
/// </summary>
public sealed class BuildingBonusConfig
{
    public int RecruiterBaseCap { get; set; } = 1;
    public int RecruiterCapPerBarracksLevel { get; set; } = 1;

    public int TransferBaseCap { get; set; } = 1;
    public int TransferCapPerBarracksLevel { get; set; } = 1;

    public int SallyBaseCap { get; set; } = 1;
    public int SallyCapPerBarracksLevel { get; set; } = 1;

    public int PatrolBaseCap { get; set; } = 1;
    public int PatrolCapPerGuardHouseLevel { get; set; } = 1;

    public int GarrisonXpBasePerDay { get; set; } = 5;
    public int GarrisonXpPerBarracksLevel { get; set; } = 5;
}
