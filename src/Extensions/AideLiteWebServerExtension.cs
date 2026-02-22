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
        // Each route maps a URL path to a file in the deployed WebAssets folder
        webServer.AddRoute("aide-lite/chat", ServeFile("WebAssets/chat.html", "text/html; charset=utf-8"));
        webServer.AddRoute("aide-lite/settings", ServeFile("WebAssets/settings.html", "text/html; charset=utf-8"));
        webServer.AddRoute("aide-lite/styles.css", ServeFile("WebAssets/styles.css", "text/css; charset=utf-8"));
        webServer.AddRoute("aide-lite/app.js", ServeFile("WebAssets/app.js", "application/javascript; charset=utf-8"));
        webServer.AddRoute("aide-lite/settings.js", ServeFile("WebAssets/settings.js", "application/javascript; charset=utf-8"));
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
