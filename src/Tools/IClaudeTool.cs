// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool interface — contract for all Claude tool-use implementations
// ============================================================================
using System.Text.Json.Nodes;
using AideLite.Models;

namespace AideLite.Tools;

/// <summary>
/// Every tool must expose its name, description, and JSON Schema for Claude,
/// plus an Execute method that returns a ToolResult. Read-only tools leave
/// IsWriteTool as false; write tools override it so the registry can
/// exclude them when only read access is needed.
/// </summary>
public interface IClaudeTool
{
    string Name { get; }
    string Description { get; }
    JsonObject InputSchema { get; }
    bool IsWriteTool => false;
    ToolResult Execute(JsonObject input);
}
