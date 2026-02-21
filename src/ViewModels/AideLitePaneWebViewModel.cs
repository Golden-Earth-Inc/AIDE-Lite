// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Main ViewModel — orchestrates chat, tool execution, context, and WebView messaging
// ============================================================================
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models.DTOs;
using AideLite.Models.Messages;
using AideLite.ModelWriters;
using AideLite.Services;
using AideLite.Tools;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;

namespace AideLite.ViewModels;

[SupportedOSPlatform("windows")]
public class AideLitePaneWebViewModel : WebViewDockablePaneViewModel
{
    private readonly Func<IModel?> _getModel;
    private readonly Uri _webServerBaseUrl;
    private readonly ILogService _logService;
    private readonly ConfigurationService _configService;
    private readonly IHttpClientService _httpClientService;
    private readonly IDomainModelService _domainModelService;
    private readonly IMicroflowService _microflowService;
    private readonly IMicroflowActivitiesService _activitiesService;
    private readonly IMicroflowExpressionService _expressionService;
    private readonly IUntypedModelAccessService _untypedModelAccessService;
    private IWebView? _webView;

    // Lazy model accessor — always gets the current app even after project switch (via lambda, not snapshot)
    private IModel? Model => _getModel();
    // Tracks which model we last initialized services for, to detect project switches
    private IModel? _lastInitializedModel;

    // Services initialized lazily when model is available
    private ClaudeApiService? _claudeApi;
    private ConversationManager? _conversation;
    private bool _isChatProcessing;
    private PromptBuilder? _promptBuilder;
    private ToolRegistry? _toolRegistry;
    private ToolExecutor? _toolExecutor;
    private AppContextExtractor? _contextExtractor;
    private AppContextDto? _cachedContext;
    private string? _userRules;
    private ConversationHistoryService? _historyService;
    private string? _currentConversationId;
    private string? _currentConversationTitle;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AideLitePaneWebViewModel(
        Func<IModel?> getModel,
        Uri webServerBaseUrl,
        ILogService logService,
        ConfigurationService configService,
        IHttpClientService httpClientService,
        IDomainModelService domainModelService,
        IMicroflowService microflowService,
        IMicroflowActivitiesService activitiesService,
        IMicroflowExpressionService expressionService,
        IUntypedModelAccessService untypedModelAccessService)
    {
        _getModel = getModel;
        _webServerBaseUrl = webServerBaseUrl;
        _logService = logService;
        _configService = configService;
        _httpClientService = httpClientService;
        _domainModelService = domainModelService;
        _microflowService = microflowService;
        _activitiesService = activitiesService;
        _expressionService = expressionService;
        _untypedModelAccessService = untypedModelAccessService;

        Title = "AIDE Lite";
    }

    public override void InitWebView(IWebView webView)
    {
        // Unsubscribe from previous WebView to prevent duplicate handler accumulation on re-open
        if (_webView != null)
            _webView.MessageReceived -= OnMessageReceived;

        _webView = webView;
        // Cache-busting query param forces the WebView to load fresh JS/CSS during development
        webView.Address = new Uri(_webServerBaseUrl, $"aide-lite/chat?v={DateTime.UtcNow.Ticks}");
        webView.MessageReceived += OnMessageReceived;
        DiagLog("InitWebView: WebView initialized, MessageReceived handler attached");
    }

    /// <summary>
    /// Write diagnostic log to file AND send to WebView for visibility.
    /// </summary>
    private void DiagLog(string message)
    {
        var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AideLite");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "debug.log"), logLine + Environment.NewLine);
        }
        catch { /* ignore file errors */ }

        try { _logService.Info($"AIDE Lite: {message}"); } catch { }
    }

    /// <summary>
    /// Lazily initializes (or re-initializes after project switch) all services: Claude API,
    /// model readers, microflow generator, and the 14 tool registrations.
    /// </summary>
    private void EnsureServicesInitialized()
    {
        var model = Model;

        // Re-initialize when model reference changes (user switched projects) or on first call
        if (_claudeApi != null && model == _lastInitializedModel) return;

        _claudeApi = new ClaudeApiService(_httpClientService, _configService, _logService);
        _conversation ??= new ConversationManager();
        _promptBuilder = new PromptBuilder();
        _historyService ??= new ConversationHistoryService(_logService);
        _lastInitializedModel = model;

        if (model != null)
        {
            _contextExtractor = new AppContextExtractor(model, _domainModelService, _microflowService, _untypedModelAccessService, _logService);
            var domainModelReader = new DomainModelReader(model, _domainModelService);
            var microflowReader = new MicroflowReader(model, _microflowService, _untypedModelAccessService, _logService);
            var pageReader = new PageReader(model);
            var transactionManager = new TransactionManager(model, _logService);
            var microflowGenerator = new MicroflowGenerator(
                model, _microflowService, _activitiesService, _expressionService, _domainModelService, transactionManager, _logService);

            _toolRegistry = new ToolRegistry();
            _toolRegistry.Register(new GetModulesTool(_contextExtractor));
            _toolRegistry.Register(new GetEntitiesTool(_contextExtractor, domainModelReader));
            _toolRegistry.Register(new GetEntityDetailsTool(_contextExtractor, domainModelReader));
            _toolRegistry.Register(new GetMicroflowsTool(_contextExtractor, microflowReader));
            _toolRegistry.Register(new GetMicroflowDetailsTool(_contextExtractor, microflowReader));
            _toolRegistry.Register(new GetAssociationsTool(_contextExtractor, domainModelReader));
            _toolRegistry.Register(new GetEnumerationsTool(_contextExtractor, domainModelReader));
            _toolRegistry.Register(new GetPagesTool(_contextExtractor, pageReader));
            _toolRegistry.Register(new SearchModelTool(model, _contextExtractor));
            _toolRegistry.Register(new CreateMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new RenameMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new AddActivitiesToMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new ReplaceMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new EditMicroflowActivityTool(_contextExtractor, microflowGenerator));

            _toolExecutor = new ToolExecutor(_toolRegistry, _logService);

            // Load user rules from .aide-lite-rules.md in the project root
            LoadUserRules();
        }
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            var messageType = e.Message;
            var data = e.Data;

            DiagLog($"OnMessageReceived: type='{messageType}', hasData={data != null}");

            switch (messageType)
            {
                case "chat":
                    HandleChatMessage(data);
                    break;
                case "get_context":
                    HandleGetContext();
                    break;
                case "get_settings":
                    HandleGetSettings();
                    break;
                case "save_settings":
                    HandleSaveSettings(data);
                    break;
                case "new_chat":
                    HandleNewChat();
                    break;
                case "cancel":
                    HandleCancel();
                    break;
                case "get_history":
                    HandleGetHistory();
                    break;
                case "load_conversation":
                    HandleLoadConversation(data);
                    break;
                case "delete_conversation":
                    HandleDeleteConversation(data);
                    break;
                case "save_chat_state":
                    HandleSaveChatState(data);
                    break;
                case "MessageListenerRegistered":
                    DiagLog("OnMessageReceived: JS message listener registered - bridge ready");
                    break;
                default:
                    DiagLog($"OnMessageReceived: UNKNOWN type '{messageType}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagLog($"OnMessageReceived: EXCEPTION: {ex}");
            SendToWebView("error", new { message = "An internal error occurred. Check the AIDE Lite log for details.", code = "internal" });
        }
    }

    private async void HandleChatMessage(JsonObject? data)
    {
        if (_isChatProcessing)
        {
            DiagLog("HandleChatMessage: BLOCKED - already processing a chat request");
            return;
        }
        _isChatProcessing = true;
        try
        {
            DiagLog("[1/6] HandleChatMessage started");

            if (Model == null)
            {
                DiagLog("[1/6] No model - app not open");
                SendToWebView("error", new { message = "No app is open in Studio Pro.", code = "no_app" });
                return;
            }

            var messageText = data?["message"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(messageText))
            {
                DiagLog("[1/6] Empty message text");
                return;
            }

            DiagLog($"[2/6] Message: \"{messageText.Substring(0, Math.Min(messageText.Length, 50))}\"");

            EnsureServicesInitialized();

            if (_currentConversationId == null)
            {
                _currentConversationId = Guid.NewGuid().ToString("N");
                _currentConversationTitle = messageText.Length > 60
                    ? messageText[..60] + "..."
                    : messageText;
            }
            DiagLog("[3/6] Services initialized");

            var mode = data?["mode"]?.GetValue<string>() ?? "agent";
            var isAskMode = mode == "ask";

            var imageAttachments = ParseImageAttachments(data);
            if (imageAttachments.Count > 0)
            {
                DiagLog($"[2/6] Attaching {imageAttachments.Count} image(s) to message");
                _conversation!.AddUserMessageWithImages(messageText, imageAttachments);
            }
            else
            {
                _conversation!.AddUserMessage(messageText);
            }

            var systemPrompt = _promptBuilder!.BuildSystemPromptParts(_cachedContext, _userRules, isAskMode);
            var messages = _conversation.BuildApiMessages();

            var config = _configService.GetConfig();
            var cachingEnabled = config.PromptCachingEnabled;
            List<Dictionary<string, object>>? tools = isAskMode
                ? _toolRegistry?.BuildToolDefinitions(includeWriteTools: false, withCacheControl: cachingEnabled)
                : _toolRegistry?.BuildToolDefinitions(withCacheControl: cachingEnabled);
            DiagLog($"[4/6] Sending to Claude API (mode: {mode}, messages: {messages.Count}, tools: {tools?.Count ?? 0}, context: {(_cachedContext != null ? "loaded" : "none")}, caching: {cachingEnabled})");

            // Tool use loop — Claude may request multiple rounds of tool calls before producing a final answer.
            // Each round: send conversation -> receive response -> execute tool calls -> append results -> repeat.
            var maxToolRounds = config.MaxToolRounds;
            var totalInputTokens = 0;
            var totalOutputTokens = 0;
            var totalCacheCreation = 0;
            var totalCacheRead = 0;
            var lastRoundInputTokens = 0;
            var lastRoundOutputTokens = 0;
            var modelWasModified = false;

            for (var round = 0; round < maxToolRounds; round++)
            {
                DiagLog($"[5/6] API call round {round + 1} (tools: {tools?.Count ?? 0})...");

                var response = await _claudeApi!.SendStreamingRequestAsync(
                    systemPrompt,
                    messages,
                    tools,
                    onTextDelta: token => SendToWebView("chat_streaming", new { token, done = false }),
                    onToolStart: (toolName, toolId) => SendToWebView("tool_start", new { toolName }),
                    onRetryWait: (attempt, delaySec, maxRetries) => SendToWebView("retry_wait", new { attempt, delaySec, maxRetries }));

                totalInputTokens += response.InputTokens;
                totalOutputTokens += response.OutputTokens;
                totalCacheCreation += response.CacheCreationInputTokens;
                totalCacheRead += response.CacheReadInputTokens;
                lastRoundInputTokens = response.InputTokens;
                lastRoundOutputTokens = response.OutputTokens;
                DiagLog($"[5/6] Round {round + 1}: success={response.IsSuccess}, stop={response.StopReason}, text={response.FullText?.Length ?? 0}chars, tools={response.ToolCalls.Count}, tokens(in={response.InputTokens},out={response.OutputTokens},cacheWrite={response.CacheCreationInputTokens},cacheRead={response.CacheReadInputTokens},totalIn={totalInputTokens},totalOut={totalOutputTokens})");

                if (!response.IsSuccess)
                {
                    DiagLog($"[5/6] API FAILED: {response.ErrorCode} - {response.ErrorMessage}");
                    SendToWebView("error", new { message = response.ErrorMessage, code = response.ErrorCode });
                    return;
                }

                // If no tool calls, record as simple assistant message and finish
                if (!response.HasToolCalls)
                {
                    if (!string.IsNullOrEmpty(response.FullText))
                        _conversation.AddAssistantMessage(response.FullText);
                    SendToWebView("chat_streaming", new { token = "", done = true });
                    SendToWebView("token_usage", new { inputTokens = totalInputTokens, outputTokens = totalOutputTokens, cacheCreationTokens = totalCacheCreation, cacheReadTokens = totalCacheRead, contextUsedTokens = lastRoundInputTokens + lastRoundOutputTokens, contextLimitTokens = GetContextLimit(config.SelectedModel) });
                    if (modelWasModified) SendToWebView("model_changed", new { message = "Model was modified. Click ↻ to refresh context for future requests." });
                    return;
                }

                // Claude API protocol: all content blocks (text + tool_use) from one turn go in ONE assistant message
                _conversation.AddAssistantTurn(response.FullText, response.ToolCalls);

                // Execute all tool calls and collect results
                var toolResults = new List<(string ToolUseId, string Content, bool IsError)>();
                foreach (var toolCall in response.ToolCalls)
                {
                    var toolResult = _toolExecutor!.Execute(toolCall.Name, toolCall.InputJson);
                    SendToWebView("tool_result", new
                    {
                        toolName = toolCall.Name,
                        summary = toolResult.Summary
                    });

                    toolResults.Add((toolCall.Id, toolResult.Content, !toolResult.Success));

                    // If tool created or modified a microflow, notify the UI and flag for context refresh
                    if (toolCall.Name == "create_microflow" && toolResult.Success)
                    {
                        modelWasModified = true;
                        SendToWebView("microflow_created", new
                        {
                            name = toolResult.Summary.Replace("Created microflow ", ""),
                            message = toolResult.Content
                        });
                    }
                    else if ((toolCall.Name == "rename_microflow" || toolCall.Name == "add_activities_to_microflow" || toolCall.Name == "replace_microflow" || toolCall.Name == "edit_microflow_activity") && toolResult.Success)
                    {
                        modelWasModified = true;
                        SendToWebView("microflow_modified", new
                        {
                            toolName = toolCall.Name,
                            summary = toolResult.Summary,
                            message = toolResult.Content
                        });
                    }
                }

                // Claude API protocol: all tool results from one round go in ONE user message
                _conversation.AddToolResults(toolResults);

                // Continue the loop - send updated conversation back to Claude
                messages = _conversation.BuildApiMessages();
            }

            DiagLog($"[6/6] Max tool rounds ({maxToolRounds}) reached. Total tokens: in={totalInputTokens}, out={totalOutputTokens}, cacheWrite={totalCacheCreation}, cacheRead={totalCacheRead}");
            SendToWebView("chat_streaming", new { token = "\n\n*[Reached maximum tool rounds (" + maxToolRounds + "). Break complex tasks into smaller steps.]*", done = false });
            SendToWebView("chat_streaming", new { token = "", done = true });
            SendToWebView("token_usage", new { inputTokens = totalInputTokens, outputTokens = totalOutputTokens, cacheCreationTokens = totalCacheCreation, cacheReadTokens = totalCacheRead, contextUsedTokens = lastRoundInputTokens + lastRoundOutputTokens, contextLimitTokens = GetContextLimit(config.SelectedModel) });
            if (modelWasModified) SendToWebView("model_changed", new { message = "Model was modified. Click ↻ to refresh context for future requests." });
        }
        catch (Exception ex)
        {
            DiagLog($"[ERROR] Chat exception: {ex}");
            SendToWebView("error", new { message = "An error occurred while processing your request. Check the AIDE Lite log for details.", code = "internal" });
            SendToWebView("chat_streaming", new { token = "", done = true });
        }
        finally
        {
            _isChatProcessing = false;
        }
    }

    private void HandleGetContext()
    {
        if (Model == null)
        {
            SendToWebView("error", new { message = "No app is open in Studio Pro.", code = "no_app" });
            return;
        }

        EnsureServicesInitialized();

        try
        {
            var config = _configService.GetConfig();
            _cachedContext = _contextExtractor!.ExtractDetailedAppContext(config.ContextDepth);
            SendToWebView("context_loaded", new
            {
                summary = $"{_cachedContext.Modules.Count} modules, " +
                    $"{_cachedContext.Modules.Sum(m => m.Entities.Count)} entities, " +
                    $"{_cachedContext.Modules.Sum(m => m.Microflows.Count)} microflows"
            });
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to load context: {ex.Message}");
            SendToWebView("error", new { message = "Failed to load context. Check the AIDE Lite log for details.", code = "context_error" });
        }
    }

    private void HandleGetSettings()
    {
        var config = _configService.GetConfig();
        SendToWebView("load_settings", new
        {
            hasKey = _configService.HasApiKey(),
            selectedModel = config.SelectedModel,
            contextDepth = config.ContextDepth,
            maxTokens = config.MaxTokens,
            theme = config.Theme,
            retryMaxAttempts = config.RetryMaxAttempts,
            retryDelaySeconds = config.RetryDelaySeconds,
            maxToolRounds = config.MaxToolRounds,
            promptCachingEnabled = config.PromptCachingEnabled,
            autoRefreshContext = config.AutoRefreshContext,
            autoLoadLastConversation = config.AutoLoadLastConversation
        });
    }

    private void HandleSaveSettings(JsonObject? data)
    {
        var apiKey = data?["apiKey"]?.GetValue<string>();
        var model = data?["selectedModel"]?.GetValue<string>();
        var depth = data?["contextDepth"]?.GetValue<string>();
        var theme = data?["theme"]?.GetValue<string>();
        int? tokens = null;
        if (data?["maxTokens"] != null)
            tokens = data["maxTokens"]!.GetValue<int>();
        int? retryMaxAttempts = null;
        if (data?["retryMaxAttempts"] != null)
            retryMaxAttempts = data["retryMaxAttempts"]!.GetValue<int>();
        int? retryDelaySeconds = null;
        if (data?["retryDelaySeconds"] != null)
            retryDelaySeconds = data["retryDelaySeconds"]!.GetValue<int>();
        int? maxToolRounds = null;
        if (data?["maxToolRounds"] != null)
            maxToolRounds = data["maxToolRounds"]!.GetValue<int>();
        bool? promptCachingEnabled = null;
        if (data?["promptCachingEnabled"] != null)
            promptCachingEnabled = data["promptCachingEnabled"]!.GetValue<bool>();
        bool? autoRefreshContext = null;
        if (data?["autoRefreshContext"] != null)
            autoRefreshContext = data["autoRefreshContext"]!.GetValue<bool>();
        bool? autoLoadLastConversation = null;
        if (data?["autoLoadLastConversation"] != null)
            autoLoadLastConversation = data["autoLoadLastConversation"]!.GetValue<bool>();

        _configService.SaveConfig(apiKey, model, depth, tokens, theme, retryMaxAttempts, retryDelaySeconds, maxToolRounds, promptCachingEnabled, autoRefreshContext, autoLoadLastConversation);
        SendToWebView("settings_saved", new { success = true });
    }

    private void HandleNewChat()
    {
        _claudeApi?.Cancel();
        _isChatProcessing = false;
        _conversation?.Clear();
        _currentConversationId = null;
        _currentConversationTitle = null;
        _logService.Info("AIDE Lite: Chat cleared");
    }

    private void HandleCancel()
    {
        _claudeApi?.Cancel();
        _logService.Info("AIDE Lite: Request cancelled");
    }

    private void HandleGetHistory()
    {
        EnsureServicesInitialized();
        var list = _historyService!.GetConversationList();
        SendToWebView("history_list", new { conversations = list });
    }

    private void HandleLoadConversation(JsonObject? data)
    {
        var id = data?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return;

        EnsureServicesInitialized();
        var conversation = _historyService!.LoadConversation(id);
        if (conversation == null)
        {
            SendToWebView("error", new { message = "Conversation not found.", code = "history_error" });
            return;
        }

        _conversation!.RestoreFromJson(conversation.ApiMessagesJson);
        _currentConversationId = conversation.Id;
        _currentConversationTitle = conversation.Title;

        SendToWebView("conversation_loaded", new
        {
            id = conversation.Id,
            title = conversation.Title,
            displayHistory = conversation.DisplayHistory
        });
    }

    private void HandleDeleteConversation(JsonObject? data)
    {
        var id = data?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return;

        EnsureServicesInitialized();
        _historyService!.DeleteConversation(id);

        if (_currentConversationId == id)
        {
            _currentConversationId = null;
            _currentConversationTitle = null;
        }

        HandleGetHistory();
    }

    private void HandleSaveChatState(JsonObject? data)
    {
        if (_currentConversationId == null || _conversation == null || _historyService == null)
            return;

        try
        {
            var displayHistoryNode = data?["displayHistory"]?.AsArray();
            var displayHistory = new List<DisplayHistoryEntry>();

            if (displayHistoryNode != null)
            {
                foreach (var entry in displayHistoryNode)
                {
                    if (entry == null) continue;
                    displayHistory.Add(new DisplayHistoryEntry
                    {
                        Type = entry["type"]?.GetValue<string>() ?? "",
                        Content = entry["content"]?.GetValue<string>() ?? "",
                        ToolName = entry["toolName"]?.GetValue<string>()
                    });
                }
            }

            var isNew = !File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AideLite", "history", $"{_currentConversationId}.json"));

            _historyService.SaveConversation(new SavedConversation
            {
                Id = _currentConversationId,
                Title = _currentConversationTitle ?? "Untitled",
                CreatedAt = isNew ? DateTime.UtcNow : DateTime.UtcNow,
                DisplayHistory = displayHistory,
                ApiMessagesJson = _conversation.SerializeMessages()
            });
        }
        catch (Exception ex)
        {
            DiagLog($"HandleSaveChatState error: {ex.Message}");
        }
    }

    private List<ImageAttachment> ParseImageAttachments(JsonObject? data)
    {
        var result = new List<ImageAttachment>();
        var imagesNode = data?["images"];
        if (imagesNode is not JsonArray imagesArray) return result;

        foreach (var item in imagesArray)
        {
            var base64 = item?["base64"]?.GetValue<string>();
            var mediaType = item?["mediaType"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(base64) && !string.IsNullOrEmpty(mediaType))
                result.Add(new ImageAttachment(base64, mediaType));
        }
        return result;
    }

    /// <summary>
    /// Serializes data to JSON and posts it to the WebView. The JS side receives this via
    /// its registered message listener (which must send "MessageListenerRegistered" first).
    /// </summary>
    internal void SendToWebView(string type, object data)
    {
        try
        {
            var serializedData = JsonSerializer.Serialize(data, JsonOptions);
            if (_webView == null)
            {
                DiagLog($"SendToWebView FAILED: _webView is null! type={type}");
                return;
            }
            _webView.PostMessage(type, JsonNode.Parse(serializedData)!);
            DiagLog($"SendToWebView OK: type={type}, len={serializedData.Length}");
        }
        catch (Exception ex)
        {
            DiagLog($"SendToWebView EXCEPTION: type={type}, error={ex}");
        }
    }

    /// <summary>
    /// Load user rules from .aide-lite-rules.md in the Mendix project root.
    /// </summary>
    private void LoadUserRules()
    {
        try
        {
            // Navigate from extension DLL location up to the Mendix project root (3 levels up)
            var assemblyDir = Path.GetDirectoryName(GetType().Assembly.Location)!;
            var appRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", ".."));
            var rulesPath = Path.Combine(appRoot, ".aide-lite-rules.md");
            _userRules = File.Exists(rulesPath) ? File.ReadAllText(rulesPath) : null;
        }
        catch
        {
            _userRules = null;
        }
    }

    private static int GetContextLimit(string model) => model switch
    {
        "claude-opus-4-6" => 200_000,
        "claude-haiku-4-5-20251001" => 200_000,
        _ => 200_000
    };

    /// <summary>
    /// Clean up resources when the pane is closed.
    /// </summary>
    internal void Cleanup()
    {
        _claudeApi?.Cancel();
        _conversation?.Clear();
        _cachedContext = null;
        if (_webView != null)
        {
            _webView.MessageReceived -= OnMessageReceived;
            _webView = null;
        }
    }
}
