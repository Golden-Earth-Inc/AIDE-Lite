// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Extracted chat logic — shared between pane and tab ViewModels
// ============================================================================
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;
using AideLite.Models.DTOs;
using AideLite.Models.Messages;
using AideLite.ModelWriters;
using AideLite.Tools;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;

namespace AideLite.Services;

[SupportedOSPlatform("windows")]
public class ChatController
{
    private readonly Func<IModel?> _getModel;
    private readonly ILogService _logService;
    private readonly ConfigurationService _configService;
    private readonly IHttpClientService _httpClientService;
    private readonly IDomainModelService _domainModelService;
    private readonly IMicroflowService _microflowService;
    private readonly IMicroflowActivitiesService _activitiesService;
    private readonly IMicroflowExpressionService _expressionService;
    private readonly IUntypedModelAccessService _untypedModelAccessService;
    private readonly IDockingWindowService _dockingService;

    private IWebView? _webView;
    private bool _webViewReady;
    private readonly List<DocumentReference> _pendingReferences = new();

    // Active document tracking
    private string? _activeDocumentName;
    private string? _activeDocumentType;
    private string? _activeDocumentQualifiedName;

    // Lazy model accessor
    private IModel? Model => _getModel();
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
    private DateTime _currentConversationCreatedAt;
    private DateTime _lastSettingsSave = DateTime.MinValue;

    // View state snapshot for toggle
    private ViewStateSnapshot? _pendingRestore;

    /// <summary>
    /// Fires when the JS side sends a "toggle_view" message.
    /// The ViewToggleCoordinator subscribes to this.
    /// </summary>
    public event Action? OnToggleRequested;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ChatController(
        Func<IModel?> getModel,
        ILogService logService,
        ConfigurationService configService,
        IHttpClientService httpClientService,
        IDomainModelService domainModelService,
        IMicroflowService microflowService,
        IMicroflowActivitiesService activitiesService,
        IMicroflowExpressionService expressionService,
        IUntypedModelAccessService untypedModelAccessService,
        IDockingWindowService dockingService)
    {
        _getModel = getModel;
        _logService = logService;
        _configService = configService;
        _httpClientService = httpClientService;
        _domainModelService = domainModelService;
        _microflowService = microflowService;
        _activitiesService = activitiesService;
        _expressionService = expressionService;
        _untypedModelAccessService = untypedModelAccessService;
        _dockingService = dockingService;
    }

    /// <summary>
    /// Attach a new WebView (from a pane or tab ViewModel).
    /// </summary>
    public void AttachWebView(IWebView webView)
    {
        if (_webView != null)
            _webView.MessageReceived -= OnMessageReceived;

        DocumentReferenceStore.OnDocumentReferenced -= OnDocumentReferenced;

        _webView = webView;
        _webViewReady = false;
        webView.MessageReceived += OnMessageReceived;

        // Drain any references that were enqueued before the view opened
        DocumentReferenceStore.DrainAll(r => _pendingReferences.Add(r));
        DocumentReferenceStore.OnDocumentReferenced += OnDocumentReferenced;

        DiagLog($"AttachWebView: WebView attached, drained {_pendingReferences.Count} pending ref(s)");
    }

    /// <summary>
    /// Detach the current WebView (before closing a pane/tab).
    /// Preserves all conversation state so a new view can reattach.
    /// </summary>
    public void DetachWebView()
    {
        _webViewReady = false;
        DocumentReferenceStore.OnDocumentReferenced -= OnDocumentReferenced;
        if (_webView != null)
        {
            _webView.MessageReceived -= OnMessageReceived;
            _webView = null;
        }
        DiagLog("DetachWebView: WebView detached, state preserved");
    }

    /// <summary>
    /// Capture display-level state for restoration after a view toggle.
    /// </summary>
    public ViewStateSnapshot CaptureViewState()
    {
        return new ViewStateSnapshot
        {
            ActiveDocumentName = _activeDocumentName,
            ActiveDocumentType = _activeDocumentType,
            ActiveDocumentQualifiedName = _activeDocumentQualifiedName,
            ConversationId = _currentConversationId,
            ConversationTitle = _currentConversationTitle
        };
    }

    /// <summary>
    /// Queue a state restore for after the new WebView is ready.
    /// </summary>
    public void QueueStateRestore(ViewStateSnapshot snapshot)
    {
        _pendingRestore = snapshot;
    }

    public void UpdateActiveDocument(string? name, string? type, string? qualifiedName)
    {
        _activeDocumentName = name;
        _activeDocumentType = type;
        _activeDocumentQualifiedName = qualifiedName;

        if (!_webViewReady) return;

        if (name == null)
        {
            SendToWebView("active_document_changed", new { name = (string?)null, type = (string?)null, qualifiedName = (string?)null });
        }
        else
        {
            SendToWebView("active_document_changed", new { name, type, qualifiedName });
        }
    }

    /// <summary>
    /// Full cleanup — clears conversation state, cancels requests.
    /// Called when the user intentionally closes AIDE (not during a toggle).
    /// </summary>
    public void FullCleanup()
    {
        _claudeApi?.Cancel();
        _isChatProcessing = false;
        _conversation?.Clear();
        _cachedContext = null;
        _webViewReady = false;
        _pendingReferences.Clear();
        _activeDocumentName = null;
        _activeDocumentType = null;
        _activeDocumentQualifiedName = null;
        _currentConversationId = null;
        _currentConversationTitle = null;
        DocumentReferenceStore.OnDocumentReferenced -= OnDocumentReferenced;
        if (_webView != null)
        {
            _webView.MessageReceived -= OnMessageReceived;
            _webView = null;
        }
    }

    // ========================================================================
    // Private — Document References
    // ========================================================================

    private void OnDocumentReferenced(DocumentReference reference)
    {
        if (!_webViewReady)
        {
            _pendingReferences.Add(reference);
            DiagLog($"OnDocumentReferenced: queued '{reference.QualifiedName}' (WebView not ready)");
            return;
        }

        SendDocumentReference(reference);
    }

    private void FlushPendingReferences()
    {
        if (_pendingReferences.Count == 0) return;

        SendToWebView("skip_auto_load", new { });

        foreach (var reference in _pendingReferences)
            SendDocumentReference(reference);
        _pendingReferences.Clear();
    }

    private void SendDocumentReference(DocumentReference reference)
    {
        var messageType = reference.Action == DocumentAction.Explain
            ? "auto_explain"
            : "document_referenced";

        SendToWebView(messageType, new
        {
            type = reference.ElementType,
            qualifiedName = reference.QualifiedName
        });
    }

    // ========================================================================
    // Private — Logging
    // ========================================================================

    private const long MaxLogFileSize = 5 * 1024 * 1024; // 5 MB

    private void DiagLog(string message)
    {
        var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AideLite");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "debug.log");

            if (File.Exists(logPath))
            {
                var info = new FileInfo(logPath);
                if (info.Length > MaxLogFileSize)
                {
                    var oldPath = logPath + ".old";
                    File.Copy(logPath, oldPath, overwrite: true);
                    File.WriteAllText(logPath, logLine + Environment.NewLine);
                    return;
                }
            }

            File.AppendAllText(logPath, logLine + Environment.NewLine);
        }
        catch { /* ignore file errors */ }

        try { _logService.Info($"AIDE Lite: {message}"); } catch { }
    }

    // ========================================================================
    // Private — Service initialization
    // ========================================================================

    private void EnsureServicesInitialized()
    {
        var model = Model;

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
            _toolRegistry.Register(new ValidateOqlQueryTool(_contextExtractor, domainModelReader));
            _toolRegistry.Register(new CreateMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new RenameMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new AddActivitiesToMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new ReplaceMicroflowTool(_contextExtractor, microflowGenerator));
            _toolRegistry.Register(new EditMicroflowActivityTool(_contextExtractor, microflowGenerator));

            _toolExecutor = new ToolExecutor(_toolRegistry, _logService);

            LoadUserRules();
        }
    }

    // ========================================================================
    // Private — Message handling
    // ========================================================================

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
                    MarkWebViewReady();
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
                case "delete_all_conversations":
                    HandleDeleteAllConversations();
                    break;
                case "save_chat_state":
                    HandleSaveChatState(data);
                    break;
                case "consent_accepted":
                    HandleConsentAccepted();
                    break;
                case "open_document":
                    HandleOpenDocument(data);
                    break;
                case "toggle_view":
                    DiagLog("OnMessageReceived: toggle_view requested");
                    OnToggleRequested?.Invoke();
                    break;
                case "MessageListenerRegistered":
                    DiagLog("OnMessageReceived: JS message listener registered");
                    MarkWebViewReady();
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

    private void MarkWebViewReady()
    {
        if (_webViewReady) return;
        _webViewReady = true;
        DiagLog("MarkWebViewReady: WebView bridge ready, flushing pending references");
        FlushPendingReferences();

        // If a view state restore is pending (from a toggle), send it now
        if (_pendingRestore != null)
        {
            var snapshot = _pendingRestore;
            _pendingRestore = null;
            SendToWebView("restore_view_state", new
            {
                conversationId = snapshot.ConversationId,
                conversationTitle = snapshot.ConversationTitle,
                activeDocumentName = snapshot.ActiveDocumentName,
                activeDocumentType = snapshot.ActiveDocumentType,
                activeDocumentQualifiedName = snapshot.ActiveDocumentQualifiedName
            });

            // Re-send document index so linkifyDocumentReferences works in the new view
            if (_cachedContext != null)
            {
                var documentIndex = BuildDocumentIndex();
                SendToWebView("context_loaded", new
                {
                    summary = $"{_cachedContext.Modules.Count} modules, " +
                        $"{_cachedContext.Modules.Sum(m => m.Entities.Count)} entities, " +
                        $"{_cachedContext.Modules.Sum(m => m.Microflows.Count)} microflows",
                    documentIndex
                });
            }

            // Also trigger loading the current conversation so display history is restored
            if (snapshot.ConversationId != null)
            {
                EnsureServicesInitialized();
                var conversation = _historyService!.LoadConversation(snapshot.ConversationId);
                if (conversation != null)
                {
                    SendToWebView("conversation_loaded", new
                    {
                        id = conversation.Id,
                        title = conversation.Title,
                        displayHistory = conversation.DisplayHistory
                    });
                }
            }
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

            DiagLog($"[2/6] Message received (length={messageText.Length})");

            EnsureServicesInitialized();

            var consentConfig = _configService.GetConfig();
            if (!consentConfig.HasAcceptedDataConsent)
            {
                DiagLog("[2/6] Data consent not yet accepted — prompting user");
                _conversation!.AddUserMessage(messageText);
                SendToWebView("consent_required", new { pendingMessage = true });
                return;
            }

            if (_currentConversationId == null)
            {
                _currentConversationId = Guid.NewGuid().ToString("N");
                _currentConversationCreatedAt = DateTime.UtcNow;
                _currentConversationTitle = messageText.Length > 60
                    ? messageText[..60] + "..."
                    : messageText;
            }
            DiagLog("[3/6] Services initialized");

            var rawMode = data?["mode"]?.GetValue<string>();
            var mode = rawMode is "agent" or "ask" ? rawMode : "ask";
            var isAskMode = mode == "ask";

            messageText = PrependFileAttachments(data, messageText);
            messageText = PrependDocumentReferences(data, messageText);
            messageText = PrependActiveDocumentContext(data, messageText);

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

                if (!response.HasToolCalls)
                {
                    if (!string.IsNullOrEmpty(response.FullText))
                        _conversation.AddAssistantMessage(response.FullText);
                    SendToWebView("chat_streaming", new { token = "", done = true });
                    SendToWebView("token_usage", new { inputTokens = totalInputTokens, outputTokens = totalOutputTokens, cacheCreationTokens = totalCacheCreation, cacheReadTokens = totalCacheRead, contextUsedTokens = lastRoundInputTokens + lastRoundOutputTokens, contextLimitTokens = GetContextLimit(config.SelectedModel) });
                    if (modelWasModified) SendToWebView("model_changed", new { message = "Model was modified. Click \u21BB to refresh context for future requests." });
                    return;
                }

                _conversation.AddAssistantTurn(response.FullText, response.ToolCalls);

                var toolResults = new List<(string ToolUseId, string Content, bool IsError)>();
                foreach (var toolCall in response.ToolCalls)
                {
                    var toolResult = _toolExecutor!.Execute(toolCall.Name, toolCall.InputJson, isAskMode);
                    SendToWebView("tool_result", new
                    {
                        toolName = toolCall.Name,
                        summary = toolResult.Summary
                    });

                    toolResults.Add((toolCall.Id, toolResult.Content, !toolResult.Success));

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

                _conversation.AddToolResults(toolResults);
                messages = _conversation.BuildApiMessages();
            }

            DiagLog($"[6/6] Max tool rounds ({maxToolRounds}) reached. Total tokens: in={totalInputTokens}, out={totalOutputTokens}, cacheWrite={totalCacheCreation}, cacheRead={totalCacheRead}");
            SendToWebView("chat_streaming", new { token = "\n\n*[Reached maximum tool rounds (" + maxToolRounds + "). Break complex tasks into smaller steps.]*", done = false });
            SendToWebView("chat_streaming", new { token = "", done = true });
            SendToWebView("token_usage", new { inputTokens = totalInputTokens, outputTokens = totalOutputTokens, cacheCreationTokens = totalCacheCreation, cacheReadTokens = totalCacheRead, contextUsedTokens = lastRoundInputTokens + lastRoundOutputTokens, contextLimitTokens = GetContextLimit(config.SelectedModel) });
            if (modelWasModified) SendToWebView("model_changed", new { message = "Model was modified. Click \u21BB to refresh context for future requests." });
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

    private void HandleOpenDocument(JsonObject? data)
    {
        var qualifiedName = data?["qualifiedName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(qualifiedName))
        {
            DiagLog("HandleOpenDocument: no qualifiedName provided");
            return;
        }

        if (Model == null)
        {
            DiagLog("HandleOpenDocument: no model available");
            return;
        }

        var dotIndex = qualifiedName.IndexOf('.');
        if (dotIndex < 0)
        {
            DiagLog($"HandleOpenDocument: invalid qualifiedName format '{qualifiedName}'");
            return;
        }

        var moduleName = qualifiedName[..dotIndex];
        var docName = qualifiedName[(dotIndex + 1)..];
        var docType = data?["docType"]?.GetValue<string>() ?? "";

        try
        {
            foreach (var module in Model.Root.GetModules())
            {
                if (module.Name != moduleName) continue;

                // Entities aren't IDocument — open the module's domain model instead
                if (docType == "entity")
                {
                    var domainModel = module.DomainModel;
                    var opened = _dockingService.TryOpenEditor(domainModel);
                    DiagLog($"HandleOpenDocument: TryOpenEditor(DomainModel for entity '{qualifiedName}') = {opened}");
                    return;
                }

                foreach (var doc in MicroflowReader.GetAllDocumentsRecursive<IDocument>(module))
                {
                    if (doc.Name == docName)
                    {
                        var opened = _dockingService.TryOpenEditor(doc);
                        DiagLog($"HandleOpenDocument: TryOpenEditor('{qualifiedName}') = {opened}");
                        return;
                    }
                }

                // Fallback: try domain model if document not found (could be an entity without docType)
                {
                    var domainModel = module.DomainModel;
                    if (domainModel.GetEntities().Any(e => e.Name == docName))
                    {
                        var opened = _dockingService.TryOpenEditor(domainModel);
                        DiagLog($"HandleOpenDocument: entity fallback TryOpenEditor(DomainModel for '{qualifiedName}') = {opened}");
                        return;
                    }
                }
                break;
            }
            DiagLog($"HandleOpenDocument: document '{qualifiedName}' not found in model");
        }
        catch (Exception ex)
        {
            DiagLog($"HandleOpenDocument: error opening '{qualifiedName}': {ex.Message}");
        }
    }

    private void HandleConsentAccepted()
    {
        _configService.SaveConsent(true);
        DiagLog("Data consent accepted by user");
        SendToWebView("consent_saved", new { accepted = true });
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

            var documentIndex = BuildDocumentIndex();

            SendToWebView("context_loaded", new
            {
                summary = $"{_cachedContext.Modules.Count} modules, " +
                    $"{_cachedContext.Modules.Sum(m => m.Entities.Count)} entities, " +
                    $"{_cachedContext.Modules.Sum(m => m.Microflows.Count)} microflows",
                documentIndex
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
        if ((DateTime.UtcNow - _lastSettingsSave).TotalSeconds < 2)
        {
            DiagLog("HandleSaveSettings: throttled (< 2s since last save)");
            return;
        }
        _lastSettingsSave = DateTime.UtcNow;

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
        _currentConversationCreatedAt = default;
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

        if (_isChatProcessing)
        {
            _claudeApi?.Cancel();
            _isChatProcessing = false;
        }

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
        _currentConversationCreatedAt = conversation.CreatedAt;

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

    private void HandleDeleteAllConversations()
    {
        EnsureServicesInitialized();
        _historyService!.DeleteAllConversations();
        _currentConversationId = null;
        _currentConversationTitle = null;
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

            var existing = _historyService.LoadConversation(_currentConversationId);

            _historyService.SaveConversation(new SavedConversation
            {
                Id = _currentConversationId,
                Title = _currentConversationTitle ?? "Untitled",
                CreatedAt = _currentConversationCreatedAt != default
                    ? _currentConversationCreatedAt
                    : (existing?.CreatedAt ?? DateTime.UtcNow),
                DisplayHistory = displayHistory,
                ApiMessagesJson = _conversation.SerializeMessages()
            });
        }
        catch (Exception ex)
        {
            DiagLog($"HandleSaveChatState error: {ex.Message}");
        }
    }

    // ========================================================================
    // Private — Message building helpers
    // ========================================================================

    private static string PrependFileAttachments(JsonObject? data, string messageText)
    {
        const int maxFiles = 10;
        const long maxTotalContentBytes = 2 * 1024 * 1024;

        var filesNode = data?["files"];
        if (filesNode is not JsonArray filesArray || filesArray.Count == 0)
            return messageText;

        var sb = new System.Text.StringBuilder();
        var fileCount = 0;
        long totalContentBytes = 0;
        foreach (var item in filesArray)
        {
            if (fileCount >= maxFiles) break;

            var name = item?["name"]?.GetValue<string>();
            var language = item?["language"]?.GetValue<string>() ?? "";
            var content = item?["content"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name) || content == null) continue;

            totalContentBytes += content.Length;
            if (totalContentBytes > maxTotalContentBytes) break;

            var sizeLabel = content.Length >= 1024
                ? $"{content.Length / 1024.0:F1} KB"
                : $"{content.Length} B";

            sb.AppendLine($"[Attached file: {name} ({language}, {sizeLabel})]");
            sb.AppendLine($"```{language}");
            sb.AppendLine(content);
            sb.AppendLine("```");
            sb.AppendLine();
            fileCount++;
        }

        return sb.Length > 0
            ? sb.ToString() + messageText
            : messageText;
    }

    private static string PrependDocumentReferences(JsonObject? data, string messageText)
    {
        var docsNode = data?["documents"];
        if (docsNode is not JsonArray docsArray || docsArray.Count == 0)
            return messageText;

        var lines = new List<string>();
        foreach (var item in docsArray)
        {
            var type = item?["type"]?.GetValue<string>() ?? "document";
            var qname = item?["qualifiedName"]?.GetValue<string>();
            if (string.IsNullOrEmpty(qname)) continue;

            var toolHint = type switch
            {
                "entity" => "use get_entity_details to inspect it",
                "microflow" => "use get_microflow_details to inspect it",
                "page" => "use get_pages to list page details",
                "enumeration" => "use get_enumerations to inspect it",
                "constant" => "use search_model to find constant details",
                "java_action" => "use search_model to find Java action details",
                _ => "use search_model to find more information"
            };
            lines.Add($"[The user is asking about: @{qname} ({type}) — {toolHint}]");
        }

        return lines.Count > 0
            ? string.Join("\n", lines) + "\n\n" + messageText
            : messageText;
    }

    private string PrependActiveDocumentContext(JsonObject? data, string messageText)
    {
        var docsNode = data?["documents"];
        if (docsNode is JsonArray { Count: > 0 })
            return messageText;

        if (string.IsNullOrEmpty(_activeDocumentQualifiedName))
            return messageText;

        var type = _activeDocumentType ?? "document";
        var toolHint = type switch
        {
            "microflow" => "use get_microflow_details to inspect it",
            "page" => "use get_pages to list page details",
            "entity" => "use get_entity_details to inspect it",
            "enumeration" => "use get_enumerations to inspect it",
            "constant" => "use search_model to find constant details",
            "java_action" => "use search_model to find Java action details",
            _ => "use search_model to find more information"
        };

        var contextLine = $"[The user is currently viewing: @{_activeDocumentQualifiedName} ({type}) — {toolHint}]";
        return contextLine + "\n\n" + messageText;
    }

    private static readonly HashSet<string> AllowedImageMediaTypes = new(StringComparer.Ordinal)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp"
    };

    private List<ImageAttachment> ParseImageAttachments(JsonObject? data)
    {
        const int maxImages = 5;
        const long maxTotalBase64Bytes = 20 * 1024 * 1024;

        var result = new List<ImageAttachment>();
        var imagesNode = data?["images"];
        if (imagesNode is not JsonArray imagesArray) return result;

        long totalBase64Bytes = 0;

        foreach (var item in imagesArray)
        {
            if (result.Count >= maxImages)
            {
                DiagLog($"ParseImageAttachments: max image count ({maxImages}) reached, skipping remaining");
                break;
            }

            var base64 = item?["base64"]?.GetValue<string>();
            var mediaType = item?["mediaType"]?.GetValue<string>();
            if (string.IsNullOrEmpty(base64) || string.IsNullOrEmpty(mediaType)) continue;
            if (!AllowedImageMediaTypes.Contains(mediaType))
            {
                DiagLog($"ParseImageAttachments: skipping unsupported media type '{mediaType}'");
                continue;
            }

            totalBase64Bytes += base64.Length;
            if (totalBase64Bytes > maxTotalBase64Bytes)
            {
                DiagLog($"ParseImageAttachments: total base64 size exceeds {maxTotalBase64Bytes / (1024 * 1024)}MB, skipping remaining");
                break;
            }

            result.Add(new ImageAttachment(base64, mediaType));
        }
        return result;
    }

    // ========================================================================
    // Private — WebView communication
    // ========================================================================

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

    // ========================================================================
    // Private — User rules
    // ========================================================================

    private const long MaxRulesFileSize = 64 * 1024;

    private void LoadUserRules()
    {
        try
        {
            var assemblyDir = Path.GetDirectoryName(GetType().Assembly.Location)!;
            var appRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", ".."));

            if (!Directory.GetFiles(appRoot, "*.mpr").Any())
            {
                DiagLog("LoadUserRules: No .mpr file found in resolved project root — skipping rules");
                _userRules = null;
                return;
            }

            var rulesPath = Path.Combine(appRoot, ".aide-lite-rules.md");
            if (!File.Exists(rulesPath))
            {
                _userRules = null;
                return;
            }

            var fileInfo = new FileInfo(rulesPath);
            if (fileInfo.Length > MaxRulesFileSize)
            {
                DiagLog($"LoadUserRules: Rules file exceeds {MaxRulesFileSize / 1024}KB limit ({fileInfo.Length} bytes) — skipping");
                _userRules = null;
                return;
            }

            _userRules = File.ReadAllText(rulesPath);
        }
        catch (Exception ex)
        {
            DiagLog($"LoadUserRules: Failed to load rules: {ex.Message}");
            _userRules = null;
        }
    }

    private List<object> BuildDocumentIndex()
    {
        var documentIndex = new List<object>();
        if (_cachedContext == null) return documentIndex;

        foreach (var module in _cachedContext.Modules)
        {
            foreach (var e in module.Entities)
                documentIndex.Add(new { qualifiedName = $"{module.Name}.{e.Name}", type = "entity" });
            foreach (var mf in module.Microflows)
                documentIndex.Add(new { qualifiedName = $"{module.Name}.{mf.Name}", type = "microflow" });
            foreach (var p in module.Pages)
                documentIndex.Add(new { qualifiedName = $"{module.Name}.{p.Name}", type = "page" });
            foreach (var en in module.Enumerations)
                documentIndex.Add(new { qualifiedName = $"{module.Name}.{en.Name}", type = "enumeration" });
        }
        return documentIndex;
    }

    private static int GetContextLimit(string model) => 200_000;
}
