// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Configuration model — settings persisted to config.json
// ============================================================================

using System.Text.Json.Serialization;

namespace AideLite.Models;

public class AideLiteConfig
{
    // API key stored as DPAPI-encrypted base64 — no plaintext property exists by design
    [JsonPropertyName("encryptedApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EncryptedApiKey { get; set; }

    [JsonPropertyName("selectedModel")]
    public string SelectedModel { get; set; } = "claude-sonnet-4-5-20250929";

    [JsonPropertyName("contextDepth")]
    public string ContextDepth { get; set; } = "summary";

    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; } = 8192;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "light";

    [JsonPropertyName("retryMaxAttempts")]
    public int RetryMaxAttempts { get; set; } = 20;

    [JsonPropertyName("retryDelaySeconds")]
    public int RetryDelaySeconds { get; set; } = 60;

    [JsonPropertyName("maxToolRounds")]
    public int MaxToolRounds { get; set; } = 10;

    [JsonPropertyName("promptCachingEnabled")]
    public bool PromptCachingEnabled { get; set; } = true;

    [JsonPropertyName("hasAcceptedDataConsent")]
    public bool HasAcceptedDataConsent { get; set; } = false;
}
