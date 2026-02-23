// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: rename_microflow — renames a microflow (Mendix auto-updates references)
// ============================================================================
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;
using AideLite.ModelWriters;

namespace AideLite.Tools;

public class RenameMicroflowTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly MicroflowGenerator _generator;

    public RenameMicroflowTool(AppContextExtractor extractor, MicroflowGenerator generator)
    {
        _extractor = extractor;
        _generator = generator;
    }

    public string Name => "rename_microflow";
    public bool IsWriteTool => true;
    public string Description => "Rename an existing microflow. Mendix automatically updates all references (microflow calls, page buttons, etc.) to use the new name.";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module containing the microflow" },
            ["currentName"] = new JsonObject { ["type"] = "string", ["description"] = "Current name of the microflow to rename" },
            ["newName"] = new JsonObject { ["type"] = "string", ["description"] = "New name for the microflow (use ACT_, SUB_, DS_, or VAL_ prefix)" }
        },
        ["required"] = new JsonArray("moduleName", "currentName", "newName")
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var currentName = input["currentName"]?.GetValue<string>();
        var newName = input["newName"]?.GetValue<string>();

        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(currentName) || string.IsNullOrEmpty(newName))
            return ToolResult.Fail("moduleName, currentName, and newName are all required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        try
        {
            var qualifiedName = _generator.RenameMicroflow(module, currentName, newName);
            return ToolResult.Ok(
                $"Microflow renamed from '{moduleName}.{currentName}' to '{qualifiedName}'. Tell the user to press F4 to refresh the App Explorer.",
                $"Renamed microflow to {qualifiedName}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to rename microflow: {ex.Message}");
        }
    }
}
