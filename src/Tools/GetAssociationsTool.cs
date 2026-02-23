// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_associations — lists all associations in a module
// ============================================================================
using System.Text;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetAssociationsTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly DomainModelReader _domainModelReader;

    public GetAssociationsTool(AppContextExtractor extractor, DomainModelReader domainModelReader)
    {
        _extractor = extractor;
        _domainModelReader = domainModelReader;
    }

    public string Name => "get_associations";
    public string Description => "Get all associations in a module with parent/child entities and types";
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

        var associations = _domainModelReader.GetAssociationDetails(module);
        var sb = new StringBuilder();
        sb.AppendLine($"Associations in {moduleName}:");
        foreach (var a in associations)
            sb.AppendLine($"- {a.Name}: {a.Parent} -> {a.Child} ({a.Type}, owner: {a.Owner})");

        return ToolResult.Ok(sb.ToString(), $"Found {associations.Count} associations in {moduleName}");
    }
}
