// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_microflows — lists microflows with parameter and return info
// ============================================================================
using System.Text;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetMicroflowsTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly MicroflowReader _microflowReader;

    public GetMicroflowsTool(AppContextExtractor extractor, MicroflowReader microflowReader)
    {
        _extractor = extractor;
        _microflowReader = microflowReader;
    }

    public string Name => "get_microflows";
    public string Description => "List all microflows in a module";
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

        var microflows = _microflowReader.GetMicroflowSummaries(module);
        var sb = new StringBuilder();
        sb.AppendLine($"Microflows in {moduleName}:");
        foreach (var mf in microflows)
            sb.AppendLine($"- {mf.Name}");

        return ToolResult.Ok(sb.ToString(), $"Found {microflows.Count} microflows in {moduleName}");
    }
}
