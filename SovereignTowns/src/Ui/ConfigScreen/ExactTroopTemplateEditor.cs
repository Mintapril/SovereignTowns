using System;
using System.Collections.Generic;
using SovereignTowns.Configuration;
using TaleWorlds.ScreenSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ConfigScreen;

/// <summary>
/// 士兵定义模板编辑器入口。点击 ConfigScreen 上的"编辑士兵定义"按钮时调用本类。
/// 内部推 <see cref="STTroopPickerScreen"/>——CustomBattle 风格的紧凑兵种选择弹窗，
/// 而不是 ImprovedGarrisons 原生的临时 PartyScreen，避免临时 MobileParty / 隐藏兵种问题。
/// 公开签名保持不变以兼容现有 VM 调用方。
/// </summary>
public static class ExactTroopTemplateEditor
{
    public static void OpenForRule(
        TownGarrisonRule? rule,
        string ownerName,
        Action? onSaved = null)
    {
        try
        {
            if (rule is null)
            {
                Logger.Warn("ExactTroopTemplateEditor.OpenForRule: rule is null");
                return;
            }

            rule.ExactTroopTemplate ??= new Dictionary<string, int>();
            var screen = new STTroopPickerScreen(rule, ownerName ?? "", onSaved);
            ScreenManager.PushScreen(screen);
        }
        catch (Exception ex)
        {
            Logger.Error("ExactTroopTemplateEditor.OpenForRule failed", ex);
        }
    }
}
