// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Claude API integration — streaming SSE requests with tool-use support
// ============================================================================
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.Models;
using Mendix.StudioPro.ExtensionsAPI;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Services;

[SupportedOSPlatform("windows")]
public class ClaudeApiService
{
    private readonly IHttpClientService _httpClientService;
    private readonly ConfigurationService _configService;
    private readonly ILogService _logService;
    private CancellationTokenSource? _currentCts;

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    private const int MaxStreamTextBytes = 2 * 1024 * 1024;
    private const int MaxToolInputJsonBytes = 512 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ClaudeApiService(
        IHttpClientService httpClientService,
        ConfigurationService configService,
        ILogService logService)
    {
        _httpClientService = httpClientService;
        _configService = configService;
        _logService = logService;
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Send a streaming request to the Claude API with tool definitions.
    /// Retries automatically on rate-limit (429), overloaded (529), and transient 5xx errors.
    /// Uses prompt caching: system prompt blocks and tools are marked with cache_control
    /// so repeated content doesn't count toward input token rate limits.
    /// </summary>
    public async Task<ApiResponse> SendStreamingRequestAsync(
        SystemPromptParts systemPrompt,
        List<object> messages,
        List<Dictionary<string, object>>? tools,
        Action<string> onTextDelta,
        Action<string, string> onToolStart,
        Action<int, int, int>? onRetryWait = null,
        CancellationToken externalToken = default)
    {
        var apiKey = _configService.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return ApiResponse.Error("No API key configured. Please set your Claude API key in Settings.");

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var ct = _currentCts.Token;

        try
        {
            var config = _configService.GetConfig();
            var requestJson = BuildRequestJson(systemPrompt, messages, tools, config);
            _logService.Info($"AIDE Lite: [API] Model: {config.SelectedModel}, MaxTokens: {config.MaxTokens}, body: {requestJson.Length} chars");
            _logService.Info($"AIDE Lite: [API] System prompt sizes: instructions={systemPrompt.StaticInstructions.Length}chars, context={systemPrompt.AppContext.Length}chars");

            return await SendWithRetries(apiKey, requestJson, config, onTextDelta, onToolStart, onRetryWait, ct);
        }
        catch (OperationCanceledException)
        {
            return ApiResponse.Error("Request cancelled.", "cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logService.Error($"AIDE Lite: Network error: {ex.Message}");
            return ApiResponse.Error("Network error. Please check your connection.", "network");
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Unexpected error: {ex.Message}");
            return ApiResponse.Error("An unexpected error occurred. Check the AIDE Lite log for details.");
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    public void Cancel() => _currentCts?.Cancel();

    // ── Request building ────────────────────────────────────────────────

    private static readonly object CacheBreakpoint = new { type = "ephemeral" };

    private static string BuildRequestJson(
        SystemPromptParts systemPrompt, List<object> messages,
        List<Dictionary<string, object>>? tools, AideLiteConfig config)
    {
        // System prompt is sent as an array of content blocks, each with cache_control.
        // Anthropic caches matching prefixes for 5 minutes — subsequent calls in the
        // same conversation (especially tool-use rounds) get cache hits, which do NOT
        // count toward the input token rate limit.
        var systemBlocks = new List<object>
        {
            new { type = "text", text = systemPrompt.StaticInstructions, cache_control = CacheBreakpoint },
            new { type = "text", text = systemPrompt.AppContext, cache_control = CacheBreakpoint }
        };

        var body = new Dictionary<string, object>
        {
            ["model"] = config.SelectedModel,
            ["max_tokens"] = config.MaxTokens,
            ["stream"] = true,
            ["system"] = systemBlocks,
            ["messages"] = messages
        };

        if (tools is { Count: > 0 })
            body["tools"] = tools;

        return JsonSerializer.Serialize(body, JsonOptions);
    }

    // ── Retry loop ──────────────────────────────────────────────────────

    private static bool IsRetryableStatus(int code) =>
        code is 429 or 529 or 500 or 502 or 503;

    private async Task<ApiResponse> SendWithRetries(
        string apiKey, string requestJson, AideLiteConfig config,
        Action<string> onTextDelta, Action<string, string> onToolStart,
        Action<int, int, int>? onRetryWait, CancellationToken ct)
    {
        var maxRetries = config.RetryMaxAttempts;
        var retryDelay = config.RetryDelaySeconds;
        _logService.Info($"AIDE Lite: [API] Retry config: max={maxRetries}, delay={retryDelay}s");

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            _logService.Info($"AIDE Lite: [API] Attempt {attempt + 1}/{maxRetries + 1}...");

            using var httpClient = _httpClientService.CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            using var request = CreateHttpRequest(apiKey, requestJson);
            using var response = await httpClient.SendAsync(request, ct);
            var statusCode = (int)response.StatusCode;
            _logService.Info($"AIDE Lite: [API] HTTP {statusCode} {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var result = await ParseStream(response, onTextDelta, onToolStart, ct);
                _logService.Info($"AIDE Lite: [API] Cache stats: creation={result.CacheCreationInputTokens}, read={result.CacheReadInputTokens}, input={result.InputTokens}");
                return result;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logService.Error($"AIDE Lite: [API] Error body: {errorBody}");

            if (statusCode == 401)
                return ApiResponse.Error("Invalid API key. Please check your settings.", "auth_error");

            if (!IsRetryableStatus(statusCode) || attempt >= maxRetries)
                return FinalErrorForStatus(statusCode, maxRetries);

            var delaySec = GetRetryDelay(response, retryDelay);
            _logService.Info($"AIDE Lite: [API] Retryable {statusCode}, waiting {delaySec}s...");
            onRetryWait?.Invoke(attempt + 1, delaySec, maxRetries);
            await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
        }

        return ApiResponse.Error("Rate limit exceeded after maximum retries.", "rate_limit");
    }

    private static HttpRequestMessage CreateHttpRequest(string apiKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        request.Headers.Add("anthropic-beta", "prompt-caching-2024-07-31");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static ApiResponse FinalErrorForStatus(int statusCode, int maxRetries)
    {
        if (statusCode is 429 or 529)
            return ApiResponse.Error(
                $"Rate limit exceeded after {maxRetries} retries. Try switching to claude-haiku-4-5-20251001 in Settings for higher rate limits, or increase retry settings.",
                "rate_limit");

        return ApiResponse.Error($"API error ({statusCode}). Check the AIDE Lite log for details.");
    }

    private static int GetRetryDelay(HttpResponseMessage response, int configuredDelay)
    {
        if (response.Headers.TryGetValues("retry-after", out var values))
        {
            var retryAfter = values.FirstOrDefault();
            if (int.TryParse(retryAfter, out var headerSeconds) && headerSeconds > 0)
                return Math.Min(headerSeconds, 300);
        }
        return configuredDelay;
    }

    // ── SSE stream parsing ──────────────────────────────────────────────
    //
    // Claude streams Server-Sent Events (SSE) with these event types:
    //   message_start / message_delta / message_stop   — message lifecycle
    //   content_block_start / _delta / _stop           — text & tool_use blocks
    //   ping                                           — keep-alive (ignored)
    //   error                                          — stream-level error
    //
    // Tool input JSON arrives as incremental fragments across multiple
    // input_json_delta events and is reassembled in StreamState.

    private async Task<ApiResponse> ParseStream(
        HttpResponseMessage response,
        Action<string> onTextDelta,
        Action<string, string> onToolStart,
        CancellationToken ct)
    {
        var state = new StreamState();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var earlyExit = ProcessSseEvent(line[6..], state, onTextDelta, onToolStart);
            if (earlyExit != null)
                return earlyExit;
        }

        state.Result.FullText = state.FullText.ToString();
        state.Result.IsSuccess = string.IsNullOrEmpty(state.Result.ErrorMessage);
        return state.Result;
    }

    private static ApiResponse? ProcessSseEvent(
        string data, StreamState s,
        Action<string> onTextDelta, Action<string, string> onToolStart)
    {
        try
        {
            var evt = JsonNode.Parse(data);
            if (evt == null) return null;

            return evt["type"]?.GetValue<string>() switch
            {
                "content_block_start" => OnBlockStart(evt, s, onToolStart),
                "content_block_delta" => OnBlockDelta(evt, s, onTextDelta),
                "content_block_stop"  => OnBlockStop(s),
                "message_start"       => OnMessageStart(evt, s),
                "message_delta"       => OnMessageDelta(evt, s),
                "error"               => OnError(evt, s),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Individual SSE event handlers ───────────────────────────────────

    private static ApiResponse? OnBlockStart(JsonNode evt, StreamState s, Action<string, string> onToolStart)
    {
        var block = evt["content_block"];
        s.CurrentBlockType = block?["type"]?.GetValue<string>() ?? "";

        if (s.CurrentBlockType != "tool_use")
            return null;

        s.CurrentToolId = block?["id"]?.GetValue<string>() ?? "";
        s.CurrentToolName = block?["name"]?.GetValue<string>() ?? "";
        s.ToolInputJson.Clear();
        onToolStart(s.CurrentToolName, s.CurrentToolId);
        return null;
    }

    private static ApiResponse? OnBlockDelta(JsonNode evt, StreamState s, Action<string> onTextDelta)
    {
        var delta = evt["delta"];
        var deltaType = delta?["type"]?.GetValue<string>();

        if (deltaType == "text_delta")
            return AccumulateText(delta, s, onTextDelta);

        if (deltaType == "input_json_delta")
            return AccumulateToolInput(delta, s);

        return null;
    }

    private static ApiResponse? AccumulateText(JsonNode? delta, StreamState s, Action<string> onTextDelta)
    {
        var text = delta?["text"]?.GetValue<string>() ?? "";
        if (s.FullText.Length + text.Length > MaxStreamTextBytes)
            return SizeLimitError("Response exceeded maximum size limit.", s);

        s.FullText.Append(text);
        onTextDelta(text);
        return null;
    }

    private static ApiResponse? AccumulateToolInput(JsonNode? delta, StreamState s)
    {
        var fragment = delta?["partial_json"]?.GetValue<string>() ?? "";
        if (s.ToolInputJson.Length + fragment.Length > MaxToolInputJsonBytes)
            return SizeLimitError("Tool input exceeded maximum size limit.", s);

        s.ToolInputJson.Append(fragment);
        return null;
    }

    private static ApiResponse? OnBlockStop(StreamState s)
    {
        if (s.CurrentBlockType == "tool_use" && !string.IsNullOrEmpty(s.CurrentToolId))
        {
            s.Result.ToolCalls.Add(new ToolCall
            {
                Id = s.CurrentToolId,
                Name = s.CurrentToolName,
                InputJson = s.ToolInputJson.ToString()
            });
            s.CurrentToolId = "";
            s.CurrentToolName = "";
            s.ToolInputJson.Clear();
        }
        s.CurrentBlockType = "";
        return null;
    }

    private static ApiResponse? OnMessageStart(JsonNode evt, StreamState s)
    {
        var usage = evt["message"]?["usage"];
        if (usage == null) return null;

        var inputTokens = usage["input_tokens"]?.GetValue<int>();
        if (inputTokens.HasValue)
            s.Result.InputTokens = inputTokens.Value;

        var cacheCreation = usage["cache_creation_input_tokens"]?.GetValue<int>();
        if (cacheCreation.HasValue)
            s.Result.CacheCreationInputTokens = cacheCreation.Value;

        var cacheRead = usage["cache_read_input_tokens"]?.GetValue<int>();
        if (cacheRead.HasValue)
            s.Result.CacheReadInputTokens = cacheRead.Value;

        return null;
    }

    private static ApiResponse? OnMessageDelta(JsonNode evt, StreamState s)
    {
        var stopReason = evt["delta"]?["stop_reason"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(stopReason))
            s.Result.StopReason = stopReason;

        var outputTokens = evt["usage"]?["output_tokens"]?.GetValue<int>();
        if (outputTokens.HasValue)
            s.Result.OutputTokens = outputTokens.Value;
        return null;
    }

    private static ApiResponse? OnError(JsonNode evt, StreamState s)
    {
        s.Result.ErrorMessage = evt["error"]?["message"]?.GetValue<string>() ?? "Unknown streaming error";
        s.Result.ErrorCode = "stream_error";
        return null;
    }

    private static ApiResponse SizeLimitError(string message, StreamState s) => new()
    {
        IsSuccess = false,
        ErrorMessage = message,
        ErrorCode = "stream_too_large",
        FullText = s.FullText.ToString()
    };

    // ── Stream parse state ──────────────────────────────────────────────

    private class StreamState
    {
        public readonly ApiResponse Result = new();
        public readonly StringBuilder FullText = new();
        public readonly StringBuilder ToolInputJson = new();
        public string CurrentBlockType = "";
        public string CurrentToolId = "";
        public string CurrentToolName = "";
    }
}

// ── Data models ─────────────────────────────────────────────────────────

public class ApiResponse
{
    public bool IsSuccess { get; set; }
    public string FullText { get; set; } = string.Empty;
    public string StopReason { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public List<ToolCall> ToolCalls { get; set; } = new();
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheCreationInputTokens { get; set; }
    public int CacheReadInputTokens { get; set; }

    public bool HasToolCalls => ToolCalls.Count > 0;

    public static ApiResponse Error(string message, string? code = null) => new()
    {
        IsSuccess = false,
        ErrorMessage = message,
        ErrorCode = code ?? "error"
    };
}

public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
}
