// ============================================================================
// AIDE Lite - Shared State & Constants
// All mutable state lives here. Modules use AIDE.state.get/set instead of globals.
// ============================================================================
(function (AIDE) {
    'use strict';

    // --- Constants ---
    AIDE.CONST = {
        VIEW_MODES: { PANE: 'pane', TAB: 'tab' },
        MAX_IMAGE_SIZE: 20 * 1024 * 1024,
        MAX_FILE_SIZE: 500 * 1024,
        MAX_TEXTAREA_HEIGHT: 120,
        ALLOWED_IMAGE_TYPES: { 'image/jpeg': true, 'image/png': true, 'image/gif': true, 'image/webp': true },
        ALLOWED_FILE_EXTENSIONS: {
            '.json': 'json', '.xml': 'xml', '.yaml': 'yaml', '.yml': 'yaml',
            '.java': 'java', '.js': 'javascript', '.css': 'css', '.html': 'html',
            '.md': 'markdown', '.txt': 'text', '.log': 'log', '.csv': 'csv',
            '.sql': 'sql', '.properties': 'properties'
        }
    };

    // --- Mutable State ---
    var _state = {
        isStreaming: false,
        streamBuffer: '',
        streamElement: null,
        cumulativeInputTokens: 0,
        cumulativeOutputTokens: 0,
        currentTheme: 'light',
        chatHistory: [],
        pendingImages: [],
        pendingFiles: [],
        pendingDocuments: [],
        retryCountdownInterval: null,
        retryAttemptCount: 0,
        autoLoadPending: false,
        initialSettingsLoaded: false,
        skipAutoLoadConversation: false,
        activeDocument: null,
        viewMode: AIDE.CONST.VIEW_MODES.PANE
    };

    AIDE.state = {
        get: function (key) {
            return _state[key];
        },
        set: function (key, value) {
            _state[key] = value;
        },
        reset: function () {
            _state.isStreaming = false;
            _state.streamBuffer = '';
            _state.streamElement = null;
            _state.cumulativeInputTokens = 0;
            _state.cumulativeOutputTokens = 0;
            _state.chatHistory = [];
            _state.pendingImages = [];
            _state.pendingFiles = [];
            _state.pendingDocuments = [];
            _state.retryCountdownInterval = null;
            _state.retryAttemptCount = 0;
            _state.autoLoadPending = false;
            _state.viewMode = AIDE.CONST.VIEW_MODES.PANE;
        }
    };

})(window.AIDE = window.AIDE || {});
