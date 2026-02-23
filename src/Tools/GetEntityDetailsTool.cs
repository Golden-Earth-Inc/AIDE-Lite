// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_entity_details — full entity with attributes, types, and associations
// ============================================================================
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetEntityDetailsTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly DomainModelReader _domainModelReader;

    public GetEntityDetailsTool(AppContextExtractor extractor, DomainModelReader domainModelReader)
    {
        _extractor = extractor;
        _domainModelReader = domainModelReader;
    }

    public string Name => "get_entity_details";
    public string Description => "Get full details of an entity including attributes, types, and associations";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module" },
            ["entityName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the entity" }
        },
        ["required"] = new JsonArray("moduleName", "entityName")
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var entityName = input["entityName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(entityName))
            return ToolResult.Fail("moduleName and entityName are required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        var entity = _domainModelReader.GetEntityDetails(module, entityName);
        if (entity == null)
            return ToolResult.Fail($"Entity '{entityName}' not found in module '{moduleName}'");

        return ToolResult.Ok(entity.ToSummary(), $"Entity {entity.QualifiedName}: {entity.Attributes.Count} attrs, {entity.Associations.Count} assocs");
    }
}
