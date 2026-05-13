using TaleWorlds.Library;

namespace SovereignTowns.Ui.ConfigScreen.Options;

/// <summary>
/// Common base for all SovereignTowns config-screen option rows. Carries the user-facing
/// <see cref="Name"/> and <see cref="Description"/> strings; concrete subclasses (numeric / toggle)
/// add their own value bindings.
/// </summary>
/// <remarks>
/// The Bannerlord Gauntlet engine binds XML <c>@Name</c> / <c>@Description</c> placeholders by exact
/// property name, so renaming these here means simultaneously updating the prefab XML.
/// </remarks>
public abstract class STOptionDataVM : ViewModel
{
    private string _name = "";
    private string _description = "";

    [DataSourceProperty]
    public string Name
    {
        get => _name;
        set
        {
            if (value != _name)
            {
                _name = value ?? "";
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    [DataSourceProperty]
    public string Description
    {
        get => _description;
        set
        {
            if (value != _description)
            {
                _description = value ?? "";
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    protected STOptionDataVM(string name, string description)
    {
        _name = name ?? "";
        _description = description ?? "";
    }
}
