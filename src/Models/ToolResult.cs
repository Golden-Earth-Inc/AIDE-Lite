// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool result model — standardized success/failure responses from tool execution
// ============================================================================

namespace AideLite.Models;

public class ToolResult
{
    public bool Success { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    // Summary is truncated for log output; full Content goes to Claude as tool_result
    public static ToolResult Ok(string content, string? summary = null) => new()
    {
        Success = true,
        Content = content,
        Summary = summary ?? (content.Length > 80 ? content[..80] + "..." : content)
    };

    // Failed results are sent to Claude with is_error: true in the tool_result block
    public static ToolResult Fail(string error) => new()
    {
        Success = false,
        Content = error,
        Summary = error
    };
}
