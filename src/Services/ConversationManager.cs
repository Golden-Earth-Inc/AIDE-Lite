// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Conversation history management — message building, trimming, and tool pairing
// ============================================================================
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.Models.Messages;

namespace AideLite.Services;

public class ConversationManager
{
    private readonly List<ChatMessage> _messages = new();
    private const int MaxMessages = 20;
    // Tool results (e.g., full entity dumps) can be very large — truncate to save tokens
    private const int MaxToolResultLength = 3000;

    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    public void AddUserMessage(string text)
    {
        CompactOldToolResults();
        _messages.Add(ChatMessage.User(text));
        TrimIfNeeded();
    }

    /// <summary>
    /// Add a user message that contains both images and text.
    /// Claude API requires content blocks: image blocks first, then text block.
    /// </summary>
    public void AddUserMessageWithImages(string text, List<ImageAttachment> images)
    {
        CompactOldToolResults();

        var contentBlocks = new List<object>();

        foreach (var img in images)
        {
            contentBlocks.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = img.MediaType,
                    data = img.Base64
                }
            });
        }

        contentBlocks.Add(new { type = "text", text });

        _messages.Add(new ChatMessage
        {
            Role = "user",
            Content = contentBlocks
        });
        TrimIfNeeded();
    }

    public void AddAssistantMessage(string text)
    {
        _messages.Add(ChatMessage.Assistant(text));
        TrimIfNeeded();
    }

    /// <summary>
    /// Add a combined assistant message with optional text and tool_use blocks.
    /// Claude API requires all content blocks from one turn in a single message.
    /// </summary>
    public void AddAssistantTurn(string? text, List<ToolCall> toolCalls)
    {
        var contentBlocks = new List<object>();

        if (!string.IsNullOrEmpty(text))
        {
            contentBlocks.Add(new { type = "text", text });
        }

        foreach (var tc in toolCalls)
        {
            // Parse InputJson string to JsonNode so it serializes as a JSON object, not a string
            JsonNode? parsedInput;
            try
            {
                parsedInput = JsonNode.Parse(tc.InputJson) ?? new JsonObject();
            }
            catch
            {
                parsedInput = new JsonObject();
            }

            contentBlocks.Add(new
            {
                type = "tool_use",
                id = tc.Id,
                name = tc.Name,
                input = parsedInput
            });
        }

        // HasToolUse flag prevents TrimIfNeeded from orphaning this message's tool_result pair
        _messages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = contentBlocks,
            HasToolUse = true
        });
        TrimIfNeeded();
    }

    /// <summary>
    /// Add all tool results as a single user message.
    /// Claude API requires tool results grouped in one message.
    /// Truncates large tool results to save tokens.
    /// </summary>
    public void AddToolResults(List<(string ToolUseId, string Content, bool IsError)> results)
    {
        var contentBlocks = new List<object>();

        foreach (var (toolUseId, content, isError) in results)
        {
            var truncatedContent = content.Length > MaxToolResultLength
                ? content[..MaxToolResultLength] + "\n[...truncated — use tool again for full details]"
                : content;

            if (isError)
            {
                contentBlocks.Add(new
                {
                    type = "tool_result",
                    tool_use_id = toolUseId,
                    content = truncatedContent,
                    is_error = true
                });
            }
            else
            {
                contentBlocks.Add(new
                {
                    type = "tool_result",
                    tool_use_id = toolUseId,
                    content = truncatedContent
                });
            }
        }

        _messages.Add(new ChatMessage
        {
            Role = "user",
            Content = contentBlocks,
            HasToolResult = true
        });
        TrimIfNeeded();
    }

    public void Clear()
    {
        _messages.Clear();
    }

    /// <summary>
    /// Serialize all messages to a JSON string for persistence.
    /// </summary>
    public string SerializeMessages()
    {
        var apiMessages = new List<object>();
        foreach (var msg in _messages)
        {
            apiMessages.Add(new
            {
                role = msg.Role,
                content = msg.Content,
                hasToolUse = msg.HasToolUse,
                hasToolResult = msg.HasToolResult
            });
        }
        return JsonSerializer.Serialize(apiMessages);
    }

    /// <summary>
    /// Restore messages from a JSON string (previously saved with SerializeMessages).
    /// Content is stored as JsonNode which serializes correctly for API calls.
    /// </summary>
    public void RestoreFromJson(string json)
    {
        _messages.Clear();
        try
        {
            var nodes = JsonNode.Parse(json)?.AsArray();
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                if (node == null) continue;
                var role = node["role"]?.GetValue<string>() ?? "";
                var contentNode = node["content"];
                var hasToolUse = node["hasToolUse"]?.GetValue<bool>() ?? false;
                var hasToolResult = node["hasToolResult"]?.GetValue<bool>() ?? false;

                object content;
                if (contentNode is JsonArray)
                    content = contentNode.DeepClone();
                else
                    content = contentNode?.GetValue<string>() ?? "";

                _messages.Add(new ChatMessage
                {
                    Role = role,
                    Content = content,
                    HasToolUse = hasToolUse,
                    HasToolResult = hasToolResult
                });
            }
        }
        catch
        {
            _messages.Clear();
        }
    }

    /// <summary>
    /// Build the messages array for the Claude API request.
    /// </summary>
    public List<object> BuildApiMessages()
    {
        var apiMessages = new List<object>();
        foreach (var msg in _messages)
        {
            apiMessages.Add(new { role = msg.Role, content = msg.Content });
        }
        return apiMessages;
    }

    /// <summary>
    /// Replace old tool result content with short placeholders to save tokens.
    /// Only the most recent tool_result message keeps full content.
    /// </summary>
    private void CompactOldToolResults()
    {
        // Find the index of the last tool_result message
        var lastToolResultIdx = -1;
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].HasToolResult) { lastToolResultIdx = i; break; }
        }

        if (lastToolResultIdx <= 0) return;

        // Compact all tool_result messages except the most recent one
        for (var i = 0; i < lastToolResultIdx; i++)
        {
            if (!_messages[i].HasToolResult) continue;
            if (_messages[i].Content is not List<object> blocks) continue;

            // Replace each block with a compact version
            for (var j = 0; j < blocks.Count; j++)
            {
                // Anonymous types are immutable — round-trip through JSON to replace content
                // This avoids reflection and keeps the compact placeholder serialization-safe
                var blockJson = System.Text.Json.JsonSerializer.Serialize(blocks[j]);
                var node = JsonNode.Parse(blockJson);
                if (node == null) continue;

                var toolUseId = node["tool_use_id"]?.GetValue<string>();
                if (toolUseId == null) continue;

                var isError = node["is_error"]?.GetValue<bool>() ?? false;

                if (isError)
                {
                    blocks[j] = new
                    {
                        type = "tool_result",
                        tool_use_id = toolUseId,
                        content = "[previous tool result]",
                        is_error = true
                    };
                }
                else
                {
                    blocks[j] = new
                    {
                        type = "tool_result",
                        tool_use_id = toolUseId,
                        content = "[previous tool result]"
                    };
                }
            }
        }
    }

    private void TrimIfNeeded()
    {
        // Never trim a tool_use without its matching tool_result (would break Claude API protocol)
        // Claude requires: assistant[tool_use] immediately followed by user[tool_result].
        // Orphaning either side causes HTTP 400 errors.
        while (_messages.Count > MaxMessages)
        {
            var first = _messages[0];

            // If removing an assistant message with tool_use, also remove the paired tool_result
            if (first.HasToolUse)
            {
                _messages.RemoveAt(0);
                if (_messages.Count > 0 && _messages[0].HasToolResult)
                    _messages.RemoveAt(0);
                continue;
            }

            // If a tool_result user message is first (its paired tool_use was already removed)
            if (first.HasToolResult)
            {
                _messages.RemoveAt(0);
                continue;
            }

            _messages.RemoveAt(0);
        }
    }
}
