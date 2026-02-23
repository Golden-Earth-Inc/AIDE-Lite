// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_modules — lists all app modules with document counts
// ============================================================================
using System.Text;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetModulesTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    public GetModulesTool(AppContextExtractor extractor) => _extractor = extractor;

    public string Name => "get_modules";
    public string Description => "List all modules in the app with entity, microflow, and page counts";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["required"] = new JsonArray()
    };

    public ToolResult Execute(JsonObject input)
    {
        var context = _extractor.ExtractAppContext();
        var sb = new StringBuilder();
        foreach (var module in context.Modules)
        {
            var marker = module.FromAppStore ? " [Marketplace]" : "";
            sb.AppendLine($"- {module.Name}{marker}: {module.Entities.Count} entities, " +
                $"{module.Microflows.Count} microflows, {module.Pages.Count} pages, " +
                $"{module.Enumerations.Count} enumerations");
        }
        var result = sb.ToString();
        return ToolResult.Ok(result, $"Found {context.Modules.Count} modules");
    }
}
