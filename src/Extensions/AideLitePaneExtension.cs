// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Dockable pane entry point — creates the WebView chat panel in Studio Pro
// ============================================================================
using System.ComponentModel.Composition;
using System.Runtime.Versioning;
using AideLite.Services;
using AideLite.ViewModels;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Events;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Extensions;

// MEF discovers this class automatically via [Export].
// Open() is called every time the pane opens — not just once — so we must clean up previous state.
[Export(typeof(DockablePaneExtension))]
[SupportedOSPlatform("windows")]
public class AideLitePaneExtension : DockablePaneExtension
{
    // Shared pane ID referenced by other extensions (e.g., context menu, menu items) to open this pane
    public const string PaneId = "aide-lite-chat";

    private readonly IMessageBoxService _messageBoxService;
    private readonly ILogService _logService;
    private readonly IHttpClientService _httpClientService;
    private readonly IDomainModelService _domainModelService;
    private readonly IMicroflowService _microflowService;
    private readonly IMicroflowActivitiesService _activitiesService;
    private readonly IMicroflowExpressionService _expressionService;
    private readonly IUntypedModelAccessService _untypedModelAccessService;
    private ConfigurationService? _configService;
    private AideLitePaneWebViewModel? _currentViewModel;
    private IEventSubscription? _activeDocSubscription;

    // All Mendix services are injected by MEF via [ImportingConstructor]
    [ImportingConstructor]
    public AideLitePaneExtension(
        IMessageBoxService messageBoxService,
        ILogService logService,
        IHttpClientService httpClientService,
        IDomainModelService domainModelService,
        IMicroflowService microflowService,
        IMicroflowActivitiesService activitiesService,
        IMicroflowExpressionService expressionService,
        IUntypedModelAccessService untypedModelAccessService)
    {
        _messageBoxService = messageBoxService;
        _logService = logService;
        _httpClientService = httpClientService;
        _domainModelService = domainModelService;
        _microflowService = microflowService;
        _activitiesService = activitiesService;
        _expressionService = expressionService;
        _untypedModelAccessService = untypedModelAccessService;
    }

    public override string Id => PaneId;

    public override DockablePaneViewModelBase Open()
    {
        _logService.Info("AIDE Lite: Opening chat pane");

        // Clean up previous ViewModel if pane is being re-opened (Open() fires every time, not just once)
        _currentViewModel?.Cleanup();

        _configService ??= new ConfigurationService(_logService, null);

        // Lambda `() => CurrentApp` ensures we always read the latest model, even after project switch
        _currentViewModel = new AideLitePaneWebViewModel(
            () => CurrentApp,
            WebServerBaseUrl,
            _logService,
            _configService,
            _httpClientService,
            _domainModelService,
            _microflowService,
            _activitiesService,
            _expressionService,
            _untypedModelAccessService
        );

        // Track active document changes and forward to ViewModel
        if (_activeDocSubscription != null)
            Unsubscribe(_activeDocSubscription);
        _activeDocSubscription = Subscribe<ActiveDocumentChanged>(e => OnActiveDocumentChanged(e));

        // OnClosed callback: cancel in-flight API calls, clear conversation, detach WebView
        _currentViewModel.OnClosed = () =>
        {
            _logService.Info("AIDE Lite: Pane closed, cleaning up");
            if (_activeDocSubscription != null)
            {
                Unsubscribe(_activeDocSubscription);
                _activeDocSubscription = null;
            }
            _currentViewModel.Cleanup();
            _currentViewModel = null;
        };
        return _currentViewModel;
    }

    private void OnActiveDocumentChanged(ActiveDocumentChanged e)
    {
        var vm = _currentViewModel;
        if (vm == null) return;

        if (e.DocumentName == null)
        {
            vm.UpdateActiveDocument(null, null, null);
            return;
        }

        var docType = e.DocumentType?.ToLowerInvariant() switch
        {
            "microflow" or "microflows$microflow" => "microflow",
            "page" or "pages$page" => "page",
            "constant" or "constants$constant" => "constant",
            "enumeration" or "enumerations$enumeration" => "enumeration",
            "javaaction" or "javaactions$javaaction" => "java_action",
            _ => e.DocumentType ?? "document"
        };

        var qualifiedName = ResolveQualifiedName(e.DocumentName);
        vm.UpdateActiveDocument(e.DocumentName, docType, qualifiedName);
    }

    private string ResolveQualifiedName(string documentName)
    {
        if (CurrentApp == null) return documentName;

        try
        {
            foreach (var module in CurrentApp.Root.GetModules())
            {
                if (module.FromAppStore) continue;
                foreach (var doc in module.GetDocuments())
                {
                    if (doc.Name == documentName)
                        return $"{module.Name}.{documentName}";
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Warn($"AIDE Lite: Error resolving active doc qualified name for '{documentName}': {ex.Message}");
        }
        return documentName;
    }
}
