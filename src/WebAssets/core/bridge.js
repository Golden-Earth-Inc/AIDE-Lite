// ============================================================================
// AIDE Lite - WebView Communication Bridge
// C# <-> JS message passing via WebView2 PostMessage protocol.
// ============================================================================
(function (AIDE) {
    'use strict';

    var state = AIDE.state;

    AIDE.sendToBackend = function (type, payload) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ message: type, data: payload || {} });
        } else {
            console.log('WebView bridge not available. Message:', type, payload);
        }
    };

    AIDE.handleMessage = function (event) {
        var envelope = event.data;
        if (!envelope) return;
        var type = envelope.message;
        var data = envelope.data;
        if (type) {
            AIDE.handleBackendMessage(type, data);
        }
    };

    AIDE.handleBackendMessage = function (type, data) {
        switch (type) {
            case 'chat_streaming':
                AIDE.handleStreamChunk(data);
                break;
            case 'tool_start':
                AIDE.handleToolStart(data);
                break;
            case 'tool_result':
                AIDE.handleToolResult(data);
                break;
            case 'context_loaded':
                AIDE.handleContextLoaded(data);
                break;
            case 'microflow_created':
                AIDE.handleMicroflowCreated(data);
                break;
            case 'model_changed':
                AIDE.handleModelChanged(data);
                break;
            case 'token_usage':
                AIDE.handleTokenUsage(data);
                break;
            case 'retry_wait':
                AIDE.handleRetryWait(data);
                break;
            case 'history_list':
                AIDE.handleHistoryList(data);
                break;
            case 'conversation_loaded':
                AIDE.handleConversationLoaded(data);
                break;
            case 'error':
                AIDE.handleError(data);
                break;
            case 'load_settings':
                AIDE.handleLoadSettings(data);
                break;
            case 'settings_saved':
                AIDE.handleSettingsSaved(data);
                break;
            case 'skip_auto_load':
                state.set('skipAutoLoadConversation', true);
                state.set('autoLoadPending', false);
                break;
            case 'active_document_changed':
                AIDE.handleActiveDocumentChanged(data);
                break;
            case 'auto_explain':
                AIDE.handleAutoExplain(data);
                break;
            case 'document_referenced':
                AIDE.handleDocumentReferenced(data);
                break;
            case 'consent_required':
                AIDE.handleConsentRequired();
                break;
            case 'consent_saved':
                AIDE.handleConsentSaved();
                break;
            case 'restore_view_state':
                AIDE.handleRestoreViewState(data);
                break;
            case 'set_view_mode':
                AIDE.handleSetViewMode(data);
                break;
        }
    };

    AIDE.initBridge = function () {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', AIDE.handleMessage);
            AIDE.sendToBackend('MessageListenerRegistered');
        }
    };

})(window.AIDE = window.AIDE || {});
