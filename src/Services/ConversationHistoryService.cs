// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Conversation history persistence — save/load chat sessions to disk
// ============================================================================
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Services;

public partial class ConversationHistoryService
{
    private readonly ILogService _logService;
    private readonly string _historyDir;
    private const int MaxSavedConversations = 50;

    [GeneratedRegex("^[a-f0-9]{32}$")]
    private static partial Regex SafeIdPattern();

    private string? SafeFilePath(string id)
    {
        if (!SafeIdPattern().IsMatch(id)) return null;
        var path = Path.GetFullPath(Path.Combine(_historyDir, $"{id}.json"));
        return path.StartsWith(_historyDir, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ConversationHistoryService(ILogService logService)
    {
        _logService = logService;
        _historyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AideLite", "history");
        Directory.CreateDirectory(_historyDir);
    }

    public void SaveConversation(SavedConversation conversation)
    {
        try
        {
            var filePath = SafeFilePath(conversation.Id);
            if (filePath == null) return;
            conversation.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(conversation, JsonOptions);
            File.WriteAllText(filePath, json);
            PruneOldConversations();
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to save conversation: {ex.Message}");
        }
    }

    public List<ConversationSummary> GetConversationList()
    {
        var summaries = new List<ConversationSummary>();
        try
        {
            foreach (var file in Directory.GetFiles(_historyDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var conv = JsonSerializer.Deserialize<SavedConversation>(json, JsonOptions);
                    if (conv != null)
                    {
                        summaries.Add(new ConversationSummary
                        {
                            Id = conv.Id,
                            Title = conv.Title,
                            CreatedAt = conv.CreatedAt,
                            UpdatedAt = conv.UpdatedAt,
                            MessageCount = conv.DisplayHistory?.Count ?? 0
                        });
                    }
                }
                catch { /* skip corrupt files */ }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to list conversations: {ex.Message}");
        }

        return summaries.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public SavedConversation? LoadConversation(string id)
    {
        try
        {
            var filePath = SafeFilePath(id);
            if (filePath == null || !File.Exists(filePath)) return null;
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SavedConversation>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to load conversation {id}: {ex.Message}");
            return null;
        }
    }

    public void DeleteConversation(string id)
    {
        try
        {
            var filePath = SafeFilePath(id);
            if (filePath == null) return;
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to delete conversation {id}: {ex.Message}");
        }
    }

    private void PruneOldConversations()
    {
        try
        {
            var files = Directory.GetFiles(_historyDir, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (var i = MaxSavedConversations; i < files.Count; i++)
                files[i].Delete();
        }
        catch { /* best-effort cleanup */ }
    }
}

public class SavedConversation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("displayHistory")]
    public List<DisplayHistoryEntry> DisplayHistory { get; set; } = new();

    [JsonPropertyName("apiMessagesJson")]
    public string ApiMessagesJson { get; set; } = "[]";
}

public class DisplayHistoryEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }
}

public class ConversationSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }
}
