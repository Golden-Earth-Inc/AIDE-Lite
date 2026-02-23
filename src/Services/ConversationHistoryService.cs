// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Conversation history persistence — save/load chat sessions to disk
// ============================================================================
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Services;

[SupportedOSPlatform("windows")]
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

    // DPAPI encryption for conversation history — same entropy as API key storage
    private static readonly byte[] DpapiEntropy = "AideLite-ConversationHistory-2026"u8.ToArray();

    private static byte[] EncryptData(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        return ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
    }

    private static string DecryptData(byte[] encrypted)
    {
        var decrypted = ProtectedData.Unprotect(encrypted, DpapiEntropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    public void SaveConversation(SavedConversation conversation)
    {
        try
        {
            var filePath = SafeFilePath(conversation.Id);
            if (filePath == null) return;
            conversation.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(conversation, JsonOptions);
            var encrypted = EncryptData(json);
            var tempPath = filePath + ".tmp";
            File.WriteAllBytes(tempPath, encrypted);
            File.Move(tempPath, filePath, overwrite: true);
            PruneOldConversations();
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to save conversation: {ex.Message}");
        }
    }

    /// <summary>
    /// Read a conversation file with backward-compatible decryption.
    /// Tries DPAPI decryption first; falls back to plaintext for pre-encryption files.
    /// </summary>
    private static string? ReadConversationFile(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        try
        {
            return DecryptData(bytes);
        }
        catch (CryptographicException)
        {
            // Pre-encryption plaintext file — read as UTF-8
            return Encoding.UTF8.GetString(bytes);
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
                    var json = ReadConversationFile(file);
                    if (json == null) continue;
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
            var json = ReadConversationFile(filePath);
            if (json == null) return null;
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

    public void DeleteAllConversations()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_historyDir, "*.json"))
            {
                try { File.Delete(file); }
                catch (Exception ex)
                {
                    _logService.Error($"AIDE Lite: Failed to delete {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to delete all conversations: {ex.Message}");
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
