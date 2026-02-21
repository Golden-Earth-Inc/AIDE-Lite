// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Configuration persistence — DPAPI-encrypted API key and app settings
// ============================================================================
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AideLite.Models;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Services;

[SupportedOSPlatform("windows")]
public class ConfigurationService
{
    private readonly ILogService _logService;
    private readonly string _configFilePath;
    private AideLiteConfig _cachedConfig;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ConfigurationService(ILogService logService, IExtensionFileService? extensionFileService)
    {
        _logService = logService;

        // APPDATA provides per-user persistence that survives project switches and reinstalls
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AideLite");
        Directory.CreateDirectory(appDataDir);
        _configFilePath = Path.Combine(appDataDir, "config.json");

        _cachedConfig = LoadFromDisk();
    }

    public AideLiteConfig GetConfig()
    {
        _cachedConfig = LoadFromDisk();
        return _cachedConfig;
    }

    public string? GetApiKey()
    {
        var config = GetConfig();
        if (!string.IsNullOrEmpty(config.EncryptedApiKey))
        {
            try
            {
                return DecryptApiKey(config.EncryptedApiKey);
            }
            catch (Exception ex)
            {
                _logService.Error($"AIDE Lite: Failed to decrypt API key: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    public bool HasApiKey() => !string.IsNullOrEmpty(GetConfig().EncryptedApiKey);

    private static readonly HashSet<string> AllowedModels = new(StringComparer.Ordinal)
    {
        "claude-sonnet-4-5-20250929",
        "claude-sonnet-4-6",
        "claude-opus-4-6",
        "claude-haiku-4-5-20251001"
    };

    private static readonly HashSet<string> AllowedContextDepths = new(StringComparer.Ordinal)
    {
        "full", "summary", "none"
    };

    private const int MaxTokensCeiling = 64000;

    public void SaveConfig(string? apiKey, string? selectedModel, string? contextDepth, int? maxTokens, string? theme = null, int? retryMaxAttempts = null, int? retryDelaySeconds = null, int? maxToolRounds = null, bool? promptCachingEnabled = null)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _cachedConfig.EncryptedApiKey = EncryptApiKey(apiKey);
        }

        if (!string.IsNullOrEmpty(selectedModel) && AllowedModels.Contains(selectedModel))
            _cachedConfig.SelectedModel = selectedModel;

        if (!string.IsNullOrEmpty(contextDepth) && AllowedContextDepths.Contains(contextDepth))
            _cachedConfig.ContextDepth = contextDepth;

        if (maxTokens.HasValue && maxTokens.Value >= 256)
            _cachedConfig.MaxTokens = Math.Min(maxTokens.Value, MaxTokensCeiling);

        if (theme is "light" or "dark")
            _cachedConfig.Theme = theme;

        if (retryMaxAttempts.HasValue && retryMaxAttempts.Value >= 0)
            _cachedConfig.RetryMaxAttempts = Math.Min(retryMaxAttempts.Value, 100);

        if (retryDelaySeconds.HasValue && retryDelaySeconds.Value >= 1)
            _cachedConfig.RetryDelaySeconds = Math.Min(retryDelaySeconds.Value, 600);

        if (maxToolRounds.HasValue && maxToolRounds.Value >= 1)
            _cachedConfig.MaxToolRounds = Math.Min(maxToolRounds.Value, 50);

        if (promptCachingEnabled.HasValue)
            _cachedConfig.PromptCachingEnabled = promptCachingEnabled.Value;

        SaveToDisk(_cachedConfig);
        _logService.Info("AIDE Lite: Configuration saved");
    }

    private AideLiteConfig LoadFromDisk()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                return JsonSerializer.Deserialize<AideLiteConfig>(json) ?? new AideLiteConfig();
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to load config: {ex.Message}");
        }
        return new AideLiteConfig();
    }

    private void SaveToDisk(AideLiteConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to save config: {ex.Message}");
        }
    }

    // DPAPI encryption with app-specific entropy for secure local storage
    // Entropy ensures keys encrypted by other DPAPI-using apps cannot be cross-decrypted
    private static readonly byte[] DpapiEntropy = "AideLite-MendixExtension-2026"u8.ToArray();

    private static string EncryptApiKey(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string DecryptApiKey(string encrypted)
    {
        var bytes = Convert.FromBase64String(encrypted);
        // Backward-compatible decryption: try entropy first, then without (pre-entropy versions)
        try
        {
            var decrypted = ProtectedData.Unprotect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            // Key was encrypted without entropy (from older version) - try without
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
