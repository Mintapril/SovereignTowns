using TaleWorlds.SaveSystem;

namespace SovereignTowns.SaveSystem;

/// <summary>
/// 注册 SovereignTowns 模组自定义类型到 vanilla 存档系统。
///
/// 自动发现机制（v1.3.15）：
///   TaleWorlds.SaveSystem.Definition.DefinitionContext.FillWithCurrentTypes() 通过反射扫描
///   所有已加载程序集，找出 `typeof(SaveableTypeDefiner).IsAssignableFrom(type) && !type.IsAbstract`
///   的类型，Activator.CreateInstance 实例化后调用各 DefineXxx 钩子。
///   ——所以**无需**在 SubModule 中显式注册本类。
///
/// SaveBaseId 选择（2026-05-12 审查 E1 调整）：
///   ButterLib 占用 2_002_018_000 起，是当前已知主流 lib 最高位 base id。
///   vanilla 各 Behavior 自带 TypeDefiner 用的 base id 多在 ~80 万 - ~200 万 区间
///   （例：RecruitmentCampaignBehaviorTypeDefiner 用 881200）。
///   选 1_900_000_000：远高于 vanilla 范围、低于 ButterLib 安全区，且 1 亿 ID 本地命名空间充足；
///   同时显著降低与其它 mod 撞 base 的概率（旧值 100_000_000 是 8 位低位，社区有 mod 也用同范围）。
///   参考 ButterLib SaveSystem Overview: https://github.com/BUTR/Bannerlord.ButterLib/blob/dev/docs/articles/SaveSystem/Overview.md
/// </summary>
public sealed class SovereignTownsTypeDefiner : SaveableTypeDefiner
{
    /// <summary>本 Mod 的存档 ID 命名空间起点。所有 local id 必须在 [0, ~) 内。</summary>
    public const int SaveBaseId = 1_900_000_000;

    public SovereignTownsTypeDefiner() : base(SaveBaseId)
    {
    }

    protected override void DefineClassTypes()
    {
        // local id 1: 招募队伍组件
        AddClassDefinition(typeof(Parties.RecruitingPartyComponent), 1);

        // local id 2: 调拨队伍组件
        AddClassDefinition(typeof(Parties.TransferPartyComponent), 2);

        // local id 3: 出击队伍组件
        AddClassDefinition(typeof(Parties.SallyForthPartyComponent), 3);
    }
}
