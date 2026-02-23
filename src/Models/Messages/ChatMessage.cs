// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Chat message model — conversation history entries with tool-use tracking
// ============================================================================

using System.Text.Json.Serialization;

namespace AideLite.Models.Messages;

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    // Content is `object` because it can be a plain string or a JSON array
    // of content blocks (text + tool_use or tool_result blocks)
    [JsonPropertyName("content")]
    public object Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // HasToolUse/HasToolResult flags prevent orphaning during conversation trimming —
    // a tool_use message is never trimmed without its corresponding tool_result (and vice versa)
    public bool HasToolUse { get; set; }
    public bool HasToolResult { get; set; }

    public static ChatMessage User(string text) => new() { Role = "user", Content = text };
    public static ChatMessage Assistant(string text) => new() { Role = "assistant", Content = text };
}
