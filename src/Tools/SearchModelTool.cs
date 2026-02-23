// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: search_model — searches elements by name across all non-Marketplace modules
// ============================================================================
using System.Text;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;

namespace AideLite.Tools;

public class SearchModelTool : IClaudeTool
{
    private readonly IModel _model;
    private readonly AppContextExtractor _extractor;

    // Needs direct IModel access (not just extractor) to iterate all modules including folders
    public SearchModelTool(IModel model, AppContextExtractor extractor)
    {
        _model = model;
        _extractor = extractor;
    }

    public string Name => "search_model";
    public string Description => "Search for model elements (entities, microflows, pages) by name across all modules";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Search term (case-insensitive partial match)" },
            ["elementType"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Filter by element type",
                ["enum"] = new JsonArray("entity", "microflow", "page", "all")
            }
        },
        ["required"] = new JsonArray("query")
    };

    public ToolResult Execute(JsonObject input)
    {
        var query = input["query"]?.GetValue<string>();
        if (string.IsNullOrEmpty(query))
            return ToolResult.Fail("query is required");

        var elementType = input["elementType"]?.GetValue<string>() ?? "all";
        var sb = new StringBuilder();
        var matchCount = 0;

        // Walk every module, skipping Marketplace to avoid noisy results
        foreach (var module in _model.Root.GetModules())
        {
            if (module.FromAppStore) continue;

            if (elementType is "entity" or "all")
            {
                foreach (var entity in module.DomainModel.GetEntities())
                {
                    if (entity.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"[Entity] {module.Name}.{entity.Name}");
                        matchCount++;
                    }
                }
            }

            if (elementType is "microflow" or "all")
            {
                foreach (var mf in MicroflowReader.GetAllDocumentsRecursive<IMicroflow>(module))
                {
                    if (mf.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"[Microflow] {module.Name}.{mf.Name}");
                        matchCount++;
                    }
                }
            }

            if (elementType is "page" or "all")
            {
                foreach (var page in MicroflowReader.GetAllDocumentsRecursive<IPage>(module))
                {
                    if (page.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"[Page] {module.Name}.{page.Name}");
                        matchCount++;
                    }
                }
            }
        }

        if (matchCount == 0)
            return ToolResult.Ok($"No results found for '{query}'", "No matches");

        return ToolResult.Ok(sb.ToString(), $"Found {matchCount} matches for '{query}'");
    }
}
