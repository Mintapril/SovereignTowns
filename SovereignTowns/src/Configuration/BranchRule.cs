namespace SovereignTowns.Configuration;

/// <summary>
/// 非首府（城镇 / 城堡）驻军规则。极简：只关心"够多兵力"+"别全是高 tier 几个人"。
/// 不区分 role、不带模板、不限文化 / Tier 范围、不参与升级 — 非首府是 mod 内部的"黑箱"，
/// 只对首府的 role 缺口做兵力借调。
/// </summary>
public sealed class BranchRule
{
    /// <summary>目标兵力（vanilla strength 口径，调 MilitaryPowerModel.GetTroopPower 累加）。
    /// 玩家氏族用此固定值；AI 氏族用 GarrisonPowerEvaluator.ComputeAiVanillaTargetPower 动态算。
    /// 默认 150 ≈ 100 个 T3 步兵；城堡通常 ~80、大城镇 ~250 也合理。</summary>
    public int TargetPower { get; set; } = 150;

    /// <summary>低 tier（T1+T2）头数 / 驻军总头数 必须不低于此比例。
    /// 防御"3 个 T6 = 满 power"的退化局，确保有炮灰。默认 0.20。</summary>
    public float LowTierMinFraction { get; set; } = 0.20f;

    public static BranchRule CreateDefault() => new BranchRule();

    public BranchRule Clone() => new BranchRule
    {
        TargetPower = this.TargetPower,
        LowTierMinFraction = this.LowTierMinFraction,
    };
}
