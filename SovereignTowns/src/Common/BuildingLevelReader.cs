using System;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

/// <summary>本 Mod 关心的 vanilla 建筑。Barracks=军营,GuardHouse=哨所。</summary>
public enum StBuilding
{
    Barracks,
    GuardHouse,
}

/// <summary>
/// 全 Mod 唯一的 vanilla 建筑等级读取入口。集中持有正确的 BuildingType.StringId
/// (反编译 DefaultBuildingTypes 核实,v1.3.15):
///   军营   Town  = building_settlement_barracks    Castle = building_castle_barracks
///   哨所   Town  = building_settlement_guard_house  Castle = building_castle_guard_house
/// 旧代码误用 settlement_garrison / castle_barracks(无 building_ 前缀且名字错),
/// 导致建筑等级恒为 0。本类取代那些重复查找。
/// </summary>
public static class BuildingLevelReader
{
    /// <summary>返回建筑当前等级,钳制 [0,3]。town==null / 未建造 / 任何异常 → 0。绝不抛。</summary>
    public static int GetLevel(Settlement? settlement, StBuilding building)
    {
        try
        {
            var town = settlement?.Town;
            if (town?.Buildings == null) return 0;
            bool isCastle = settlement!.IsCastle;
            string targetId = building switch
            {
                StBuilding.Barracks   => isCastle ? "building_castle_barracks"    : "building_settlement_barracks",
                StBuilding.GuardHouse => isCastle ? "building_castle_guard_house" : "building_settlement_guard_house",
                _ => "",
            };
            if (targetId.Length == 0) return 0;

            foreach (var b in town.Buildings)
            {
                if (b?.BuildingType == null) continue;
                string id;
                try { id = b.BuildingType.StringId ?? ""; }
                catch { continue; }
                if (string.Equals(id, targetId, StringComparison.Ordinal))
                {
                    int level;
                    try { level = b.CurrentLevel; }
                    catch { level = 0; }
                    if (level < 0) level = 0;
                    if (level > 3) level = 3;
                    return level;
                }
            }
            return 0; // 该建筑尚未建造(建造槽空着)
        }
        catch (Exception ex)
        {
            Logger.Error($"BuildingLevelReader.GetLevel failed ({building})", ex);
            return 0;
        }
    }
}
