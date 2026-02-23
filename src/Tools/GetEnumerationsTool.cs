// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_enumerations — lists enumerations and their values
// ============================================================================
using System.Text;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetEnumerationsTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly DomainModelReader _domainModelReader;

    public GetEnumerationsTool(AppContextExtractor extractor, DomainModelReader domainModelReader)
    {
        _extractor = extractor;
        _domainModelReader = domainModelReader;
    }

    public string Name => "get_enumerations";
    public string Description => "Get all enumerations in a module with their values";
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

        var enums = _domainModelReader.GetEnumerationSummaries(module);
        var sb = new StringBuilder();
        sb.AppendLine($"Enumerations in {moduleName}:");
        foreach (var e in enums)
            sb.AppendLine($"- {e.Name}: [{string.Join(", ", e.Values)}]");

        return ToolResult.Ok(sb.ToString(), $"Found {enums.Count} enumerations in {moduleName}");
    }
}
