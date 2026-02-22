// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Settings dialog logic — WebView bridge and form handling
// ============================================================================

(function () {
    'use strict';

    function sendToBackend(type, payload) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ message: type, data: payload || {} });
        }
    }

    function handleMessage(event) {
        var envelope = event.data;
        if (!envelope || typeof envelope.message !== 'string') return;

        var type = envelope.message;
        var data = envelope.data;

        if (type === 'load_settings' && data) {
            if (data.selectedModel) document.getElementById('modelSelect').value = data.selectedModel;
            if (data.contextDepth) document.getElementById('contextDepthSelect').value = data.contextDepth;
            if (data.maxTokens) document.getElementById('maxTokensInput').value = data.maxTokens;
            if (data.retryMaxAttempts != null) document.getElementById('retryMaxAttemptsInput').value = data.retryMaxAttempts;
            if (data.retryDelaySeconds != null) document.getElementById('retryDelaySecondsInput').value = data.retryDelaySeconds;
            if (data.maxToolRounds != null) document.getElementById('maxToolRoundsInput').value = data.maxToolRounds;
            if (data.promptCachingEnabled != null) document.getElementById('promptCachingCheckbox').checked = data.promptCachingEnabled;
            if (data.hasKey) document.getElementById('apiKeyInput').placeholder = '********** (key saved)';
            if (data.theme) {
                document.getElementById('themeSelect').value = data.theme;
                document.body.classList.toggle('dark', data.theme === 'dark');
                document.body.classList.toggle('settings-body', true);
            }
        }
        if (type === 'settings_saved') {
            // Dialog is closed by C# backend after saving
        }
    }

    document.getElementById('saveSettingsBtn').addEventListener('click', function () {
        sendToBackend('save_settings', {
            apiKey: document.getElementById('apiKeyInput').value,
            selectedModel: document.getElementById('modelSelect').value,
            contextDepth: document.getElementById('contextDepthSelect').value,
            maxTokens: parseInt(document.getElementById('maxTokensInput').value) || 8192,
            retryMaxAttempts: (function(v) { var n = parseInt(v); return isNaN(n) ? 20 : n; })(document.getElementById('retryMaxAttemptsInput').value),
            retryDelaySeconds: parseInt(document.getElementById('retryDelaySecondsInput').value) || 60,
            maxToolRounds: parseInt(document.getElementById('maxToolRoundsInput').value) || 10,
            promptCachingEnabled: document.getElementById('promptCachingCheckbox').checked,
            theme: document.getElementById('themeSelect').value
        });
    });

    document.getElementById('cancelSettingsBtn').addEventListener('click', function () {
        sendToBackend('cancel_settings', {});
    });

    // Per Mendix API docs: register message handler, then post MessageListenerRegistered
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', handleMessage);
        sendToBackend('MessageListenerRegistered');
    }

    // Request current settings
    sendToBackend('get_settings', {});
})();
