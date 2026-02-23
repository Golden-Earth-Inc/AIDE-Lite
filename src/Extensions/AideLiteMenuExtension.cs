// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Extensions menu registration — AIDE Lite Chat and Settings menu items
// ============================================================================
using System.ComponentModel.Composition;
using AideLite.Services;
using AideLite.ViewModels;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace AideLite.Extensions;

// MEF discovers this class automatically via [Export].
// Adds two items under Studio Pro's Extensions menu: Chat pane and Settings dialog.
[Export(typeof(MenuExtension))]
public class AideLiteMenuExtension : MenuExtension
{
    private readonly IDockingWindowService _dockingService;
    private readonly IDialogService _dialogService;
    private readonly ILogService _logService;
    private ConfigurationService? _configService;

    [ImportingConstructor]
    public AideLiteMenuExtension(
        IDockingWindowService dockingService,
        IDialogService dialogService,
        ILogService logService)
    {
        _dockingService = dockingService;
        _dialogService = dialogService;
        _logService = logService;
    }

    // Lazy singleton — ConfigurationService is created once and reused across menu actions
    private ConfigurationService GetConfigService()
    {
        _configService ??= new ConfigurationService(_logService, null);
        return _configService;
    }

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        yield return new MenuViewModel(
            "AIDE Lite Chat",
            () => _dockingService.OpenPane(AideLitePaneExtension.PaneId)
        );
        yield return new MenuViewModel(
            "AIDE Lite Settings",
            () =>
            {
                // WebServerBaseUrl is inherited from MenuExtension base class
                var dialog = new SettingsDialogViewModel(
                    GetConfigService(),
                    _logService,
                    _dialogService,
                    WebServerBaseUrl);
                _dialogService.ShowDialog(dialog);
            }
        );
    }
}
