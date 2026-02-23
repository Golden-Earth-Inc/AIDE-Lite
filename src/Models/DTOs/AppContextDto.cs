// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// App context DTOs — compact model representation sent to Claude as context
// ============================================================================

namespace AideLite.Models.DTOs;

public class AppContextDto
{
    public string AppName { get; set; } = string.Empty;
    public List<ModuleSummaryDto> Modules { get; set; } = new();

    public string ToCondensedSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== APP: {AppName} ===");

        foreach (var module in Modules)
        {
            var marker = module.FromAppStore ? " [Marketplace]" : "";
            sb.Append($"Module {module.Name}{marker}: ");
            sb.Append($"{module.Entities.Count} entities");
            if (module.Associations.Count > 0) sb.Append($", {module.Associations.Count} assocs");
            sb.Append($", {module.Microflows.Count} mf");
            if (module.Pages.Count > 0) sb.Append($", {module.Pages.Count} pages");
            if (module.Enumerations.Count > 0) sb.Append($", {module.Enumerations.Count} enums");
            sb.AppendLine();

            if (module.Entities.Count > 0 && !module.FromAppStore)
                sb.AppendLine($"  Entities: {string.Join(", ", module.Entities.Select(e => e.Name))}");

            if (module.Microflows.Count > 0 && !module.FromAppStore)
            {
                var mfNames = module.Microflows.Select(m => m.Name).ToList();
                if (mfNames.Count <= 20)
                    sb.AppendLine($"  Microflows: {string.Join(", ", mfNames)}");
                else
                    sb.AppendLine($"  Microflows: {string.Join(", ", mfNames.Take(20))} ...and {mfNames.Count - 20} more");
            }

            if (module.OtherDocuments.Count > 0 && !module.FromAppStore)
            {
                var grouped = module.OtherDocuments.GroupBy(d => d.Type)
                    .Select(g => $"{g.Count()} {g.Key}s");
                sb.AppendLine($"  Other: {string.Join(", ", grouped)}");
            }
        }

        sb.AppendLine("Use get_entities, get_associations, get_enumerations, get_pages, get_document_details tools for full details.");
        return sb.ToString();
    }

    /// <summary>
    /// Detailed compact summary with full entity attributes and microflow activity sequences.
    /// Front-loaded into the system prompt so Claude can answer questions without tool calls.
    /// Uses two-pass rendering: modules that don't fit in detailed form are condensed to
    /// names-only summaries instead of being dropped entirely.
    /// </summary>
    public string ToDetailedCompactSummary(int maxChars = 80000)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== APP MODEL (use this for lookups — call tools only to verify after modifications) ===");

        // Pre-render each module in detailed form
        var moduleBlocks = Modules.Select(m => (module: m, detailed: RenderModuleDetailed(m))).ToList();

        var budget = maxChars - 200; // reserve for header + footer
        var condensedAny = false;

        foreach (var (module, detailed) in moduleBlocks)
        {
            if (!condensedAny && sb.Length + detailed.Length <= budget)
            {
                sb.Append(detailed);
            }
            else
            {
                condensedAny = true;
                sb.Append(RenderModuleCondensed(module));
            }
        }

        if (condensedAny)
            sb.AppendLine("\n[Some modules condensed due to size. Use tools for full attribute/activity details.]");

        sb.AppendLine("\n=== END APP MODEL ===");
        return sb.ToString();
    }

    private static string RenderModuleDetailed(ModuleSummaryDto module)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"\n## Module: {module.Name}{(module.FromAppStore ? " [Marketplace]" : "")}");

        // Full entity details when available (one line per entity with all attrs)
        if (module.EntityDetails.Count > 0)
        {
            foreach (var entity in module.EntityDetails)
            {
                var attrs = string.Join(", ", entity.Attributes.Select(a => $"{a.Name}:{a.TypeName}"));
                var genPart = !string.IsNullOrEmpty(entity.Generalization) ? $" (inherits {entity.Generalization})" : "";
                sb.AppendLine($"  Entity {entity.Name}{genPart}: {attrs}");

                if (entity.Associations.Count > 0)
                {
                    var assocs = string.Join(", ", entity.Associations.Select(a =>
                    {
                        var typeSymbol = a.Type == "ReferenceSet" ? "*→*" :
                                         a.Parent == entity.Name ? "*→1" : "1→*";
                        var target = a.Parent == entity.Name ? a.Child : a.Parent;
                        return $"{a.Name}({typeSymbol} {target})";
                    }));
                    sb.AppendLine($"    assocs: {assocs}");
                }
            }
        }
        // Fallback for modules where detailed extraction was skipped
        else if (module.Entities.Count > 0)
        {
            var entityList = string.Join(", ", module.Entities.Select(e => $"{e.Name}({e.AttributeCount} attrs)"));
            sb.AppendLine($"  Entities: {entityList}");
        }

        // Microflows with params and activity sequence
        if (module.Microflows.Count > 0)
        {
            sb.AppendLine("  Microflows:");
            foreach (var mf in module.Microflows)
            {
                var paramPart = "";
                if (mf.Parameters.Count > 0)
                    paramPart = string.Join(", ", mf.Parameters.Select(p => $"{p.TypeName} {p.Name}"));

                var returnPart = !string.IsNullOrEmpty(mf.ReturnType) ? $" → {mf.ReturnType}" : "";
                var activityPart = !string.IsNullOrEmpty(mf.ActivitySummary) ? $": {mf.ActivitySummary}" : "";

                sb.AppendLine($"    {mf.Name}({paramPart}{returnPart}){activityPart}");
            }
        }

        if (module.Pages.Count > 0)
        {
            var pageList = string.Join(", ", module.Pages.Select(p => p.Name));
            sb.AppendLine($"  Pages: {pageList}");
        }

        if (module.Enumerations.Count > 0)
        {
            var enumList = string.Join(", ", module.Enumerations.Select(e =>
                $"{e.Name}({string.Join(",", e.Values)})"));
            sb.AppendLine($"  Enumerations: {enumList}");
        }

        if (module.OtherDocuments.Count > 0)
        {
            var grouped = module.OtherDocuments.GroupBy(d => d.Type);
            foreach (var group in grouped)
            {
                var label = group.Key switch
                {
                    "nanoflow" => "Nanoflows",
                    "scheduled_event" => "Scheduled Events",
                    "rest_service" => "REST Services",
                    "odata_service" => "OData Services",
                    "snippet" => "Snippets",
                    "layout" => "Layouts",
                    "rule" => "Rules",
                    "import_mapping" => "Import Mappings",
                    "export_mapping" => "Export Mappings",
                    "building_block" => "Building Blocks",
                    "document_template" => "Document Templates",
                    _ => group.Key
                };
                sb.AppendLine($"  {label}: {string.Join(", ", group.Select(d => d.Name))}");
            }
        }

        return sb.ToString();
    }

    private static string RenderModuleCondensed(ModuleSummaryDto module)
    {
        const int maxNames = 30;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"\n## Module: {module.Name}{(module.FromAppStore ? " [Marketplace]" : "")} [condensed]");

        // Entities — names only
        var entityNames = module.EntityDetails.Count > 0
            ? module.EntityDetails.Select(e => e.Name).ToList()
            : module.Entities.Select(e => e.Name).ToList();
        if (entityNames.Count > 0)
            sb.AppendLine($"  Entities: {FormatNameList(entityNames, maxNames)}");

        // Microflows — names only
        if (module.Microflows.Count > 0)
            sb.AppendLine($"  Microflows: {FormatNameList(module.Microflows.Select(m => m.Name).ToList(), maxNames)}");

        // Pages
        if (module.Pages.Count > 0)
            sb.AppendLine($"  Pages: {FormatNameList(module.Pages.Select(p => p.Name).ToList(), maxNames)}");

        // Enumerations with values
        if (module.Enumerations.Count > 0)
        {
            var enumList = module.Enumerations.Select(e =>
                $"{e.Name}({string.Join(",", e.Values)})");
            sb.AppendLine($"  Enumerations: {string.Join(", ", enumList)}");
        }

        // Other documents grouped by type
        if (module.OtherDocuments.Count > 0)
        {
            var grouped = module.OtherDocuments.GroupBy(d => d.Type);
            foreach (var group in grouped)
            {
                var label = group.Key switch
                {
                    "nanoflow" => "Nanoflows",
                    "scheduled_event" => "Scheduled Events",
                    "rest_service" => "REST Services",
                    "odata_service" => "OData Services",
                    "snippet" => "Snippets",
                    "layout" => "Layouts",
                    "rule" => "Rules",
                    "import_mapping" => "Import Mappings",
                    "export_mapping" => "Export Mappings",
                    "building_block" => "Building Blocks",
                    "document_template" => "Document Templates",
                    _ => group.Key
                };
                sb.AppendLine($"  {label}: {FormatNameList(group.Select(d => d.Name).ToList(), maxNames)}");
            }
        }

        return sb.ToString();
    }

    private static string FormatNameList(List<string> names, int max)
    {
        if (names.Count <= max)
            return string.Join(", ", names);
        return $"{string.Join(", ", names.Take(max))} ...and {names.Count - max} more";
    }

    public string ToCompactSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== APP STRUCTURE ===");

        foreach (var module in Modules)
        {
            sb.AppendLine($"Module: {module.Name}{(module.FromAppStore ? " [Marketplace]" : "")}");

            if (module.Entities.Count > 0)
            {
                var entityList = string.Join(", ", module.Entities.Select(e => $"{e.Name}({e.AttributeCount} attrs)"));
                sb.AppendLine($"  Entities: {entityList}");
            }

            if (module.Associations.Count > 0)
            {
                var assocList = string.Join(", ", module.Associations.Select(a => $"{a.Name}({a.Parent}->{a.Child},{a.Type})"));
                sb.AppendLine($"  Associations: {assocList}");
            }

            if (module.Microflows.Count > 0)
            {
                var mfList = string.Join(", ", module.Microflows.Select(m =>
                {
                    var info = m.Name;
                    if (m.ParameterCount > 0 || m.ReturnType != null)
                    {
                        var details = new List<string>();
                        if (m.ParameterCount > 0) details.Add($"{m.ParameterCount} params");
                        if (m.ReturnType != null) details.Add($"→{m.ReturnType}");
                        info += $"({string.Join(",", details)})";
                    }
                    return info;
                }));
                sb.AppendLine($"  Microflows: {mfList} ({module.Microflows.Count} total)");
            }

            if (module.Pages.Count > 0)
            {
                var pageList = string.Join(", ", module.Pages.Select(p => p.Name));
                sb.AppendLine($"  Pages: {pageList} ({module.Pages.Count} total)");
            }

            if (module.Enumerations.Count > 0)
            {
                var enumList = string.Join(", ", module.Enumerations.Select(e =>
                    $"{e.Name}({string.Join(",", e.Values)})"));
                sb.AppendLine($"  Enumerations: {enumList}");
            }

            if (module.OtherDocuments.Count > 0)
            {
                var grouped = module.OtherDocuments.GroupBy(d => d.Type)
                    .Select(g => $"{g.Count()} {g.Key}s");
                sb.AppendLine($"  Other: {string.Join(", ", grouped)}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("=== END APP STRUCTURE ===");
        return sb.ToString();
    }
}

public class ModuleSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public bool FromAppStore { get; set; }
    public List<EntitySummaryDto> Entities { get; set; } = new();
    public List<AssociationSummaryDto> Associations { get; set; } = new();
    public List<MicroflowSummaryDto> Microflows { get; set; } = new();
    public List<PageSummaryDto> Pages { get; set; } = new();
    public List<EnumerationSummaryDto> Enumerations { get; set; } = new();

    /// <summary>Full entity details for system prompt front-loading (populated by ExtractDetailedAppContext).</summary>
    public List<EntityDto> EntityDetails { get; set; } = new();

    /// <summary>Other documents (nanoflows, scheduled events, REST services, etc.) for system prompt awareness.</summary>
    public List<DocumentSummaryDto> OtherDocuments { get; set; } = new();
}

public class EntitySummaryDto
{
    public string Name { get; set; } = string.Empty;
    public int AttributeCount { get; set; }
}

public class AssociationSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string Child { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class MicroflowSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string? ReturnType { get; set; }
    public int ParameterCount { get; set; }

    /// <summary>Parameter names and types (populated by GetEnrichedMicroflowSummaries).</summary>
    public List<MicroflowParameterDto> Parameters { get; set; } = new();

    /// <summary>Brief activity type sequence, e.g. "Retrieve → ChangeObject → Commit" (populated by GetEnrichedMicroflowSummaries).</summary>
    public string? ActivitySummary { get; set; }
}

public class PageSummaryDto
{
    public string Name { get; set; } = string.Empty;
}

public class EnumerationSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
}

public class DocumentSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
