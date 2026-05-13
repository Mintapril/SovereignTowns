using System;
using System.Reflection;
using SovereignTowns.Ui.ConfigScreen;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.MapRibbon;

/// <summary>
/// Adds a single persistent GauntletLayer to the vanilla campaign-map screen
/// (<c>SandBox.View.Map.MapScreen</c>). The layer hosts the IG-style floating
/// drawer prefab (<c>SovereignTownsRibbon.xml</c>) bound to <see cref="SovereignTownsConfigVM"/>.
///
/// <para>
/// Reflection-based access to <c>MapScreen.Instance</c> avoids taking a hard reference on
/// <c>SandBox.View.dll</c> (a Modules/SandBox assembly, not a Native one). Falls back to
/// <see cref="ScreenManager.TopScreen"/> when <c>MapScreen.Instance</c> happens to be null
/// at the call site (rare but possible during very early init / cutscenes).
/// </para>
///
/// <para>
/// Idempotency: walks <c>mapScreen.Layers</c> for an existing layer named <see cref="LayerName"/>
/// before adding. Save-load → re-OnSessionLaunched → reuse, no duplicate stacking.
/// </para>
/// </summary>
public static class SovereignTownsRibbonInjector
{
    public enum FloatingPanelState
    {
        Expanded,
        Retracted
    }

    public const string LayerName = "SovereignTownsRibbonLayer";
    private const string MovieName = "SovereignTownsRibbon";
    private const int LocalOrder = 550; // IG uses the same order for its map floating panel.

    private static GauntletLayer? _layer;
    private static SovereignTownsConfigVM? _vm;
    private static ScreenBase? _hostScreen;

    public static FloatingPanelState CurrentUiState { get; private set; } = FloatingPanelState.Retracted;

    /// <summary>
    /// Idempotent install. Safe to call multiple times — checks for an existing layer and
    /// reuses it. Wrapped in try/catch so any reflection / Gauntlet failure degrades to a log
    /// entry instead of crashing OnSessionLaunched.
    /// </summary>
    public static void Inject()
    {
        try
        {
            var mapScreen = TryGetMapScreen();
            if (mapScreen == null)
            {
                Logger.Warn("Ribbon: MapScreen instance not available yet — skipping (will retry on map open)");
                return;
            }

            // Idempotency: if we already added our layer to the same screen, do nothing.
            if (_layer != null && ReferenceEquals(_hostScreen, mapScreen))
            {
                Logger.Info("Ribbon: layer already injected — skip");
                return;
            }

            // Re-attach path: a layer from a previous session may already exist on the screen.
            foreach (var layer in mapScreen.Layers)
            {
                if (layer is GauntletLayer gl && layer.Name == LayerName)
                {
                    _layer = gl;
                    _hostScreen = mapScreen;
                    Logger.Info("Ribbon: re-attached to pre-existing layer on MapScreen");
                    return;
                }
            }

            _vm = new SovereignTownsConfigVM(OnFloatingPanelFinished);
            // GauntletLayer ctor signature: (string name, int localOrder, bool shouldClear)
            // — verified via reflection on v1.3.15. Name is read-only after construction, so
            // pass LayerName here to enable the find-by-name re-attach path above.
            _layer = new GauntletLayer(LayerName, LocalOrder, false);
            _layer.LoadMovie(MovieName, _vm);
            // InputUsageMask 7 == MouseButtons|Keyboard|Movement, non-exclusive.
            // This mirrors IG's floating menu: mouse works on the drawer while map input remains usable.
            _layer.InputRestrictions.SetInputRestrictions(false, (InputUsageMask)7);

            mapScreen.AddLayer(_layer);
            _hostScreen = mapScreen;
            ScreenManager.TrySetFocus(_layer);
            Logger.Info("Floating panel: layer injected onto MapScreen");
        }
        catch (Exception ex)
        {
            Logger.Error("Ribbon: Inject failed", ex);
        }
    }

    /// <summary>
    /// Tear-down hook for <c>OnSubModuleUnloaded</c>. Best-effort — never throws.
    /// </summary>
    /// <summary>
    /// Cheap "have we attached yet?" probe — used by the campaign behavior's hourly tick to
    /// drive a deferred retry if <see cref="Inject"/> at session-launch found no MapScreen
    /// yet (intro cutscene / character creation paths).
    /// </summary>
    public static bool IsInjected => _layer != null;

    public static void InvertPanelState()
    {
        CurrentUiState = CurrentUiState == FloatingPanelState.Expanded
            ? FloatingPanelState.Retracted
            : FloatingPanelState.Expanded;
    }

    public static void RetractPanel()
    {
        CurrentUiState = FloatingPanelState.Retracted;
    }

    public static void Unload()
    {
        try
        {
            if (_layer != null && _hostScreen != null)
            {
                try { _hostScreen.RemoveLayer(_layer); }
                catch (Exception ex) { Logger.Error("Ribbon: RemoveLayer failed", ex); }
            }
        }
        finally
        {
            _layer = null;
            _vm = null;
            _hostScreen = null;
            CurrentUiState = FloatingPanelState.Retracted;
        }
    }

    private static void OnFloatingPanelFinished(bool saveRequested)
    {
        try
        {
            // Match the previous full-screen panel behavior: edits are committed immediately,
            // and closing the panel flushes the current config to disk.
            ConfigurationManager.Save();
            Logger.Info(saveRequested
                ? "Floating panel: saved and retracted"
                : "Floating panel: closed and saved current edits");
        }
        catch (Exception ex)
        {
            Logger.Error("Floating panel: save-on-close failed", ex);
        }
        finally
        {
            RetractPanel();
        }
    }

    /// <summary>
    /// Look up <c>SandBox.View.Map.MapScreen.Instance</c> via reflection. Returns the screen as
    /// the base <see cref="ScreenBase"/> type — that's all we need to call <c>AddLayer</c> on it
    /// (inherited from <c>ScreenBase</c>). Falls back to <see cref="ScreenManager.TopScreen"/>
    /// only if it's typed as <c>SandBox.View.Map.MapScreen</c>.
    /// </summary>
    private static ScreenBase? TryGetMapScreen()
    {
        try
        {
            // Locate SandBox.View.Map.MapScreen.Instance.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "SandBox.View") continue;
                var t = asm.GetType("SandBox.View.Map.MapScreen", throwOnError: false);
                if (t == null) continue;

                var prop = t.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                var inst = prop?.GetValue(null) as ScreenBase;
                if (inst != null) return inst;
                break;
            }

            // Fallback: maybe the current top screen IS the map screen.
            var top = ScreenManager.TopScreen;
            if (top != null && top.GetType().FullName == "SandBox.View.Map.MapScreen")
                return top;

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("Ribbon: TryGetMapScreen reflection failed", ex);
            return null;
        }
    }
}
