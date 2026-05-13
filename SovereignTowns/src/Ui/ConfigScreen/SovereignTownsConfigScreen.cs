using System;
using SovereignTowns.Configuration;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Ui.ConfigScreen;

/// <summary>
/// Full-screen Gauntlet layer that hosts <see cref="SovereignTownsConfigVM"/>. Lifecycle:
/// <c>OnInitialize</c> loads the <c>SovereignTownsConfigScreen</c> prefab; <c>OnFrameTick</c>
/// polls <see cref="SovereignTownsConfigVM.IsFinished"/> (set by Close/Save buttons or the Exit
/// hotkey) and tears down the screen.
/// </summary>
/// <remarks>
/// Mirrors IG's <c>ConfigMenuGauntletScreen</c> structure but uses only stock Bannerlord widgets,
/// so no custom widget registration is required. The <c>ui_options</c> sprite category is loaded
/// so brushes like <c>SPOptions.Slider.Handle</c> resolve correctly.
/// </remarks>
public sealed class SovereignTownsConfigScreen : ScreenBase
{
    private GauntletLayer? _gauntletLayer;
    private SovereignTownsConfigVM? _dataSource;
    private SpriteCategory? _spriteCategory;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        try
        {
            // Load the ui_options sprite category so SPOptions.* brushes resolve.
            var spriteData = UIResourceManager.SpriteData;
            var resourceContext = UIResourceManager.ResourceContext;
            var resourceDepot = UIResourceManager.ResourceDepot;
            if (spriteData?.SpriteCategories != null
                && spriteData.SpriteCategories.TryGetValue("ui_options", out var cat))
            {
                _spriteCategory = cat;
                _spriteCategory.Load(resourceContext, resourceDepot);
            }

            _dataSource = new SovereignTownsConfigVM();
            _gauntletLayer = new GauntletLayer("GauntletLayer", 4000, false);
            _gauntletLayer.LoadMovie("SovereignTownsConfigScreen", _dataSource);
            _gauntletLayer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            // (InputUsageMask)7 == MouseButtons | Keyboard | Movement — mirrors IG's value.
            _gauntletLayer.InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
            _gauntletLayer.IsFocusLayer = true;

            AddLayer(_gauntletLayer);
            ScreenManager.TrySetFocus(_gauntletLayer);
            Logger.Info("SovereignTownsConfigScreen: initialized");
        }
        catch (Exception ex)
        {
            Logger.Error("SovereignTownsConfigScreen.OnInitialize failed", ex);
            // Force-close so the player isn't stuck on a broken screen.
            try { ScreenManager.PopScreen(); } catch { }
        }
    }

    protected override void OnFrameTick(float dt)
    {
        base.OnFrameTick(dt);

        try
        {
            if (_dataSource is null || _gauntletLayer is null) return;

            bool exitHotKey = _gauntletLayer.Input.IsHotKeyReleased("Exit");
            if (_dataSource.IsFinished || exitHotKey)
            {
                // Always persist on close — option setters have already mutated
                // ConfigurationManager.Current; Save flushes that to global.json.
                // (Close button vs Save button distinction is kept as SaveOnClose for future
                //  Cancel-style behavior; both currently save because edits are immediate.)
                try { ConfigurationManager.Save(); }
                catch (Exception ex) { Logger.Error("ConfigurationManager.Save failed on close", ex); }
                ScreenManager.PopScreen();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SovereignTownsConfigScreen.OnFrameTick failed", ex);
        }
    }

    protected override void OnFinalize()
    {
        try
        {
            if (_gauntletLayer != null)
            {
                RemoveLayer(_gauntletLayer);
                _gauntletLayer = null;
            }
            _spriteCategory?.Unload();
            _spriteCategory = null;
            _dataSource = null;
        }
        catch (Exception ex)
        {
            Logger.Error("SovereignTownsConfigScreen.OnFinalize failed", ex);
        }
        finally
        {
            base.OnFinalize();
        }
    }
}
