using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>
/// 兵种选择弹窗中单一兵种的行视图模型。每行展示：兵种缩略图 + Tier 徽章 + 角色徽章 + Noble 标记
/// + 文化徽章 + 兵种名 + 数量 (-/+/+10/-10/直输/移除)。数量为 0 表示该兵种未进入模板；
/// 通过任意按钮调整后回调 <see cref="_onCountChanged"/>，由父 VM 维护 working template。
/// </summary>
public sealed class STTroopRowVM : ViewModel
{
    private readonly Action<string, int> _onCountChanged;
    private readonly string _troopId;
    private readonly string _name;
    private readonly int _tier;
    private readonly string _roleBadge;
    private readonly string _cultureBadge;
    private readonly bool _isNoble;
    private CharacterImageIdentifierVM _visual;
    private int _count;
    private bool _isVisible = true;

    public string TroopId => _troopId;
    public int Tier => _tier;
    public string RoleBadgeKey => _roleBadge;
    public string CultureId => _cultureBadge;
    public bool IsNoble => _isNoble;

    [DataSourceProperty]
    public CharacterImageIdentifierVM Visual
    {
        get => _visual;
        set { if (value != _visual) { _visual = value; OnPropertyChanged(nameof(Visual)); } }
    }

    [DataSourceProperty]
    public string Name => _name;

    [DataSourceProperty]
    public string TierText => _tier <= 0 ? "?" : $"T{_tier}";

    [DataSourceProperty]
    public string RoleBadge => _roleBadge;

    [DataSourceProperty]
    public string CultureBadge => _cultureBadge;

    [DataSourceProperty]
    public string NobleBadge => _isNoble ? "[Noble]" : "";

    [DataSourceProperty]
    public int Count
    {
        get => _count;
        set
        {
            if (value < 0) value = 0;
            if (value == _count) return;
            _count = value;
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsSelected));
            _onCountChanged(_troopId, _count);
        }
    }

    [DataSourceProperty]
    public string CountText => _count.ToString();

    [DataSourceProperty]
    public bool IsSelected => _count > 0;

    [DataSourceProperty]
    public bool IsVisible
    {
        get => _isVisible;
        set { if (value != _isVisible) { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); } }
    }

    public STTroopRowVM(
        CharacterObject character,
        int tier,
        string roleBadge,
        string cultureBadge,
        bool isNoble,
        int initialCount,
        Action<string, int> onCountChanged)
    {
        if (character is null) throw new ArgumentNullException(nameof(character));
        _troopId = character.StringId ?? "";
        _name = character.Name?.ToString() ?? _troopId;
        _tier = tier;
        _roleBadge = roleBadge ?? "";
        _cultureBadge = cultureBadge ?? "";
        _isNoble = isNoble;
        _count = Math.Max(0, initialCount);
        _onCountChanged = onCountChanged ?? ((_, _) => { });
        try
        {
            _visual = new CharacterImageIdentifierVM(CharacterCode.CreateFrom(character));
        }
        catch
        {
            // Visual 仅装饰；构造失败不影响其余功能。
            _visual = null!;
        }
    }

    /// <summary>+1。</summary>
    public void ExecuteIncrement() => Count = _count + 1;

    /// <summary>-1。</summary>
    public void ExecuteDecrement() => Count = _count - 1;

    /// <summary>+10。</summary>
    public void ExecuteIncrementBy10() => Count = _count + 10;

    /// <summary>-10。</summary>
    public void ExecuteDecrementBy10() => Count = Math.Max(0, _count - 10);

    /// <summary>数量清零（彻底从模板移除）。</summary>
    public void ExecuteRemove() => Count = 0;

    /// <summary>强制刷新（Reset 后由父 VM 调用，告诉绑定层数量已外部变化）。</summary>
    public void SetCountSilent(int value)
    {
        if (value < 0) value = 0;
        if (value == _count) return;
        _count = value;
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsSelected));
    }
}
