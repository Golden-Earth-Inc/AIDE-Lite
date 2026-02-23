// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_microflow_details — full microflow structure with activities and flow control
// ============================================================================
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetMicroflowDetailsTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly MicroflowReader _microflowReader;

    public GetMicroflowDetailsTool(AppContextExtractor extractor, MicroflowReader microflowReader)
    {
        _extractor = extractor;
        _microflowReader = microflowReader;
    }

    public string Name => "get_microflow_details";
    public string Description => "Get full details of a microflow including parameters, return type, activities, flow control elements (decisions, loops, merges), and sequence flows showing how elements connect";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module" },
            ["microflowName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the microflow" }
        },
        ["required"] = new JsonArray("moduleName", "microflowName")
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var microflowName = input["microflowName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(microflowName))
            return ToolResult.Fail("moduleName and microflowName are required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        var mf = _microflowReader.GetMicroflowDetails(module, microflowName);
        if (mf == null)
            return ToolResult.Fail($"Microflow '{microflowName}' not found in module '{moduleName}'");

        return ToolResult.Ok(mf.ToSummary(), $"Microflow {mf.QualifiedName}: {mf.Parameters.Count} params, {mf.Activities.Count} activities");
    }
}
