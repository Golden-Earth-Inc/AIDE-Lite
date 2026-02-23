// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// App context extraction — builds a full DTO snapshot of the Mendix app model
// ============================================================================
using AideLite.Models.DTOs;
using AideLite.Services;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.UntypedModel;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.ModelReaders;

public class AppContextExtractor
{
    private readonly IModel _model;
    private readonly DomainModelReader _domainModelReader;
    private readonly MicroflowReader _microflowReader;
    private readonly PageReader _pageReader;
    private readonly IUntypedModelAccessService _untypedService;
    private readonly ILogService _logService;

    public AppContextExtractor(
        IModel model,
        IDomainModelService domainModelService,
        IMicroflowService microflowService,
        IUntypedModelAccessService untypedModelAccessService,
        ILogService logService)
    {
        _model = model;
        _untypedService = untypedModelAccessService;
        _logService = logService;
        _domainModelReader = new DomainModelReader(model, domainModelService);
        _microflowReader = new MicroflowReader(model, microflowService, untypedModelAccessService, logService);
        _pageReader = new PageReader(model);
    }

    /// <summary>
    /// Extract a compact summary of the entire app for the system prompt.
    /// </summary>
    /// <param name="contextDepth">"full" for all modules, "module" for non-marketplace only</param>
    public AppContextDto ExtractAppContext(string contextDepth = "full")
    {
        var context = new AppContextDto { AppName = _model.Root.Name };
        var modules = _model.Root.GetModules();

        foreach (var module in modules)
        {
            // Marketplace modules add thousands of entities/microflows that overwhelm the prompt
            if (module.FromAppStore)
                continue;

            var moduleSummary = new ModuleSummaryDto
            {
                Name = module.Name,
                FromAppStore = module.FromAppStore,
                Entities = _domainModelReader.GetEntitySummaries(module),
                Associations = _domainModelReader.GetAssociationSummaries(module),
                Microflows = _microflowReader.GetMicroflowSummaries(module),
                Pages = _pageReader.GetPageSummaries(module),
                Enumerations = _domainModelReader.GetEnumerationSummaries(module),
                OtherDocuments = GetOtherDocumentSummaries(module)
            };

            context.Modules.Add(moduleSummary);
        }

        return context;
    }

    /// <summary>
    /// Extract app context with detail level controlled by contextDepth:
    ///   "full"    — all entity details, microflow activity sequences, associations (default)
    ///   "summary" — module/entity/microflow names only, no attribute details or activity sequences (~5x smaller)
    ///   "none"    — empty context, Claude uses tools for everything
    /// </summary>
    public AppContextDto ExtractDetailedAppContext(string contextDepth = "full", int maxEntitiesForDetails = 200)
    {
        var context = new AppContextDto { AppName = _model.Root.Name };

        if (contextDepth == "none")
            return context;

        var modules = _model.Root.GetModules();
        var totalEntityCount = 0;
        var isSummary = contextDepth == "summary";

        foreach (var module in modules)
        {
            if (module.FromAppStore)
                continue;

            var entities = _domainModelReader.GetEntitySummaries(module);
            totalEntityCount += entities.Count;

            var moduleSummary = new ModuleSummaryDto
            {
                Name = module.Name,
                FromAppStore = module.FromAppStore,
                Entities = entities,
                Pages = _pageReader.GetPageSummaries(module),
                Enumerations = _domainModelReader.GetEnumerationSummaries(module)
            };

            if (isSummary)
            {
                moduleSummary.Microflows = _microflowReader.GetMicroflowSummaries(module);
            }
            else
            {
                moduleSummary.Associations = _domainModelReader.GetAssociationSummaries(module);

                if (totalEntityCount <= maxEntitiesForDetails)
                    moduleSummary.EntityDetails = _domainModelReader.GetAllEntityDetails(module);

                moduleSummary.Microflows = _microflowReader.GetEnrichedMicroflowSummaries(module);
            }

            moduleSummary.OtherDocuments = GetOtherDocumentSummaries(module);

            context.Modules.Add(moduleSummary);
        }

        return context;
    }

    /// <summary>
    /// Find a module by name.
    /// </summary>
    public IModule? FindModule(string moduleName)
    {
        return _model.Root.GetModules().FirstOrDefault(m =>
            string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Metamodel types to enumerate via the Untyped Model API.
    /// The typed API (GetDocuments) doesn't return nanoflows, scheduled events, etc.
    /// </summary>
    private static readonly (string MetamodelType, string AideType)[] UntypedDocumentTypes =
    {
        ("Microflows$Nanoflow", "nanoflow"),
        ("ScheduledEvents$ScheduledEvent", "scheduled_event"),
        ("Rest$PublishedRestService", "rest_service"),
        ("Rest$PublishedODataService", "odata_service"),
        ("Mappings$ImportMapping", "import_mapping"),
        ("Mappings$ExportMapping", "export_mapping"),
        ("Pages$Snippet", "snippet"),
        ("Pages$Layout", "layout"),
        ("Microflows$Rule", "rule"),
        ("Pages$BuildingBlock", "building_block"),
        ("DocumentTemplates$DocumentTemplate", "document_template"),
    };

    private List<DocumentSummaryDto> GetOtherDocumentSummaries(IModule module)
    {
        var results = new List<DocumentSummaryDto>();
        try
        {
            var untypedRoot = _untypedService.GetUntypedModel(_model);
            var modulePrefix = module.Name + ".";

            foreach (var (metamodelType, aideType) in UntypedDocumentTypes)
            {
                try
                {
                    foreach (var unit in untypedRoot.GetUnitsOfType(metamodelType))
                    {
                        var qn = unit.QualifiedName;
                        if (qn != null && qn.StartsWith(modulePrefix, StringComparison.Ordinal))
                        {
                            var name = qn.Substring(modulePrefix.Length);
                            results.Add(new DocumentSummaryDto { Name = name, Type = aideType });
                        }
                    }
                }
                catch
                {
                    // Metamodel type may not exist in this Studio Pro version — skip silently
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Info($"AIDE Lite: Error enumerating other documents in '{module.Name}': {ex.Message}");
        }
        return results;
    }
}
