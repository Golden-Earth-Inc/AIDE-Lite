// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Chat UI logic — message handling, streaming, markdown rendering, and WebView bridge
// ============================================================================

// Follows Mendix WebView PostMessage protocol:
//   C# sends:  PostMessage("type", data)
//   JS gets:   event.data = { message: "type", data: ... }
//   JS sends:  postMessage({ message: "type", data: ... })
//   C# gets:   e.Message = "type", e.Data = ...

(function () {
    'use strict';

    // --- State ---
    var isStreaming = false;
    var streamBuffer = '';
    var streamElement = null;
    var cumulativeInputTokens = 0;
    var cumulativeOutputTokens = 0;
    var currentTheme = 'light';
    var chatHistory = [];

    // --- DOM References ---
    var chatArea = document.getElementById('chatArea');
    var chatInput = document.getElementById('chatInput');
    var modeSelect = document.getElementById('modeSelect');
    var sendBtn = document.getElementById('sendBtn');
    var refreshBtn = document.getElementById('refreshBtn');
    var newChatBtn = document.getElementById('newChatBtn');
    var settingsBtn = document.getElementById('settingsBtn');
    var settingsModal = document.getElementById('settingsModal');
    var saveSettingsBtn = document.getElementById('saveSettingsBtn');
    var cancelSettingsBtn = document.getElementById('cancelSettingsBtn');
    var toolActivity = document.getElementById('toolActivity');
    var toolLabel = document.getElementById('toolLabel');
    var stopBtn = document.getElementById('stopBtn');
    var contextDot = document.getElementById('contextDot');
    var contextText = document.getElementById('contextText');
    var modelBadge = document.getElementById('modelBadge');
    var welcomeScreen = document.getElementById('welcomeScreen');
    var processingBar = document.getElementById('processingBar');
    var processingLabel = document.getElementById('processingLabel');
    var themeToggleBtn = document.getElementById('themeToggleBtn');
    var themeSelect = document.getElementById('themeSelect');
    var historyBtn = document.getElementById('historyBtn');
    var historyModal = document.getElementById('historyModal');
    var historyList = document.getElementById('historyList');
    var historyCloseBtn = document.getElementById('historyCloseBtn');
    var historyOverlay = document.getElementById('historyOverlay');
    var contextUsage = document.getElementById('contextUsage');
    var contextUsageFill = document.getElementById('contextUsageFill');
    var contextUsageLabel = document.getElementById('contextUsageLabel');
    var exportBtn = document.getElementById('exportBtn');
    var exportModal = document.getElementById('exportModal');
    var exportDownloadBtn = document.getElementById('exportDownloadBtn');
    var exportCancelBtn = document.getElementById('exportCancelBtn');
    var exportOverlay = document.getElementById('exportOverlay');
    var exportToolActivity = document.getElementById('exportToolActivity');
    var consentModal = document.getElementById('consentModal');
    var consentAcceptBtn = document.getElementById('consentAcceptBtn');
    var consentDeclineBtn = document.getElementById('consentDeclineBtn');
    var privacyBtn = document.getElementById('privacyBtn');
    var privacyModal = document.getElementById('privacyModal');
    var privacyCloseBtn = document.getElementById('privacyCloseBtn');
    var privacyOverlay = document.getElementById('privacyOverlay');

    // --- WebView Bridge (C# <-> JS messaging) ---
    function sendToBackend(type, payload) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ message: type, data: payload || {} });
        } else {
            console.log('WebView bridge not available. Message:', type, payload);
        }
    }

    // --- Message Dispatch (C# PostMessage -> JS handler routing) ---
    function handleMessage(event) {
        // Mendix PostMessage delivers: event.data = { message: "type", data: ... }
        var envelope = event.data;
        if (!envelope) return;

        var type = envelope.message;
        var data = envelope.data;

        if (type) {
            handleBackendMessage(type, data);
        }
    }

    function handleBackendMessage(type, data) {
        switch (type) {
            case 'chat_streaming':
                handleStreamChunk(data);
                break;
            case 'chat_response':
                // Legacy: kept for backward compatibility but no longer sent by C#
                handleChatResponse(data);
                break;
            case 'tool_start':
                handleToolStart(data);
                break;
            case 'tool_result':
                handleToolResult(data);
                break;
            case 'context_loaded':
                handleContextLoaded(data);
                break;
            case 'microflow_created':
                handleMicroflowCreated(data);
                break;
            case 'model_changed':
                handleModelChanged(data);
                break;
            case 'token_usage':
                handleTokenUsage(data);
                break;
            case 'retry_wait':
                handleRetryWait(data);
                break;
            case 'history_list':
                handleHistoryList(data);
                break;
            case 'conversation_loaded':
                handleConversationLoaded(data);
                break;
            case 'error':
                handleError(data);
                break;
            case 'load_settings':
                handleLoadSettings(data);
                break;
            case 'settings_saved':
                handleSettingsSaved(data);
                break;
            case 'consent_required':
                handleConsentRequired();
                break;
            case 'consent_saved':
                handleConsentSaved();
                break;
        }
    }

    // --- Chat Message Handling ---
    function sendMessage() {
        var text = chatInput.value.trim();
        if (!text || isStreaming) return;

        hideWelcome();
        appendMessage('user', text);
        chatHistory.push({ type: 'user', content: text });
        chatInput.value = '';
        chatInput.style.height = 'auto';

        // Show stop button and processing bar immediately when user sends message
        isStreaming = true;
        retryAttemptCount = 0;
        sendBtn.disabled = true;
        sendBtn.classList.add('hidden');
        stopBtn.classList.remove('hidden');
        showProcessingBar('Thinking...');

        sendToBackend('chat', { message: text, mode: modeSelect.value });
    }

    function inlineStylesForCopy(clone) {
        clone.querySelectorAll('pre').forEach(function (el) {
            el.style.cssText = 'background:#1e1e2e;color:#cdd6f4;padding:10px;border-radius:6px;overflow-x:auto;margin:6px 0;font-family:"Cascadia Code","Consolas",monospace;font-size:12px;';
        });
        clone.querySelectorAll('pre code').forEach(function (el) {
            el.style.cssText = 'background:none;padding:0;color:inherit;font-family:inherit;font-size:inherit;';
        });
        clone.querySelectorAll('code').forEach(function (el) {
            if (el.parentElement && el.parentElement.tagName === 'PRE') return;
            el.style.cssText = 'background:#f0f0f0;padding:1px 4px;border-radius:3px;font-family:"Cascadia Code","Consolas",monospace;font-size:12px;';
        });
        clone.querySelectorAll('h1,h2,h3').forEach(function (el) {
            el.style.cssText = 'margin:8px 0 4px;font-weight:bold;';
        });
        clone.querySelectorAll('table').forEach(function (el) {
            el.style.cssText = 'border-collapse:collapse;margin:6px 0;font-size:12px;width:100%;';
        });
        clone.querySelectorAll('th,td').forEach(function (el) {
            el.style.cssText = 'border:1px solid #ddd;padding:4px 8px;text-align:left;';
        });
        clone.querySelectorAll('th').forEach(function (el) {
            el.style.fontWeight = '600';
            el.style.background = '#f5f5f5';
        });
        clone.querySelectorAll('blockquote').forEach(function (el) {
            el.style.cssText = 'border-left:3px solid #7c3aed;padding-left:10px;margin:6px 0;color:#555;';
        });
        clone.querySelectorAll('strong').forEach(function (el) {
            el.style.fontWeight = 'bold';
        });
        clone.querySelectorAll('em').forEach(function (el) {
            el.style.fontStyle = 'italic';
        });
        clone.querySelectorAll('ul,ol').forEach(function (el) {
            el.style.cssText = 'margin:4px 0;padding-left:20px;';
        });
        clone.querySelectorAll('a').forEach(function (el) {
            el.style.cssText = 'color:#7c3aed;text-decoration:underline;';
        });
    }

    function addCopyButton(div, markdown) {
        div.dataset.md = markdown;
        var btn = document.createElement('button');
        btn.className = 'copy-btn';
        btn.title = 'Copy (rich text + markdown)';
        btn.innerHTML = '&#x2398;';
        btn.addEventListener('click', function () {
            var md = div.dataset.md;
            var clone = div.cloneNode(true);
            var cloneBtn = clone.querySelector('.copy-btn');
            if (cloneBtn) cloneBtn.remove();
            inlineStylesForCopy(clone);

            var styledHtml = '<div style="font-family:system-ui,-apple-system,sans-serif;font-size:13px;line-height:1.5;color:#1a1a2e;">' + clone.innerHTML + '</div>';
            var htmlBlob = new Blob([styledHtml], { type: 'text/html' });
            var textBlob = new Blob([md], { type: 'text/plain' });

            navigator.clipboard.write([
                new ClipboardItem({ 'text/html': htmlBlob, 'text/plain': textBlob })
            ]).then(function () {
                btn.textContent = '\u2713';
                setTimeout(function () { btn.innerHTML = '&#x2398;'; }, 1500);
            }).catch(function () {
                btn.textContent = '!';
                setTimeout(function () { btn.innerHTML = '&#x2398;'; }, 1500);
            });
        });
        div.appendChild(btn);
    }

    function appendMessage(role, content) {
        var div = document.createElement('div');
        div.className = 'message ' + role;
        if (role === 'assistant') {
            div.innerHTML = renderMarkdown(content);
            addCopyButton(div, content);
        } else {
            div.textContent = content;
        }
        chatArea.appendChild(div);
        scrollToBottom();
        return div;
    }

    function hideWelcome() {
        if (welcomeScreen) {
            welcomeScreen.style.display = 'none';
        }
    }

    function scrollToBottom() {
        chatArea.scrollTop = chatArea.scrollHeight;
    }

    // --- Streaming & Processing UI ---
    function handleStreamChunk(data) {
        if (!data) return;

        if (!streamElement) {
            // First chunk — create the streaming message element
            // (isStreaming and button swap already done in sendMessage)
            isStreaming = true;
            hideWelcome();
            streamBuffer = '';
            streamElement = document.createElement('div');
            streamElement.className = 'message assistant streaming';
            chatArea.appendChild(streamElement);
            sendBtn.classList.add('hidden');
            stopBtn.classList.remove('hidden');
        }

        if (data.token) {
            streamBuffer += data.token;
            streamElement.innerHTML = renderMarkdown(streamBuffer);
            scrollToBottom();
            updateProcessingLabel('Generating response...');
        }

        if (data.done) {
            endStream();
        }
    }

    function handleChatResponse(data) {
        if (isStreaming) {
            endStream();
        }
        // Handle both string data (BYOLLM pattern) and object data
        var content = '';
        if (typeof data === 'string') {
            content = data;
        } else if (data && data.content) {
            content = data.content;
        }
        if (content) {
            hideWelcome();
            appendMessage('assistant', content);
            chatHistory.push({ type: 'assistant', content: content });
        }
        sendBtn.disabled = false;
    }

    function endStream() {
        if (retryCountdownInterval) {
            clearInterval(retryCountdownInterval);
            retryCountdownInterval = null;
        }
        retryAttemptCount = 0;
        if (streamElement) {
            streamElement.classList.remove('streaming');
            streamElement.innerHTML = renderMarkdown(streamBuffer);
            if (streamBuffer) addCopyButton(streamElement, streamBuffer);
        }
        if (streamBuffer) {
            chatHistory.push({ type: 'assistant', content: streamBuffer });
        }
        isStreaming = false;
        streamBuffer = '';
        streamElement = null;
        sendBtn.disabled = false;
        sendBtn.classList.remove('hidden');
        stopBtn.classList.add('hidden');
        hideToolActivity();
        hideProcessingBar();
        autoSaveChatState();
    }

    function autoSaveChatState() {
        if (chatHistory.length === 0) return;
        sendToBackend('save_chat_state', { displayHistory: chatHistory });
    }

    // --- Processing Bar ---
    function showProcessingBar(label) {
        processingLabel.textContent = label || 'Processing...';
        processingBar.classList.remove('hidden');
    }

    function updateProcessingLabel(label) {
        processingLabel.textContent = label;
    }

    function hideProcessingBar() {
        processingBar.classList.add('hidden');
    }

    // --- Tool Activity ---
    function handleToolStart(data) {
        if (!data) return;
        var name = data.toolName || 'tool';
        var label = formatToolName(name);
        toolLabel.textContent = label;
        toolActivity.classList.remove('hidden');
        showProcessingBar(label);
        chatHistory.push({ type: 'tool_start', content: label, toolName: name });
    }

    function handleToolResult(data) {
        hideToolActivity();
        if (data && data.summary) {
            chatHistory.push({ type: 'tool_result', content: data.summary, toolName: data.toolName });
        }
    }

    function hideToolActivity() {
        toolActivity.classList.add('hidden');
    }

    function formatToolName(name) {
        var labels = {
            'get_modules': 'Reading modules...',
            'get_entities': 'Reading entities...',
            'get_entity_details': 'Reading entity details...',
            'get_microflows': 'Reading microflows...',
            'get_microflow_details': 'Reading microflow details...',
            'get_associations': 'Reading associations...',
            'get_enumerations': 'Reading enumerations...',
            'get_pages': 'Reading pages...',
            'search_model': 'Searching model...',
            'create_microflow': 'Creating microflow...',
            'add_activities_to_microflow': 'Adding activities...',
            'replace_microflow': 'Replacing microflow...',
            'rename_microflow': 'Renaming microflow...',
            'edit_microflow_activity': 'Editing activity...'
        };
        return labels[name] || ('Running ' + name + '...');
    }

    // --- Context ---
    function handleContextLoaded(data) {
        if (!data) return;
        contextDot.className = 'status-dot online';
        contextText.textContent = data.summary || 'Context loaded';
    }

    // --- Microflow Created ---
    function handleMicroflowCreated(data) {
        if (!data) return;
        var msg = 'Microflow "' + (data.name || '') + '" created successfully. Press F4 to refresh.';
        var div = document.createElement('div');
        div.className = 'success-msg';
        div.textContent = msg;
        chatArea.appendChild(div);
        scrollToBottom();
        chatHistory.push({ type: 'success', content: msg });
    }

    // --- Model Changed (prompt user to refresh context) ---
    function handleModelChanged(data) {
        contextDot.className = 'status-dot stale';
        contextText.textContent = 'Context outdated — click \u21BB to refresh';
    }

    // --- Token Usage ---
    function handleTokenUsage(data) {
        if (!data) return;
        var inputTokens = data.inputTokens || 0;
        var outputTokens = data.outputTokens || 0;
        var cacheCreation = data.cacheCreationTokens || 0;
        var cacheRead = data.cacheReadTokens || 0;
        cumulativeInputTokens += inputTokens;
        cumulativeOutputTokens += outputTokens;

        var total = inputTokens + outputTokens;

        function makeSpan(cls, text) {
            var s = document.createElement('span');
            s.className = cls;
            s.textContent = text;
            return s;
        }

        function makeSep() {
            return document.createTextNode(' \u00B7 ');
        }

        var div = document.createElement('div');
        div.className = 'token-usage';
        div.appendChild(makeSpan('token-label', 'Tokens:'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-in', formatTokens(inputTokens) + ' in'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-out', formatTokens(outputTokens) + ' out'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-total', formatTokens(total) + ' total'));

        if (cacheCreation > 0 || cacheRead > 0) {
            var cacheText = 'Cache: ' +
                (cacheRead > 0 ? formatTokens(cacheRead) + ' hit' : '') +
                (cacheRead > 0 && cacheCreation > 0 ? ', ' : '') +
                (cacheCreation > 0 ? formatTokens(cacheCreation) + ' written' : '');
            div.appendChild(makeSep());
            div.appendChild(makeSpan('token-cache', cacheText));
        }
        chatArea.appendChild(div);
        scrollToBottom();

        updateTokenBadge();

        if (data.contextUsedTokens && data.contextLimitTokens) {
            updateContextUsage(data.contextUsedTokens, data.contextLimitTokens);
        }

        chatHistory.push({ type: 'tokens', content: 'Tokens: ' + formatTokens(inputTokens) + ' in, ' + formatTokens(outputTokens) + ' out, ' + formatTokens(total) + ' total' });
    }

    function formatTokens(n) {
        if (n >= 1000) return (n / 1000).toFixed(1) + 'k';
        return '' + n;
    }

    function updateTokenBadge() {
        var badge = document.getElementById('tokenBadge');
        if (!badge) return;
        var total = cumulativeInputTokens + cumulativeOutputTokens;
        if (total > 0) {
            badge.textContent = formatTokens(total) + ' tokens';
            badge.classList.remove('hidden');
        } else {
            badge.classList.add('hidden');
        }
    }

    function updateContextUsage(used, limit) {
        if (!contextUsage || !contextUsageFill || !contextUsageLabel) return;
        if (!limit || limit <= 0) return;
        var pct = Math.min(Math.round((used / limit) * 100), 100);
        contextUsageFill.style.width = pct + '%';

        if (pct >= 80) {
            contextUsageFill.className = 'context-usage-fill danger';
        } else if (pct >= 50) {
            contextUsageFill.className = 'context-usage-fill warning';
        } else {
            contextUsageFill.className = 'context-usage-fill';
        }

        contextUsageLabel.textContent = pct + '% (' + formatTokens(used) + ' / ' + formatTokens(limit) + ')';
        contextUsage.classList.remove('hidden');
    }

    function resetContextUsage() {
        if (!contextUsage) return;
        contextUsage.classList.add('hidden');
        if (contextUsageFill) {
            contextUsageFill.style.width = '0%';
            contextUsageFill.className = 'context-usage-fill';
        }
        if (contextUsageLabel) contextUsageLabel.textContent = '0%';
    }

    // --- Retry Wait (rate limit / overload automatic retry) ---
    var retryCountdownInterval = null;
    var retryAttemptCount = 0;

    function handleRetryWait(data) {
        if (!data) return;
        retryAttemptCount++;
        var attempt = retryAttemptCount;
        var totalSec = data.delaySec || 15;
        var maxRetries = data.maxRetries || 20;
        var remaining = totalSec;

        if (retryCountdownInterval) clearInterval(retryCountdownInterval);

        showProcessingBar('Rate limited — retrying in ' + remaining + 's (attempt ' + attempt + '/' + maxRetries + ')');

        retryCountdownInterval = setInterval(function () {
            remaining--;
            if (remaining <= 0) {
                clearInterval(retryCountdownInterval);
                retryCountdownInterval = null;
                showProcessingBar('Retrying...');
            } else {
                updateProcessingLabel('Rate limited — retrying in ' + remaining + 's (attempt ' + attempt + '/' + maxRetries + ')');
            }
        }, 1000);
    }

    // --- Error ---
    function handleError(data) {
        if (!data) return;
        endStream();
        var msg = data.message || 'An error occurred.';
        var div = document.createElement('div');
        div.className = 'error-msg';
        div.textContent = msg;
        chatArea.appendChild(div);
        scrollToBottom();
        sendBtn.disabled = false;
        chatHistory.push({ type: 'error', content: msg });
    }

    // --- History ---
    function openHistory() {
        historyModal.classList.remove('hidden');
        sendToBackend('get_history');
    }

    function closeHistory() {
        historyModal.classList.add('hidden');
    }

    function handleHistoryList(data) {
        if (!data || !data.conversations) return;
        var conversations = data.conversations;

        historyList.textContent = '';

        if (conversations.length === 0) {
            var emptyP = document.createElement('p');
            emptyP.className = 'history-empty';
            emptyP.textContent = 'No saved conversations yet.';
            historyList.appendChild(emptyP);
            return;
        }

        for (var i = 0; i < conversations.length; i++) {
            var conv = conversations[i];
            var date = new Date(conv.updatedAt).toLocaleDateString(undefined, {
                month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
            });
            var title = conv.title || 'Untitled';
            if (title.length > 70) title = title.substring(0, 70) + '...';
            var msgCount = conv.messageCount || 0;

            var item = document.createElement('div');
            item.className = 'history-item';
            item.dataset.id = conv.id;

            var main = document.createElement('div');
            main.className = 'history-item-main';

            var titleDiv = document.createElement('div');
            titleDiv.className = 'history-item-title';
            titleDiv.textContent = title;

            var metaDiv = document.createElement('div');
            metaDiv.className = 'history-item-meta';
            metaDiv.textContent = date + ' \u00B7 ' + msgCount + ' messages';

            main.appendChild(titleDiv);
            main.appendChild(metaDiv);

            var delBtn = document.createElement('button');
            delBtn.className = 'history-delete-btn';
            delBtn.dataset.id = conv.id;
            delBtn.title = 'Delete';
            delBtn.innerHTML = '&#x2715;';

            item.appendChild(main);
            item.appendChild(delBtn);
            historyList.appendChild(item);

            main.addEventListener('click', (function (convId) {
                return function () {
                    closeHistory();
                    sendToBackend('load_conversation', { id: convId });
                };
            })(conv.id));

            delBtn.addEventListener('click', (function (convId) {
                return function (e) {
                    e.stopPropagation();
                    var el = this.closest('.history-item');
                    if (el) el.remove();
                    sendToBackend('delete_conversation', { id: convId });
                };
            })(conv.id));
        }
    }

    function handleConversationLoaded(data) {
        if (!data || !data.displayHistory) return;

        if (isStreaming) {
            sendToBackend('cancel');
            endStream();
        }

        chatArea.innerHTML = '';
        chatHistory = [];
        cumulativeInputTokens = 0;
        cumulativeOutputTokens = 0;
        sendBtn.disabled = false;
        resetContextUsage();

        var history = data.displayHistory;
        for (var i = 0; i < history.length; i++) {
            var entry = history[i];
            chatHistory.push(entry);

            switch (entry.type) {
                case 'user':
                    appendMessage('user', entry.content);
                    break;
                case 'assistant':
                    appendMessage('assistant', entry.content);
                    break;
                case 'tool_start':
                    break;
                case 'tool_result':
                    break;
                case 'success':
                    var sdiv = document.createElement('div');
                    sdiv.className = 'success-msg';
                    sdiv.textContent = entry.content;
                    chatArea.appendChild(sdiv);
                    break;
                case 'error':
                    var ediv = document.createElement('div');
                    ediv.className = 'error-msg';
                    ediv.textContent = entry.content;
                    chatArea.appendChild(ediv);
                    break;
                case 'tokens':
                    var tdiv = document.createElement('div');
                    tdiv.className = 'token-usage';
                    tdiv.textContent = entry.content;
                    chatArea.appendChild(tdiv);
                    break;
            }
        }

        scrollToBottom();
        updateTokenBadge();
    }

    // --- Settings ---
    function openSettings() {
        settingsModal.classList.remove('hidden');
        sendToBackend('get_settings');
    }

    function closeSettings() {
        settingsModal.classList.add('hidden');
    }

    function handleLoadSettings(data) {
        if (!data) return;
        if (data.selectedModel) {
            document.getElementById('modelSelect').value = data.selectedModel;
            updateModelBadge(data.selectedModel);
        }
        if (data.contextDepth) document.getElementById('contextDepthSelect').value = data.contextDepth;
        if (data.maxTokens) document.getElementById('maxTokensInput').value = data.maxTokens;
        if (data.retryMaxAttempts != null) document.getElementById('retryMaxAttemptsInput').value = data.retryMaxAttempts;
        if (data.retryDelaySeconds != null) document.getElementById('retryDelaySecondsInput').value = data.retryDelaySeconds;
        if (data.maxToolRounds != null) document.getElementById('maxToolRoundsInput').value = data.maxToolRounds;
        if (data.promptCachingEnabled != null) document.getElementById('promptCachingCheckbox').checked = data.promptCachingEnabled;
        if (data.hasKey) document.getElementById('apiKeyInput').placeholder = '********** (key saved)';
        if (data.theme) applyTheme(data.theme);
    }

    function handleSettingsSaved(data) {
        document.getElementById('apiKeyInput').value = '';
        closeSettings();
    }

    // --- Consent & Privacy ---
    function handleConsentRequired() {
        // Stop the processing UI since the message is queued pending consent
        isStreaming = false;
        sendBtn.disabled = false;
        sendBtn.classList.remove('hidden');
        stopBtn.classList.add('hidden');
        hideProcessingBar();
        if (consentModal) consentModal.classList.remove('hidden');
    }

    function handleConsentSaved() {
        if (consentModal) consentModal.classList.add('hidden');
    }

    function openPrivacy() {
        if (privacyModal) privacyModal.classList.remove('hidden');
    }

    function closePrivacy() {
        if (privacyModal) privacyModal.classList.add('hidden');
    }

    function saveSettings() {
        var apiKey = document.getElementById('apiKeyInput').value;
        var model = document.getElementById('modelSelect').value;
        var depth = document.getElementById('contextDepthSelect').value;
        var tokens = parseInt(document.getElementById('maxTokensInput').value) || 8192;
        var theme = themeSelect ? themeSelect.value : currentTheme;

        var retryMaxAttemptsVal = parseInt(document.getElementById('retryMaxAttemptsInput').value);
        var retryMaxAttempts = isNaN(retryMaxAttemptsVal) ? 20 : retryMaxAttemptsVal;
        var retryDelaySeconds = parseInt(document.getElementById('retryDelaySecondsInput').value) || 60;
        var maxToolRounds = parseInt(document.getElementById('maxToolRoundsInput').value) || 10;
        var promptCachingEnabled = document.getElementById('promptCachingCheckbox').checked;

        sendToBackend('save_settings', {
            apiKey: apiKey,
            selectedModel: model,
            contextDepth: depth,
            maxTokens: tokens,
            retryMaxAttempts: retryMaxAttempts,
            retryDelaySeconds: retryDelaySeconds,
            maxToolRounds: maxToolRounds,
            promptCachingEnabled: promptCachingEnabled,
            theme: theme
        });

        updateModelBadge(model);
        applyTheme(theme);
    }

    function updateModelBadge(model) {
        var labels = {
            'claude-sonnet-4-5-20250929': 'Sonnet 4.5',
            'claude-sonnet-4-6': 'Sonnet 4.6',
            'claude-opus-4-6': 'Opus 4.6',
            'claude-haiku-4-5-20251001': 'Haiku 4.5'
        };
        modelBadge.textContent = labels[model] || model;
    }

    // --- Theme ---
    function applyTheme(theme) {
        currentTheme = theme === 'dark' ? 'dark' : 'light';
        document.body.classList.toggle('dark', currentTheme === 'dark');
        if (themeToggleBtn) {
            themeToggleBtn.innerHTML = currentTheme === 'dark' ? '&#x2600;' : '&#x1F319;';
            themeToggleBtn.title = currentTheme === 'dark' ? 'Switch to Light Mode' : 'Switch to Dark Mode';
        }
        if (themeSelect) {
            themeSelect.value = currentTheme;
        }
    }

    // --- Export Chat ---
    function openExport() {
        if (chatHistory.length === 0) return;
        exportModal.classList.remove('hidden');
    }

    function closeExport() {
        exportModal.classList.add('hidden');
    }

    function exportChat() {
        var includeTools = exportToolActivity.checked;
        var modelName = modelBadge.textContent || 'Claude';
        var now = new Date();
        var dateStr = now.toISOString().slice(0, 10);
        var timeStr = now.toTimeString().slice(0, 5);

        var lines = [];
        lines.push('# AIDE Lite Chat Export');
        lines.push('**Date:** ' + dateStr + ' ' + timeStr + ' | **Model:** ' + modelName);
        lines.push('');
        lines.push('---');
        lines.push('');

        for (var i = 0; i < chatHistory.length; i++) {
            var entry = chatHistory[i];
            switch (entry.type) {
                case 'user':
                    lines.push('## User');
                    lines.push(entry.content);
                    lines.push('');
                    break;
                case 'assistant':
                    lines.push('## Assistant');
                    lines.push(entry.content);
                    lines.push('');
                    break;
                case 'tool_start':
                    if (includeTools) {
                        lines.push('> **Tool:** ' + entry.content);
                    }
                    break;
                case 'tool_result':
                    if (includeTools) {
                        lines.push('> **Result:** ' + entry.content);
                        lines.push('');
                    }
                    break;
                case 'success':
                    lines.push('> **' + entry.content + '**');
                    lines.push('');
                    break;
                case 'error':
                    lines.push('> **Error:** ' + entry.content);
                    lines.push('');
                    break;
                case 'tokens':
                    lines.push('*' + entry.content + '*');
                    lines.push('');
                    break;
            }
        }

        lines.push('---');
        lines.push('*Exported from AIDE Lite v1.1.1*');

        var markdown = lines.join('\n');
        var blob = new Blob([markdown], { type: 'text/markdown;charset=utf-8' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = 'aide-lite-chat-' + dateStr + '.md';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        closeExport();
    }

    // --- Markdown Rendering ---
    // Lightweight regex-based Markdown-to-HTML converter. Handles code blocks,
    // inline formatting, headers, lists, tables, blockquotes, and links.
    // All text content is HTML-escaped before being wrapped in HTML tags to prevent XSS.
    function renderMarkdown(text) {
        if (!text) return '';

        // Phase 1: Extract code blocks and inline code into placeholders so they survive escaping
        var codeBlocks = [];
        var html = text;

        html = html.replace(/```(\w*)\n([\s\S]*?)```/g, function (m, lang, code) {
            var idx = codeBlocks.length;
            codeBlocks.push('<pre><code class="language-' + (lang || '') + '">' + escapeHtml(code) + '</code></pre>');
            return '\x00CODEBLOCK' + idx + '\x00';
        });

        html = html.replace(/`([^`]+)`/g, function (m, code) {
            var idx = codeBlocks.length;
            codeBlocks.push('<code>' + escapeHtml(code) + '</code>');
            return '\x00CODEBLOCK' + idx + '\x00';
        });

        // Phase 2: Escape all remaining text to prevent XSS
        html = escapeHtml(html);

        // Phase 3: Restore code block placeholders
        html = html.replace(/\x00CODEBLOCK(\d+)\x00/g, function (m, idx) {
            return codeBlocks[parseInt(idx)] || '';
        });

        // Phase 4: Apply markdown formatting on escaped text (safe — all user content is escaped)

        // Bold
        html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

        // Italic
        html = html.replace(/\*([^*]+)\*/g, '<em>$1</em>');

        // Headers
        html = html.replace(/^### (.+)$/gm, '<h3>$1</h3>');
        html = html.replace(/^## (.+)$/gm, '<h2>$1</h2>');
        html = html.replace(/^# (.+)$/gm, '<h1>$1</h1>');

        // Blockquotes
        html = html.replace(/^&gt; (.+)$/gm, '<blockquote>$1</blockquote>');

        // Links [text](url) — only allow http/https URLs
        html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');

        // Unordered lists
        html = html.replace(/^- (.+)$/gm, '<li class="ul-item">$1</li>');
        html = html.replace(/((?:<li class="ul-item">[\s\S]*?<\/li>\s*)+)/g, '<ul>$1</ul>');
        html = html.replace(/<\/ul>\s*<ul>/g, '');

        // Ordered lists
        html = html.replace(/^\d+\. (.+)$/gm, '<li class="ol-item">$1</li>');
        html = html.replace(/((?:<li class="ol-item">[\s\S]*?<\/li>\s*)+)/g, '<ol>$1</ol>');
        html = html.replace(/<\/ol>\s*<ol>/g, '');

        // Tables
        var tableIsHeader = true;
        html = html.replace(/^\|(.+)\|$/gm, function (match, content) {
            var cells = content.split('|').map(function (c) { return c.trim(); });
            if (cells.every(function (c) { return /^[-:]+$/.test(c); })) {
                tableIsHeader = false;
                return '<!-- table separator -->';
            }
            var tag = tableIsHeader ? 'th' : 'td';
            var row = cells.map(function (c) { return '<' + tag + '>' + c + '</' + tag + '>'; }).join('');
            return '<tr>' + row + '</tr>';
        });
        html = html.replace(/((<tr>[\s\S]*?<\/tr>\s*)+)/g, function (m) {
            tableIsHeader = true;
            return '<table>' + m + '</table>';
        });
        html = html.replace(/<!-- table separator -->\s*/g, '');

        // Paragraphs
        html = html.replace(/\n\n/g, '</p><p>');
        html = '<p>' + html + '</p>';
        html = html.replace(/<p>\s*<\/p>/g, '');
        html = html.replace(/<p>\s*(<h[1-3]>)/g, '$1');
        html = html.replace(/(<\/h[1-3]>)\s*<\/p>/g, '$1');
        html = html.replace(/<p>\s*(<pre>)/g, '$1');
        html = html.replace(/(<\/pre>)\s*<\/p>/g, '$1');
        html = html.replace(/<p>\s*(<ul>)/g, '$1');
        html = html.replace(/(<\/ul>)\s*<\/p>/g, '$1');
        html = html.replace(/<p>\s*(<ol>)/g, '$1');
        html = html.replace(/(<\/ol>)\s*<\/p>/g, '$1');
        html = html.replace(/<p>\s*(<table>)/g, '$1');
        html = html.replace(/(<\/table>)\s*<\/p>/g, '$1');
        html = html.replace(/<p>\s*(<blockquote>)/g, '$1');
        html = html.replace(/(<\/blockquote>)\s*<\/p>/g, '$1');

        // Line breaks
        html = html.replace(/\n/g, '<br>');

        return html;
    }

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // --- Event Listeners & UI Wiring ---
    modeSelect.addEventListener('change', function () {
        chatInput.placeholder = modeSelect.value === 'ask'
            ? 'Ask mode \u2014 read-only, no changes to your app...'
            : 'Agent mode \u2014 can read and modify your app...';
    });

    sendBtn.addEventListener('click', sendMessage);

    stopBtn.addEventListener('click', function () {
        sendToBackend('cancel');
        endStream();
    });

    chatInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    // Auto-resize textarea
    chatInput.addEventListener('input', function () {
        this.style.height = 'auto';
        this.style.height = Math.min(this.scrollHeight, 120) + 'px';
    });

    refreshBtn.addEventListener('click', function () {
        contextDot.className = 'status-dot loading';
        contextText.textContent = 'Loading context...';
        sendToBackend('get_context');
    });

    newChatBtn.addEventListener('click', function () {
        // Cancel any in-progress streaming and reset state
        if (isStreaming) {
            sendToBackend('cancel');
            endStream();
        }
        chatArea.innerHTML = '';
        if (welcomeScreen) {
            chatArea.appendChild(welcomeScreen);
            welcomeScreen.style.display = '';
        }
        sendBtn.disabled = false;
        cumulativeInputTokens = 0;
        cumulativeOutputTokens = 0;
        chatHistory = [];
        updateTokenBadge();
        resetContextUsage();
        sendToBackend('new_chat');
    });

    settingsBtn.addEventListener('click', openSettings);
    saveSettingsBtn.addEventListener('click', saveSettings);
    cancelSettingsBtn.addEventListener('click', closeSettings);

    // Theme toggle button (header quick-toggle)
    if (themeToggleBtn) {
        themeToggleBtn.addEventListener('click', function () {
            var newTheme = currentTheme === 'dark' ? 'light' : 'dark';
            applyTheme(newTheme);
            // Persist theme-only save (null guards in SaveConfig protect other settings)
            sendToBackend('save_settings', { theme: newTheme });
        });
    }

    // Live preview: apply theme immediately when dropdown changes in settings modal
    if (themeSelect) {
        themeSelect.addEventListener('change', function () {
            applyTheme(this.value);
        });
    }

    // History
    if (historyBtn) historyBtn.addEventListener('click', openHistory);
    if (historyCloseBtn) historyCloseBtn.addEventListener('click', closeHistory);
    if (historyOverlay) historyOverlay.addEventListener('click', closeHistory);

    // Export chat
    if (exportBtn) exportBtn.addEventListener('click', openExport);
    if (exportDownloadBtn) exportDownloadBtn.addEventListener('click', exportChat);
    if (exportCancelBtn) exportCancelBtn.addEventListener('click', closeExport);
    if (exportOverlay) exportOverlay.addEventListener('click', closeExport);

    // Consent
    if (consentAcceptBtn) consentAcceptBtn.addEventListener('click', function () {
        sendToBackend('consent_accepted');
        if (consentModal) consentModal.classList.add('hidden');
    });
    if (consentDeclineBtn) consentDeclineBtn.addEventListener('click', function () {
        if (consentModal) consentModal.classList.add('hidden');
    });

    // Privacy
    if (privacyBtn) privacyBtn.addEventListener('click', openPrivacy);
    if (privacyCloseBtn) privacyCloseBtn.addEventListener('click', closePrivacy);
    if (privacyOverlay) privacyOverlay.addEventListener('click', closePrivacy);

    // Close settings modal on overlay click
    document.querySelector('#settingsModal .modal-overlay')?.addEventListener('click', closeSettings);

    // Quick actions
    document.querySelectorAll('.quick-action').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var prompt = this.getAttribute('data-prompt');
            if (prompt) {
                chatInput.value = prompt;
                sendMessage();
            }
        });
    });

    // --- Initialize WebView Message Bridge ---
    // IMPORTANT: Per Mendix WebView API, the JS side MUST post 'MessageListenerRegistered'
    // after attaching its message listener. Without this handshake, all C# PostMessage
    // calls are silently queued and never delivered to JS.
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', handleMessage);
        sendToBackend('MessageListenerRegistered');
    }

    // Request settings on load (will be delivered after MessageListenerRegistered flushes the queue)
    sendToBackend('get_settings');
})();
