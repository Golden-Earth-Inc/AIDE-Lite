// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool registry — registers tools and builds Claude API tool definitions
// ============================================================================
using System.Text.Json.Nodes;

namespace AideLite.Tools;

public class ToolRegistry
{
    // Keyed by tool name (e.g. "get_modules") for O(1) lookup during execution
    private readonly Dictionary<string, IClaudeTool> _tools = new();

    public void Register(IClaudeTool tool)
    {
        _tools[tool.Name] = tool;
    }

    public IClaudeTool? GetTool(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    public IReadOnlyDictionary<string, IClaudeTool> GetAllTools() => _tools;

    /// <summary>
    /// Build the tools array for the Claude API request.
    /// When includeWriteTools is false, write tools (create/edit/rename/replace) are excluded to save tokens.
    /// When withCacheControl is true, a cache_control breakpoint is added to the last tool
    /// so Anthropic caches all tool definitions across requests.
    /// </summary>
    public List<Dictionary<string, object>> BuildToolDefinitions(bool includeWriteTools = true, bool withCacheControl = true)
    {
        var definitions = new List<Dictionary<string, object>>();
        foreach (var tool in _tools.Values)
        {
            if (!includeWriteTools && tool.IsWriteTool) continue;
            definitions.Add(new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = tool.InputSchema
            });
        }

        if (withCacheControl && definitions.Count > 0)
            definitions[^1]["cache_control"] = new { type = "ephemeral" };

        return definitions;
    }
}
