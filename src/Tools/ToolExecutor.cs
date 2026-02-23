// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool executor — dispatches tool calls by name and handles errors
// ============================================================================
using System.Text.Json.Nodes;
using AideLite.Models;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Tools;

public class ToolExecutor
{
    private readonly ToolRegistry _registry;
    private readonly ILogService _logService;

    public ToolExecutor(ToolRegistry registry, ILogService logService)
    {
        _registry = registry;
        _logService = logService;
    }

    public ToolResult Execute(string toolName, string inputJson, bool isAskMode = false)
    {
        var tool = _registry.GetTool(toolName);
        if (tool == null)
        {
            return ToolResult.Fail($"Unknown tool: {toolName}");
        }

        // Defense-in-depth: reject write tools at execution time when in Ask mode,
        // even though they should already be excluded from the API tool definitions.
        if (isAskMode && tool.IsWriteTool)
        {
            _logService.Info($"AIDE Lite: BLOCKED write tool '{toolName}' in Ask mode");
            return ToolResult.Fail($"Tool '{toolName}' is not available in Ask mode. Switch to Agent mode to make changes.");
        }

        try
        {
            var input = JsonNode.Parse(inputJson)?.AsObject() ?? new JsonObject();
            _logService.Info($"AIDE Lite: Executing tool '{toolName}'");
            var result = tool.Execute(input);
            _logService.Info($"AIDE Lite: Tool '{toolName}' completed: {result.Summary}");
            return result;
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Tool '{toolName}' failed: {ex.Message}");
            return ToolResult.Fail($"Tool execution failed: {ex.Message}");
        }
    }
}
