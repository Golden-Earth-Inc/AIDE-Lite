// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Settings dialog ViewModel — API key, model selection, and configuration
// ============================================================================
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.Services;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Dialogs;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;

namespace AideLite.ViewModels;

/// <summary>
/// Modal dialog for configuring AIDE Lite settings (API key, Claude model, context depth, max tokens).
/// Rendered as a WebView loading the settings.html page from the local web server.
/// </summary>
public class SettingsDialogViewModel : WebViewModalDialogViewModel
{
    private readonly ConfigurationService _configService;
    private readonly ILogService _logService;
    private readonly IDialogService _dialogService;
    private readonly Uri _webServerBaseUrl;
    private IWebView? _webView;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SettingsDialogViewModel(
        ConfigurationService configService,
        ILogService logService,
        IDialogService dialogService,
        Uri webServerBaseUrl)
        : base("AIDE Lite Settings")
    {
        _configService = configService;
        _logService = logService;
        _dialogService = dialogService;
        _webServerBaseUrl = webServerBaseUrl;
        Width = 420;
        Height = 460;
    }

    public override void InitWebView(IWebView webView)
    {
        _webView = webView;
        webView.Address = new Uri(_webServerBaseUrl, "aide-lite/settings");
        webView.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Handles messages sent from the settings WebView (JS side) to C#.
    /// Protocol: JS must send "MessageListenerRegistered" first before it can receive PostMessage calls.
    /// </summary>
    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            var messageType = e.Message;
            var data = e.Data;

            switch (messageType)
            {
                case "get_settings":
                    SendCurrentSettings();
                    break;
                case "save_settings":
                    HandleSaveSettings(data);
                    break;
                case "cancel_settings":
                    _dialogService.CloseDialog(this);
                    break;
                case "MessageListenerRegistered":
                    // JS ready to receive messages — required handshake before C# can PostMessage
                    break;
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite Settings: Error: {ex.Message}");
        }
    }

    private void SendCurrentSettings()
    {
        var config = _configService.GetConfig();
        SendToWebView("load_settings", new
        {
            hasKey = _configService.HasApiKey(),
            selectedModel = config.SelectedModel,
            contextDepth = config.ContextDepth,
            maxTokens = config.MaxTokens,
            theme = config.Theme
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
        {
            tokens = data["maxTokens"]!.GetValue<int>();
        }

        _configService.SaveConfig(apiKey, model, depth, tokens, theme);
        SendToWebView("settings_saved", new { success = true });
        _dialogService.CloseDialog(this);
    }

    private void SendToWebView(string type, object data)
    {
        var serializedData = JsonSerializer.Serialize(data, JsonOptions);
        _webView?.PostMessage(type, JsonNode.Parse(serializedData)!);
    }
}
