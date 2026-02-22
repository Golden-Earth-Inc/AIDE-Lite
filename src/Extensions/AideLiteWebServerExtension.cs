// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Local web server routes for serving the chat UI static assets
// ============================================================================
using System.ComponentModel.Composition;
using System.Net;
using System.Text;
using Mendix.StudioPro.ExtensionsAPI.UI.WebServer;

namespace AideLite.Extensions;

// MEF discovers this class automatically via [Export].
// Registers routes on Studio Pro's built-in local web server so the WebView can load our HTML/JS/CSS.
[Export(typeof(WebServerExtension))]
public class AideLiteWebServerExtension : WebServerExtension
{
    public override void InitializeWebServer(IWebServer webServer)
    {
        // Pages
        webServer.AddRoute("aide-lite/chat", ServeFile("WebAssets/pages/chat.html", "text/html; charset=utf-8"));
        webServer.AddRoute("aide-lite/settings", ServeFile("WebAssets/pages/settings.html", "text/html; charset=utf-8"));
        // Styles
        webServer.AddRoute("aide-lite/styles/main.css", ServeFile("WebAssets/styles/main.css", "text/css; charset=utf-8"));
        // Core modules (state, bridge, rendering, UI helpers, attachments)
        webServer.AddRoute("aide-lite/core/state.js", ServeFile("WebAssets/core/state.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/core/bridge.js", ServeFile("WebAssets/core/bridge.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/core/markdown.js", ServeFile("WebAssets/core/markdown.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/core/ui.js", ServeFile("WebAssets/core/ui.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/core/attachments.js", ServeFile("WebAssets/core/attachments.js", "application/javascript; charset=utf-8"));
        // View modules (chat, history/export, settings/theme)
        webServer.AddRoute("aide-lite/views/chat.js", ServeFile("WebAssets/views/chat.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/views/history.js", ServeFile("WebAssets/views/history.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/views/settings.js", ServeFile("WebAssets/views/settings.js", "application/javascript; charset=utf-8"));
        // Entry-point scripts
        webServer.AddRoute("aide-lite/chat-page.js", ServeFile("WebAssets/chat-page.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/settings-page.js", ServeFile("WebAssets/settings-page.js", "application/javascript; charset=utf-8"));
    }

    /// <summary>
    /// Returns a request handler that serves a file from the extension's assembly directory.
    /// </summary>
    private HandleWebRequestAsync ServeFile(string relativePath, string contentType)
    {
        return async (HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken) =>
        {
            // Resolve file relative to the deployed extension DLL location
            var assemblyDir = Path.GetDirectoryName(GetType().Assembly.Location)!;
            var filePath = Path.Combine(assemblyDir, relativePath);

            if (!File.Exists(filePath))
            {
                response.StatusCode = 404;
                response.ContentType = "text/plain";
                var notFound = Encoding.UTF8.GetBytes($"File not found: {relativePath}");
                response.ContentLength64 = notFound.Length;
                await response.OutputStream.WriteAsync(notFound, cancellationToken);
                return;
            }

            var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
            response.StatusCode = 200;
            response.ContentType = contentType;
            response.ContentLength64 = content.Length;
            // Disable caching so code changes take effect immediately during development
            response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
            response.Headers.Add("Pragma", "no-cache");
            await response.OutputStream.WriteAsync(content, cancellationToken);
        };
    }
}
