// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Entry point — DOM element cache, event listener wiring, bootstrap.
// ============================================================================

// Follows Mendix WebView PostMessage protocol:
//   C# sends:  PostMessage("type", data)
//   JS gets:   event.data = { message: "type", data: ... }
//   JS sends:  postMessage({ message: "type", data: ... })
//   C# gets:   e.Message = "type", e.Data = ...

(function (AIDE) {
    'use strict';

    var state = AIDE.state;

    // --- DOM References ---
    AIDE.dom = {
        chatArea: document.getElementById('chatArea'),
        chatInput: document.getElementById('chatInput'),
        modeSelect: document.getElementById('modeSelect'),
        sendBtn: document.getElementById('sendBtn'),
        refreshBtn: document.getElementById('refreshBtn'),
        newChatBtn: document.getElementById('newChatBtn'),
        settingsBtn: document.getElementById('settingsBtn'),
        settingsModal: document.getElementById('settingsModal'),
        saveSettingsBtn: document.getElementById('saveSettingsBtn'),
        cancelSettingsBtn: document.getElementById('cancelSettingsBtn'),
        toolActivity: document.getElementById('toolActivity'),
        toolLabel: document.getElementById('toolLabel'),
        stopBtn: document.getElementById('stopBtn'),
        contextDot: document.getElementById('contextDot'),
        contextText: document.getElementById('contextText'),
        modelBadge: document.getElementById('modelBadge'),
        welcomeScreen: document.getElementById('welcomeScreen'),
        processingBar: document.getElementById('processingBar'),
        processingLabel: document.getElementById('processingLabel'),
        themeToggleBtn: document.getElementById('themeToggleBtn'),
        themeSelect: document.getElementById('themeSelect'),
        historyBtn: document.getElementById('historyBtn'),
        historyModal: document.getElementById('historyModal'),
        historyList: document.getElementById('historyList'),
        historyCloseBtn: document.getElementById('historyCloseBtn'),
        historyOverlay: document.getElementById('historyOverlay'),
        contextUsage: document.getElementById('contextUsage'),
        contextUsageFill: document.getElementById('contextUsageFill'),
        contextUsageLabel: document.getElementById('contextUsageLabel'),
        exportBtn: document.getElementById('exportBtn'),
        exportModal: document.getElementById('exportModal'),
        exportDownloadBtn: document.getElementById('exportDownloadBtn'),
        exportCancelBtn: document.getElementById('exportCancelBtn'),
        exportOverlay: document.getElementById('exportOverlay'),
        exportToolActivity: document.getElementById('exportToolActivity'),
        inputArea: document.getElementById('inputArea'),
        filePreview: document.getElementById('filePreview'),
        imagePreview: document.getElementById('imagePreview'),
        attachBtn: document.getElementById('attachBtn'),
        attachFileInput: document.getElementById('attachFileInput'),
        helpBtn: document.getElementById('helpBtn'),
        helpModal: document.getElementById('helpModal'),
        helpCloseBtn: document.getElementById('helpCloseBtn'),
        helpOverlay: document.getElementById('helpOverlay'),
        activeDocBar: document.getElementById('activeDocBar'),
        activeDocIcon: document.getElementById('activeDocIcon'),
        activeDocText: document.getElementById('activeDocText'),
        activeDocAddBtn: document.getElementById('activeDocAddBtn'),
        consentModal: document.getElementById('consentModal'),
        consentAcceptBtn: document.getElementById('consentAcceptBtn'),
        consentDeclineBtn: document.getElementById('consentDeclineBtn'),
        privacyBtn: document.getElementById('privacyBtn'),
        privacyModal: document.getElementById('privacyModal'),
        privacyCloseBtn: document.getElementById('privacyCloseBtn'),
        privacyOverlay: document.getElementById('privacyOverlay'),
        toggleViewBtn: document.getElementById('toggleViewBtn')
    };

    var d = AIDE.dom;

    // --- Event Listeners ---
    d.modeSelect.addEventListener('change', function () {
        d.chatInput.placeholder = d.modeSelect.value === 'ask'
            ? 'Ask mode \u2014 read-only, no changes to your app...'
            : 'Agent mode \u2014 can read and modify your app...';
    });

    d.sendBtn.addEventListener('click', AIDE.sendMessage);

    d.stopBtn.addEventListener('click', function () {
        AIDE.sendToBackend('cancel');
        AIDE.endStream();
    });

    d.chatInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            AIDE.sendMessage();
        }
    });

    d.chatInput.addEventListener('input', function () {
        this.style.height = 'auto';
        this.style.height = Math.min(this.scrollHeight, AIDE.CONST.MAX_TEXTAREA_HEIGHT) + 'px';
    });

    d.refreshBtn.addEventListener('click', function () {
        d.contextDot.className = 'status-dot loading';
        d.contextText.textContent = 'Loading context...';
        AIDE.sendToBackend('get_context');
    });

    d.newChatBtn.addEventListener('click', function () {
        if (state.get('isStreaming')) {
            AIDE.sendToBackend('cancel');
            AIDE.endStream();
        }
        d.chatArea.innerHTML = '';
        if (d.welcomeScreen) {
            d.chatArea.appendChild(d.welcomeScreen);
            d.welcomeScreen.style.display = '';
        }
        d.sendBtn.disabled = false;
        state.set('cumulativeInputTokens', 0);
        state.set('cumulativeOutputTokens', 0);
        state.set('chatHistory', []);
        state.set('pendingImages', []);
        state.set('pendingFiles', []);
        state.set('pendingDocuments', []);
        AIDE.renderImagePreviews();
        AIDE.renderFilePreviews();
        AIDE.renderDocumentPreviews();
        AIDE.updateTokenBadge();
        AIDE.resetContextUsage();
        AIDE.sendToBackend('new_chat');
    });

    d.settingsBtn.addEventListener('click', AIDE.openSettings);
    d.saveSettingsBtn.addEventListener('click', AIDE.saveSettings);
    d.cancelSettingsBtn.addEventListener('click', AIDE.closeSettings);

    if (d.themeToggleBtn) {
        d.themeToggleBtn.addEventListener('click', function () {
            var newTheme = state.get('currentTheme') === 'dark' ? 'light' : 'dark';
            AIDE.applyTheme(newTheme);
            AIDE.sendToBackend('save_settings', { theme: newTheme });
        });
    }

    if (d.themeSelect) {
        d.themeSelect.addEventListener('change', function () {
            AIDE.applyTheme(this.value);
        });
    }

    if (d.historyBtn) d.historyBtn.addEventListener('click', AIDE.openHistory);
    if (d.historyCloseBtn) d.historyCloseBtn.addEventListener('click', AIDE.closeHistory);
    if (d.historyOverlay) d.historyOverlay.addEventListener('click', AIDE.closeHistory);

    var historyClearAllBtn = document.getElementById('historyClearAllBtn');
    var confirmClearModal = document.getElementById('confirmClearModal');
    var confirmClearYesBtn = document.getElementById('confirmClearYesBtn');
    var confirmClearNoBtn = document.getElementById('confirmClearNoBtn');
    var confirmClearOverlay = document.getElementById('confirmClearOverlay');
    if (historyClearAllBtn) historyClearAllBtn.addEventListener('click', function () {
        if (confirmClearModal) confirmClearModal.classList.remove('hidden');
    });
    if (confirmClearYesBtn) confirmClearYesBtn.addEventListener('click', function () {
        AIDE.sendToBackend('delete_all_conversations');
        if (confirmClearModal) confirmClearModal.classList.add('hidden');
    });
    if (confirmClearNoBtn) confirmClearNoBtn.addEventListener('click', function () {
        if (confirmClearModal) confirmClearModal.classList.add('hidden');
    });
    if (confirmClearOverlay) confirmClearOverlay.addEventListener('click', function () {
        if (confirmClearModal) confirmClearModal.classList.add('hidden');
    });

    if (d.exportBtn) d.exportBtn.addEventListener('click', AIDE.openExport);
    if (d.exportDownloadBtn) d.exportDownloadBtn.addEventListener('click', AIDE.exportChat);
    if (d.exportCancelBtn) d.exportCancelBtn.addEventListener('click', AIDE.closeExport);
    if (d.exportOverlay) d.exportOverlay.addEventListener('click', AIDE.closeExport);

    if (d.helpBtn) d.helpBtn.addEventListener('click', AIDE.openHelp);
    if (d.helpCloseBtn) d.helpCloseBtn.addEventListener('click', AIDE.closeHelp);
    if (d.helpOverlay) d.helpOverlay.addEventListener('click', AIDE.closeHelp);

    if (d.activeDocAddBtn) {
        d.activeDocAddBtn.addEventListener('click', function () {
            var doc = state.get('activeDocument');
            if (!doc) return;
            var pending = state.get('pendingDocuments');
            var already = pending.some(function (dd) { return dd.qualifiedName === doc.qualifiedName; });
            if (!already) {
                pending.push({ type: doc.type, qualifiedName: doc.qualifiedName });
                AIDE.renderDocumentPreviews();
            }
            d.chatInput.focus();
        });
    }

    // Consent
    if (d.consentAcceptBtn) d.consentAcceptBtn.addEventListener('click', function () {
        AIDE.sendToBackend('consent_accepted');
        if (d.consentModal) d.consentModal.classList.add('hidden');
    });
    if (d.consentDeclineBtn) d.consentDeclineBtn.addEventListener('click', function () {
        if (d.consentModal) d.consentModal.classList.add('hidden');
    });

    if (d.privacyBtn) d.privacyBtn.addEventListener('click', AIDE.openPrivacy);
    if (d.privacyCloseBtn) d.privacyCloseBtn.addEventListener('click', AIDE.closePrivacy);
    if (d.privacyOverlay) d.privacyOverlay.addEventListener('click', AIDE.closePrivacy);

    // Toggle view button (button is disabled during streaming via views/chat.js)
    if (d.toggleViewBtn) {
        d.toggleViewBtn.addEventListener('click', function () {
            AIDE.sendToBackend('toggle_view');
        });
    }

    // Detect initial mode from URL query param (?mode=pane|tab)
    (function () {
        var params = new URLSearchParams(window.location.search);
        var mode = params.get('mode');
        if (mode === AIDE.CONST.VIEW_MODES.TAB || mode === AIDE.CONST.VIEW_MODES.PANE) {
            state.set('viewMode', mode);
        }
        AIDE.updateToggleButton();
    })();

    // Close settings modal on overlay click
    document.querySelector('#settingsModal .modal-overlay')?.addEventListener('click', AIDE.closeSettings);

    // Modal close (X) buttons
    document.querySelector('#settingsModal .modal-close-btn')?.addEventListener('click', AIDE.closeSettings);
    document.querySelector('#historyModal .modal-close-btn')?.addEventListener('click', AIDE.closeHistory);
    document.querySelector('#exportModal .modal-close-btn')?.addEventListener('click', AIDE.closeExport);
    document.querySelector('#helpModal .modal-close-btn')?.addEventListener('click', AIDE.closeHelp);
    document.querySelector('#consentModal .modal-close-btn')?.addEventListener('click', function () {
        if (d.consentModal) d.consentModal.classList.add('hidden');
    });
    document.querySelector('#privacyModal .modal-close-btn')?.addEventListener('click', AIDE.closePrivacy);

    // Quick action buttons
    document.querySelectorAll('.quick-action').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var prompt = this.getAttribute('data-prompt');
            if (prompt) {
                d.chatInput.value = prompt;
                AIDE.sendMessage();
            }
        });
    });

    // --- Chat area delegated click handlers ---
    d.chatArea.addEventListener('click', function (e) {
        var target = e.target;

        // Document reference clicks
        if (target.classList.contains('doc-ref')) {
            var qualifiedName = target.getAttribute('data-qualified-name');
            var docType = target.getAttribute('data-doc-type');
            if (qualifiedName) {
                AIDE.sendToBackend('open_document', { qualifiedName: qualifiedName, docType: docType || '' });
            }
            return;
        }

        // Code block copy button clicks (delegated instead of inline onclick for CSP compliance)
        if (target.classList.contains('code-copy-btn')) {
            AIDE.copyCodeBlock(target);
        }
    });

    // --- Setup attachment handlers ---
    AIDE.setupDragDrop(d.inputArea, d.chatInput);
    AIDE.setupFilePicker(d.attachBtn, d.attachFileInput);

    // --- Initialize ---
    AIDE.initBridge();
    AIDE.sendToBackend('get_settings');

    // Security: freeze AIDE namespace to prevent runtime tampering by injected scripts.
    // AIDE.state remains mutable internally (closure-based), but the public API surface
    // (function references, constants, dom refs) cannot be overwritten.
    Object.freeze(AIDE.CONST);
    Object.freeze(AIDE.CONST.VIEW_MODES);
    Object.freeze(AIDE.CONST.ALLOWED_IMAGE_TYPES);
    Object.freeze(AIDE.CONST.ALLOWED_FILE_EXTENSIONS);
    Object.freeze(AIDE.dom);
    Object.freeze(AIDE);

})(window.AIDE = window.AIDE || {});
