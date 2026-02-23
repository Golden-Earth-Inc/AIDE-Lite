// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_entities — lists entities in a module
// ============================================================================
using System.Text;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetEntitiesTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly DomainModelReader _domainModelReader;

    public GetEntitiesTool(AppContextExtractor extractor, DomainModelReader domainModelReader)
    {
        _extractor = extractor;
        _domainModelReader = domainModelReader;
    }

    public string Name => "get_entities";
    public string Description => "List all entities in a module with attribute counts";
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

        var entities = _domainModelReader.GetEntitySummaries(module);
        var sb = new StringBuilder();
        sb.AppendLine($"Entities in {moduleName}:");
        foreach (var e in entities)
            sb.AppendLine($"- {e.Name} ({e.AttributeCount} attributes)");

        return ToolResult.Ok(sb.ToString(), $"Found {entities.Count} entities in {moduleName}");
    }
}
