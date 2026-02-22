// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Right-click context menu on documents (microflows, pages, etc.)
// ============================================================================
using System.ComponentModel.Composition;
using AideLite.Services;
using Mendix.StudioPro.ExtensionsAPI.Model.Constants;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using Mendix.StudioPro.ExtensionsAPI.Model.JavaActions;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace AideLite.Extensions;

[Export(typeof(ContextMenuExtension<>))]
public class AideLiteDocumentContextMenuExtension : ContextMenuExtension<IDocument>
{
    private readonly IDockingWindowService _dockingService;
    private readonly ILogService _log;

    [ImportingConstructor]
    public AideLiteDocumentContextMenuExtension(
        IDockingWindowService dockingService,
        ILogService logService)
    {
        _dockingService = dockingService;
        _log = logService;
    }

    public override IEnumerable<MenuViewModel> GetContextMenus(IDocument document)
    {
        var elementType = DetectElementType(document);
        var qualifiedName = ResolveQualifiedName(document);

        yield return new MenuViewModel(
            "Explain with AIDE Lite",
            () =>
            {
                _log.Info($"AIDE Lite: 'Explain' clicked for {elementType} '{qualifiedName}'");
                DocumentReferenceStore.Enqueue(
                    new DocumentReference(DocumentAction.Explain, elementType, qualifiedName));
                _dockingService.OpenPane(AideLitePaneExtension.PaneId);
            });

        yield return new MenuViewModel(
            "Add to AIDE Lite Context",
            () =>
            {
                _log.Info($"AIDE Lite: 'Add to Context' clicked for {elementType} '{qualifiedName}'");
                DocumentReferenceStore.Enqueue(
                    new DocumentReference(DocumentAction.AddContext, elementType, qualifiedName));
                _dockingService.OpenPane(AideLitePaneExtension.PaneId);
            });
    }

    private static string DetectElementType(IDocument document) => document switch
    {
        IMicroflow => "microflow",
        IPage => "page",
        IConstant => "constant",
        IEnumeration => "enumeration",
        IJavaAction => "java_action",
        _ => "document"
    };

    private string ResolveQualifiedName(IDocument document)
    {
        if (document is IConstant c) return c.QualifiedName?.ToString() ?? document.Name;
        if (document is IEnumeration e) return e.QualifiedName?.ToString() ?? document.Name;
        if (document is IJavaAction j) return j.QualifiedName?.ToString() ?? document.Name;

        if (CurrentApp == null) return document.Name;

        try
        {
            foreach (var module in CurrentApp.Root.GetModules())
            {
                if (module.FromAppStore) continue;
                foreach (var doc in module.GetDocuments())
                {
                    if (ReferenceEquals(doc, document))
                        return $"{module.Name}.{document.Name}";
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"AIDE Lite: Error resolving qualified name for '{document.Name}': {ex.Message}");
        }
        return document.Name;
    }
}
