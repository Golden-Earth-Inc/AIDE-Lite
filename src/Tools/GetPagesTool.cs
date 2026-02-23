// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_pages — lists pages in a module
// ============================================================================
using System.Text;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetPagesTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly PageReader _pageReader;

    public GetPagesTool(AppContextExtractor extractor, PageReader pageReader)
    {
        _extractor = extractor;
        _pageReader = pageReader;
    }

    public string Name => "get_pages";
    public string Description => "List all pages in a module";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module" }
        },
        ["required"] = new JsonArray("moduleName")
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(moduleName))
            return ToolResult.Fail("moduleName is required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        var pages = _pageReader.GetPageSummaries(module);
        var sb = new StringBuilder();
        sb.AppendLine($"Pages in {moduleName}:");
        foreach (var p in pages)
            sb.AppendLine($"- {p.Name}");

        return ToolResult.Ok(sb.ToString(), $"Found {pages.Count} pages in {moduleName}");
    }
}
