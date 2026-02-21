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
using Mendix.StudioPro.ExtensionsAPI;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Services;

[SupportedOSPlatform("windows")]
public class ClaudeApiService
{
    private readonly IHttpClientService _httpClientService;
    private readonly ConfigurationService _configService;
    private readonly ILogService _logService;
    // Per-request CTS — linked to external token so callers and Cancel() both work
    private CancellationTokenSource? _currentCts;

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    private const int MaxRetries = 3;
    private static readonly int[] RetryDelaysSeconds = { 15, 30, 60 };

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

    /// <summary>
    /// Send a streaming request to the Claude API with tool definitions.
    /// Invokes callbacks for text deltas, tool use blocks, and completion.
    /// Automatically retries on 429 (rate limit) and 529 (overloaded) with exponential backoff.
    /// </summary>
    public async Task<ApiResponse> SendStreamingRequestAsync(
        string systemPrompt,
        List<object> messages,
        List<Dictionary<string, object>>? tools,
        Action<string> onTextDelta,
        Action<string, string> onToolStart,
        Action<int, int>? onRetryWait = null,
        CancellationToken externalToken = default)
    {
        _logService.Info("AIDE Lite: [API] Getting API key...");
        var apiKey = _configService.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            _logService.Error("AIDE Lite: [API] No API key found!");
            return ApiResponse.Error("No API key configured. Please set your Claude API key in Settings.");
        }
        _logService.Info("AIDE Lite: [API] API key found");

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var ct = _currentCts.Token;

        try
        {
            var config = _configService.GetConfig();
            _logService.Info($"AIDE Lite: [API] Model: {config.SelectedModel}, MaxTokens: {config.MaxTokens}");

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = config.SelectedModel,
                ["max_tokens"] = config.MaxTokens,
                ["stream"] = true,
                ["system"] = systemPrompt,
                ["messages"] = messages
            };

            if (tools != null && tools.Count > 0)
            {
                requestBody["tools"] = tools;
            }

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            _logService.Info($"AIDE Lite: [API] Request body size: {json.Length} chars");

            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                using var httpClient = _httpClientService.CreateHttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", ApiVersion);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                _logService.Info($"AIDE Lite: [API] Sending HTTP request (attempt {attempt + 1}/{MaxRetries + 1})...");
                var response = await httpClient.SendAsync(request, ct);
                _logService.Info($"AIDE Lite: [API] HTTP response: {(int)response.StatusCode} {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logService.Error($"AIDE Lite: [API] Error body: {errorBody}");

                    var statusCode = (int)response.StatusCode;

                    if (statusCode == 401)
                        return ApiResponse.Error("Invalid API key. Please check your settings.", "auth_error");

                    if (statusCode is 429 or 529 && attempt < MaxRetries)
                    {
                        var delaySec = GetRetryDelay(response, attempt);
                        _logService.Info($"AIDE Lite: [API] Retryable error {statusCode}, waiting {delaySec}s before retry {attempt + 2}...");
                        onRetryWait?.Invoke(attempt + 1, delaySec);
                        await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
                        continue;
                    }

                    if (statusCode == 429)
                        return ApiResponse.Error("Rate limit exceeded after multiple retries. Try switching to claude-haiku-4-5-20251001 in Settings for higher rate limits, or wait a minute.", "rate_limit");
                    if (statusCode == 529)
                        return ApiResponse.Error("Claude API is overloaded after multiple retries. Please try again in a few minutes.", "overloaded");

                    return ApiResponse.Error($"API error ({statusCode}): {errorBody}");
                }

                _logService.Info("AIDE Lite: [API] Parsing streaming response...");
                var result = await ParseStreamingResponse(response, onTextDelta, onToolStart, ct);
                _logService.Info($"AIDE Lite: [API] Stream complete: text={result.FullText?.Length ?? 0} chars, tools={result.ToolCalls.Count}, stop={result.StopReason}, error={result.ErrorMessage ?? "none"}");
                return result;
            }

            return ApiResponse.Error("Rate limit exceeded after maximum retries.", "rate_limit");
        }
        catch (OperationCanceledException)
        {
            return ApiResponse.Error("Request cancelled.", "cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logService.Error($"AIDE Lite: Network error: {ex.Message}");
            return ApiResponse.Error($"Network error: {ex.Message}", "network");
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Unexpected error: {ex.Message}");
            return ApiResponse.Error($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    /// <summary>
    /// Determines how long to wait before retrying, using the Retry-After header if available,
    /// otherwise falling back to a predefined exponential backoff schedule.
    /// </summary>
    private static int GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.TryGetValues("retry-after", out var values))
        {
            var retryAfter = values.FirstOrDefault();
            if (int.TryParse(retryAfter, out var headerSeconds) && headerSeconds > 0)
                return Math.Min(headerSeconds, 120);
        }
        return attempt < RetryDelaysSeconds.Length ? RetryDelaysSeconds[attempt] : 60;
    }

    public void Cancel()
    {
        _currentCts?.Cancel();
    }

    private async Task<ApiResponse> ParseStreamingResponse(
        HttpResponseMessage response,
        Action<string> onTextDelta,
        Action<string, string> onToolStart,
        CancellationToken ct)
    {
        var result = new ApiResponse();
        // SSE event types: content_block_start/delta/stop, message_start/delta/stop, ping, error
        // Tool input arrives as incremental JSON fragments across multiple input_json_delta events
        var currentToolUseId = "";
        var currentToolName = "";
        var toolInputJson = new StringBuilder();
        var fullText = new StringBuilder();
        var currentContentBlockType = "";

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..]; // Strip "data: " prefix — Claude SSE lines are always "data: {json}"

            try
            {
                var evt = JsonNode.Parse(data);
                if (evt == null) continue;

                var eventType = evt["type"]?.GetValue<string>();

                switch (eventType)
                {
                    case "content_block_start":
                        var contentBlock = evt["content_block"];
                        var blockType = contentBlock?["type"]?.GetValue<string>();
                        currentContentBlockType = blockType ?? "";

                        if (blockType == "tool_use")
                        {
                            currentToolUseId = contentBlock?["id"]?.GetValue<string>() ?? "";
                            currentToolName = contentBlock?["name"]?.GetValue<string>() ?? "";
                            toolInputJson.Clear();
                            onToolStart(currentToolName, currentToolUseId);
                        }
                        break;

                    case "content_block_delta":
                        var delta = evt["delta"];
                        var deltaType = delta?["type"]?.GetValue<string>();

                        if (deltaType == "text_delta")
                        {
                            var text = delta?["text"]?.GetValue<string>() ?? "";
                            fullText.Append(text);
                            onTextDelta(text);
                        }
                        else if (deltaType == "input_json_delta")
                        {
                            var partialJson = delta?["partial_json"]?.GetValue<string>() ?? "";
                            toolInputJson.Append(partialJson);
                        }
                        break;

                    case "content_block_stop":
                        // Finalize tool_use block — reassemble the streamed JSON fragments
                        if (currentContentBlockType == "tool_use" && !string.IsNullOrEmpty(currentToolUseId))
                        {
                            result.ToolCalls.Add(new ToolCall
                            {
                                Id = currentToolUseId,
                                Name = currentToolName,
                                InputJson = toolInputJson.ToString()
                            });
                            currentToolUseId = "";
                            currentToolName = "";
                            toolInputJson.Clear();
                        }
                        currentContentBlockType = "";
                        break;

                    case "message_delta":
                        var stopReason = evt["delta"]?["stop_reason"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(stopReason))
                        {
                            result.StopReason = stopReason;
                        }
                        // message_delta contains output token count
                        var deltaOutputTokens = evt["usage"]?["output_tokens"]?.GetValue<int>();
                        if (deltaOutputTokens.HasValue)
                            result.OutputTokens = deltaOutputTokens.Value;
                        break;

                    case "message_stop":
                        break;

                    case "message_start":
                        // Capture input token count for usage tracking
                        var usage = evt["message"]?["usage"];
                        var inputTokens = usage?["input_tokens"]?.GetValue<int>();
                        if (inputTokens.HasValue)
                            result.InputTokens = inputTokens.Value;
                        break;

                    case "ping":
                        // Keep-alive event — safe to ignore
                        break;

                    case "error":
                        var errorMsg = evt["error"]?["message"]?.GetValue<string>() ?? "Unknown streaming error";
                        result.ErrorMessage = errorMsg;
                        result.ErrorCode = "stream_error";
                        break;
                }
            }
            catch (JsonException)
            {
                // Skip malformed SSE data lines
            }
        }

        result.FullText = fullText.ToString();
        result.IsSuccess = string.IsNullOrEmpty(result.ErrorMessage);
        return result;
    }
}

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
