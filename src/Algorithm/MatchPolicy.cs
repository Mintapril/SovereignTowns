using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;

namespace SovereignTowns.Algorithm;

/// <summary>
/// 招募候选兵种的描述桶。<see cref="RecruitmentTopology.BucketizeCharacters"/> 把 notable
/// 志愿兵按 role 聚合产出 <see cref="TroopBucket"/>，solver 在招募块按 bucket 建图。
/// 2026-05-29: <see cref="MatchPolicy"/> 静态类被清空 —— role-ratio / tier-share / role-quota 决策全部
/// 从 solver 删除；仅保留本 struct 作为招募桶的契约类型。
/// </summary>
public readonly struct TroopBucket
{
    public TroopBucket(GenericTroopRole role, int count, int minTier, CharacterObject? representative)
    {
        Role = role;
        Count = count < 0 ? 0 : count;
        MinTier = minTier <= 0 ? 1 : minTier;
        Representative = representative;
    }

    public GenericTroopRole Role { get; }
    public int Count { get; }
    public int MinTier { get; }
    public CharacterObject? Representative { get; }
}
