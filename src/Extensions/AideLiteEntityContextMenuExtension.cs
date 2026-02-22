// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Right-click context menu on entities in the Domain Model editor
// ============================================================================
using System.ComponentModel.Composition;
using AideLite.Services;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace AideLite.Extensions;

[Export(typeof(ContextMenuExtension<>))]
public class AideLiteEntityContextMenuExtension : ContextMenuExtension<IEntity>
{
    private readonly IDockingWindowService _dockingService;
    private readonly ILogService _log;

    [ImportingConstructor]
    public AideLiteEntityContextMenuExtension(
        IDockingWindowService dockingService,
        ILogService logService)
    {
        _dockingService = dockingService;
        _log = logService;
    }

    public override IEnumerable<MenuViewModel> GetContextMenus(IEntity entity)
    {
        var qualifiedName = ResolveQualifiedName(entity);

        yield return new MenuViewModel(
            "Explain with AIDE Lite",
            () =>
            {
                _log.Info($"AIDE Lite: 'Explain' clicked for entity '{qualifiedName}'");
                DocumentReferenceStore.Enqueue(
                    new DocumentReference(DocumentAction.Explain, "entity", qualifiedName));
                if (ViewToggleCoordinator.CurrentViewMode == ViewMode.Pane)
                    _dockingService.OpenPane(AideLitePaneExtension.PaneId);
            });

        yield return new MenuViewModel(
            "Add to AIDE Lite Context",
            () =>
            {
                _log.Info($"AIDE Lite: 'Add to Context' clicked for entity '{qualifiedName}'");
                DocumentReferenceStore.Enqueue(
                    new DocumentReference(DocumentAction.AddContext, "entity", qualifiedName));
                if (ViewToggleCoordinator.CurrentViewMode == ViewMode.Pane)
                    _dockingService.OpenPane(AideLitePaneExtension.PaneId);
            });
    }

    private string ResolveQualifiedName(IEntity entity)
    {
        if (CurrentApp == null) return entity.Name;

        try
        {
            foreach (var module in CurrentApp.Root.GetModules())
            {
                if (module.FromAppStore) continue;
                foreach (var e in module.DomainModel.GetEntities())
                {
                    if (ReferenceEquals(e, entity))
                        return $"{module.Name}.{entity.Name}";
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"AIDE Lite: Error resolving qualified name for entity '{entity.Name}': {ex.Message}");
        }
        return entity.Name;
    }
}
