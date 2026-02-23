// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Generic document reader — reads any document via Untyped Model API
// ============================================================================
using AideLite.Services;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.UntypedModel;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.ModelReaders;

public class GenericDocumentReader
{
    private readonly IModel _model;
    private readonly IUntypedModelAccessService _untypedService;
    private readonly ILogService _log;

    private const int MaxDepth = 3;
    private const int MaxItemsPerType = 20;

    internal static readonly Dictionary<string, string> TypeToMetamodel = new(StringComparer.OrdinalIgnoreCase)
    {
        { "nanoflow", "Microflows$Nanoflow" },
        { "scheduled_event", "ScheduledEvents$ScheduledEvent" },
        { "rest_service", "Rest$PublishedRestService" },
        { "odata_service", "Rest$PublishedODataService" },
        { "import_mapping", "Mappings$ImportMapping" },
        { "export_mapping", "Mappings$ExportMapping" },
        { "snippet", "Pages$Snippet" },
        { "layout", "Pages$Layout" },
        { "rule", "Microflows$Rule" },
        { "building_block", "Pages$BuildingBlock" },
        { "document_template", "DocumentTemplates$DocumentTemplate" },
    };

    private static readonly Dictionary<string, string[]> KnownPropertiesByType = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Microflows$Nanoflow", new[] { "name", "documentation", "returnType", "markAsUsed" } },
        { "ScheduledEvents$ScheduledEvent", new[] { "name", "documentation", "enabled", "startDateTime", "interval", "intervalType", "microflow" } },
        { "Rest$PublishedRestService", new[] { "name", "documentation", "path", "version", "serviceName", "authenticationType" } },
        { "Rest$PublishedODataService", new[] { "name", "documentation", "path", "version", "serviceName", "authenticationType" } },
        { "Mappings$ImportMapping", new[] { "name", "documentation" } },
        { "Mappings$ExportMapping", new[] { "name", "documentation" } },
        { "Pages$Snippet", new[] { "name", "documentation" } },
        { "Pages$Layout", new[] { "name", "documentation" } },
        { "Microflows$Rule", new[] { "name", "documentation", "returnType" } },
        { "Pages$BuildingBlock", new[] { "name", "documentation" } },
        { "DocumentTemplates$DocumentTemplate", new[] { "name", "documentation" } },
    };

    private static readonly string[] CommonProperties = { "name", "documentation" };

    public GenericDocumentReader(IModel model, IUntypedModelAccessService untypedService, ILogService log)
    {
        _model = model;
        _untypedService = untypedService;
        _log = log;
    }

    public string? GetDocumentDetails(IModule module, string documentName)
    {
        try
        {
            var qualifiedName = $"{module.Name}.{documentName}";

            // Try each known metamodel type via the Untyped Model API.
            // We go straight to the untyped API because the typed API (GetDocuments)
            // doesn't return nanoflows, scheduled events, and other non-core document types.
            foreach (var kvp in TypeToMetamodel)
            {
                var details = ReadViaUntypedModel(qualifiedName, kvp.Value);
                if (details != null)
                    return details;
            }

            // Fallback: try the typed API for documents the untyped search missed
            IDocument? targetDoc = null;
            foreach (var doc in MicroflowReader.GetAllDocumentsRecursive<IDocument>(module))
            {
                if (string.Equals(doc.Name, documentName, StringComparison.OrdinalIgnoreCase))
                {
                    targetDoc = doc;
                    break;
                }
            }

            if (targetDoc != null)
            {
                var detectedType = DocumentTypeDetector.Detect(targetDoc);
                return $"Document: {qualifiedName}\nType: {detectedType}\n(No additional details available via untyped model)";
            }

            return null;
        }
        catch (Exception ex)
        {
            _log.Info($"AIDE Lite: GenericDocumentReader error for '{documentName}' in '{module.Name}': {ex.Message}");
            return null;
        }
    }

    private string? ReadViaUntypedModel(string qualifiedName, string metamodelType)
    {
        try
        {
            var untypedRoot = _untypedService.GetUntypedModel(_model);
            foreach (var unit in untypedRoot.GetUnitsOfType(metamodelType))
            {
                if (unit.QualifiedName == qualifiedName)
                    return FormatModelUnit(unit, metamodelType);
            }
        }
        catch (Exception ex)
        {
            _log.Info($"AIDE Lite: ReadViaUntypedModel failed for '{qualifiedName}' ({metamodelType}): {ex.Message}");
        }
        return null;
    }

    private string FormatModelUnit(IModelUnit unit, string metamodelType)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Document: {unit.QualifiedName}");
        sb.AppendLine($"Type: {metamodelType}");

        // Read known properties
        var properties = KnownPropertiesByType.GetValueOrDefault(metamodelType, CommonProperties);
        foreach (var propName in properties)
        {
            var value = SafeGetStringProperty(unit, propName);
            if (!string.IsNullOrEmpty(value))
                sb.AppendLine($"{propName}: {value}");
        }

        // Traverse child elements
        try
        {
            var elements = unit.GetElements().ToList();
            if (elements.Count > 0)
            {
                sb.AppendLine();
                TraverseElements(sb, elements, depth: 1);
            }
        }
        catch { /* element traversal failed */ }

        return sb.ToString();
    }

    private void TraverseElements(System.Text.StringBuilder sb, List<IModelElement> elements, int depth)
    {
        if (depth > MaxDepth) return;

        var indent = new string(' ', depth * 2);

        // Group elements by type
        var grouped = elements
            .GroupBy(e => SimplifyElementType(e.Type))
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var items = group.Take(MaxItemsPerType).ToList();
            var total = group.Count();
            var countSuffix = total > MaxItemsPerType ? $" (showing {MaxItemsPerType} of {total})" : "";

            sb.AppendLine($"{indent}[{group.Key}]{countSuffix}:");

            foreach (var element in items)
            {
                var name = SafeGetStringProperty(element, "name");
                var caption = SafeGetStringProperty(element, "caption");
                var label = name ?? caption ?? element.Type;

                sb.AppendLine($"{indent}  - {label}");

                // Read a few useful properties of child elements
                var doc = SafeGetStringProperty(element, "documentation");
                if (!string.IsNullOrEmpty(doc))
                    sb.AppendLine($"{indent}    documentation: {Truncate(doc, 200)}");

                var path = SafeGetStringProperty(element, "path");
                if (!string.IsNullOrEmpty(path))
                    sb.AppendLine($"{indent}    path: {path}");

                var expression = SafeGetStringProperty(element, "expression");
                if (!string.IsNullOrEmpty(expression))
                    sb.AppendLine($"{indent}    expression: {Truncate(expression, 200)}");

                // Recurse into children
                if (depth < MaxDepth)
                {
                    try
                    {
                        var children = element.GetElements().ToList();
                        if (children.Count > 0)
                            TraverseElements(sb, children, depth + 1);
                    }
                    catch { /* ignore */ }
                }
            }
        }
    }

    private static string SimplifyElementType(string type)
    {
        var dollarIdx = type.IndexOf('$');
        return dollarIdx >= 0 ? type[(dollarIdx + 1)..] : type;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length > maxLength ? value[..maxLength] + "..." : value;
    }

    private static string? SafeGetStringProperty(IModelStructure structure, string propertyName)
    {
        try
        {
            var prop = structure.GetProperty(propertyName);
            return prop?.Value as string;
        }
        catch
        {
            return null;
        }
    }
}
