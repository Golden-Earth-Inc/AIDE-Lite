// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Thin tab shell — delegates all chat logic to ChatController
// ============================================================================
using System.Runtime.Versioning;
using AideLite.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.Tab;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;

namespace AideLite.ViewModels;

[SupportedOSPlatform("windows")]
public class AideLiteTabWebViewModel : WebViewTabViewModel
{
    private readonly ChatController _chatController;
    private readonly Uri _webServerBaseUrl;

    public AideLiteTabWebViewModel(ChatController chatController, Uri webServerBaseUrl)
    {
        _chatController = chatController;
        _webServerBaseUrl = webServerBaseUrl;
        Title = "AIDE Lite";
    }

    public override void InitWebView(IWebView webView)
    {
        webView.Address = new Uri(_webServerBaseUrl, $"aide-lite/chat?mode=tab&v={DateTime.UtcNow.Ticks}");
        _chatController.AttachWebView(webView);
    }

    internal void UpdateActiveDocument(string? name, string? type, string? qualifiedName)
    {
        _chatController.UpdateActiveDocument(name, type, qualifiedName);
    }

    internal void Cleanup()
    {
        _chatController.DetachWebView();
    }
}
