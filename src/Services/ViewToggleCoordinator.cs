// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Orchestrates toggling AIDE between sidebar pane and main editor tab
// ============================================================================
using System.Runtime.Versioning;
using AideLite.ViewModels;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Tab;

namespace AideLite.Services;

public enum ViewMode { Pane, Tab }

[SupportedOSPlatform("windows")]
public class ViewToggleCoordinator
{
    private readonly IDockingWindowService _dockingService;
    private readonly ChatController _chatController;
    private readonly Func<Uri> _getWebServerBaseUrl;
    private readonly ILogService _logService;
    private readonly string _paneId;

    private AideLiteTabWebViewModel? _currentTab;

    /// <summary>
    /// True while a toggle is in progress — OnClosed handlers should NOT
    /// run full cleanup when this is set.
    /// </summary>
    public bool IsToggling { get; private set; }

    /// <summary>
    /// Current view mode (Pane or Tab). Used by context menu extensions
    /// to decide whether to call OpenPane.
    /// </summary>
    public static ViewMode CurrentViewMode { get; private set; } = ViewMode.Pane;

    public ViewToggleCoordinator(
        IDockingWindowService dockingService,
        ChatController chatController,
        Func<Uri> getWebServerBaseUrl,
        ILogService logService,
        string paneId)
    {
        _dockingService = dockingService;
        _chatController = chatController;
        _getWebServerBaseUrl = getWebServerBaseUrl;
        _logService = logService;
        _paneId = paneId;
    }

    public void ToggleView()
    {
        if (IsToggling) return;
        IsToggling = true;

        try
        {
            _logService.Info($"AIDE Lite: Toggling view from {CurrentViewMode}");

            // 1. Capture state before closing current view
            var snapshot = _chatController.CaptureViewState();

            // 2. Detach WebView (preserves conversation state in ChatController)
            _chatController.DetachWebView();

            // 3. Queue state restore for the new view
            _chatController.QueueStateRestore(snapshot);

            if (CurrentViewMode == ViewMode.Pane)
            {
                // Switch from Pane → Tab
                _dockingService.ClosePane(_paneId);
                CurrentViewMode = ViewMode.Tab;

                _currentTab = new AideLiteTabWebViewModel(_chatController, _getWebServerBaseUrl());
                _currentTab.OnClosed = OnTabClosed;
                _dockingService.OpenTab(_currentTab);
            }
            else
            {
                // Switch from Tab → Pane
                if (_currentTab != null)
                {
                    _dockingService.CloseTab(_currentTab);
                    _currentTab = null;
                }
                CurrentViewMode = ViewMode.Pane;

                _dockingService.OpenPane(_paneId);
            }

            _logService.Info($"AIDE Lite: Toggled to {CurrentViewMode}");
        }
        finally
        {
            IsToggling = false;
        }
    }

    /// <summary>
    /// Called when the tab is closed by the user via its X button (not during a toggle).
    /// Resets to Pane mode so the next open comes from the menu.
    /// </summary>
    private void OnTabClosed()
    {
        if (IsToggling) return; // During toggle, we manage this ourselves

        _logService.Info("AIDE Lite: Tab closed by user, resetting to Pane mode");
        _chatController.DetachWebView();
        _currentTab = null;
        CurrentViewMode = ViewMode.Pane;
    }

    /// <summary>
    /// Called by AideLitePaneExtension.OnClosed when the pane is closed
    /// by the user (not during a toggle).
    /// </summary>
    public void OnPaneClosed()
    {
        if (IsToggling) return; // During toggle, we manage this ourselves
        // Pane close is already the default state — nothing extra needed
    }
}
