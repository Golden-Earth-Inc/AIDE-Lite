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
    var pendingImages = [];

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
    var inputArea = document.getElementById('inputArea');
    var imagePreview = document.getElementById('imagePreview');
    var attachImageBtn = document.getElementById('attachImageBtn');
    var imageFileInput = document.getElementById('imageFileInput');

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
        }
    }

    // --- Chat Message Handling ---
    function sendMessage() {
        var text = chatInput.value.trim();
        if (!text || isStreaming) return;

        var images = pendingImages.slice();
        hideWelcome();
        appendMessage('user', text, images);
        chatHistory.push({ type: 'user', content: text, images: images });
        chatInput.value = '';
        chatInput.style.height = 'auto';
        pendingImages = [];
        renderImagePreviews();

        isStreaming = true;
        retryAttemptCount = 0;
        sendBtn.disabled = true;
        sendBtn.classList.add('hidden');
        stopBtn.classList.remove('hidden');
        showProcessingBar('Thinking...');

        var payload = { message: text, mode: modeSelect.value };
        if (images.length > 0) {
            payload.images = images.map(function (img) {
                return { base64: img.base64, mediaType: img.mediaType };
            });
        }
        sendToBackend('chat', payload);
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
            });
        });
        div.appendChild(btn);
    }

    function appendMessage(role, content, images) {
        var div = document.createElement('div');
        div.className = 'message ' + role;
        if (role === 'assistant') {
            div.innerHTML = renderMarkdown(content);
            addCopyButton(div, content);
        } else {
            if (images && images.length > 0) {
                var imgContainer = document.createElement('div');
                imgContainer.className = 'image-attachments';
                images.forEach(function (img) {
                    var imgEl = document.createElement('img');
                    imgEl.src = img.dataUrl || ('data:' + img.mediaType + ';base64,' + img.base64);
                    imgEl.alt = img.name || 'Attached image';
                    imgEl.title = img.name || 'Attached image';
                    imgContainer.appendChild(imgEl);
                });
                div.appendChild(imgContainer);
            }
            var textNode = document.createElement('span');
            textNode.textContent = content;
            div.appendChild(textNode);
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
        var parts = [
            '<span class="token-label">Tokens:</span>',
            '<span class="token-in">' + formatTokens(inputTokens) + ' in</span>',
            '<span class="token-out">' + formatTokens(outputTokens) + ' out</span>',
            '<span class="token-total">' + formatTokens(total) + ' total</span>'
        ];

        if (cacheCreation > 0 || cacheRead > 0) {
            parts.push('<span class="token-cache">Cache: ' +
                (cacheRead > 0 ? formatTokens(cacheRead) + ' hit' : '') +
                (cacheRead > 0 && cacheCreation > 0 ? ', ' : '') +
                (cacheCreation > 0 ? formatTokens(cacheCreation) + ' written' : '') +
                '</span>');
        }

        var div = document.createElement('div');
        div.className = 'token-usage';
        div.innerHTML = parts.join(' · ');
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

    var autoLoadPending = false;

    function handleHistoryList(data) {
        if (!data || !data.conversations) return;
        var conversations = data.conversations;

        if (autoLoadPending) {
            autoLoadPending = false;
            if (conversations.length > 0) {
                sendToBackend('load_conversation', { id: conversations[0].id });
            }
            return;
        }

        if (conversations.length === 0) {
            historyList.innerHTML = '<p class="history-empty">No saved conversations yet.</p>';
            return;
        }

        var html = '';
        for (var i = 0; i < conversations.length; i++) {
            var conv = conversations[i];
            var date = new Date(conv.updatedAt).toLocaleDateString(undefined, {
                month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
            });
            var title = conv.title || 'Untitled';
            if (title.length > 70) title = title.substring(0, 70) + '...';
            var msgCount = conv.messageCount || 0;

            html += '<div class="history-item" data-id="' + escapeHtml(conv.id) + '">';
            html += '  <div class="history-item-main">';
            html += '    <div class="history-item-title">' + escapeHtml(title) + '</div>';
            html += '    <div class="history-item-meta">' + date + ' · ' + msgCount + ' messages</div>';
            html += '  </div>';
            html += '  <button class="history-delete-btn" data-id="' + escapeHtml(conv.id) + '" title="Delete">&#x2715;</button>';
            html += '</div>';
        }
        historyList.innerHTML = html;

        historyList.querySelectorAll('.history-item-main').forEach(function (el) {
            el.addEventListener('click', function () {
                var id = this.parentElement.getAttribute('data-id');
                if (id) {
                    closeHistory();
                    sendToBackend('load_conversation', { id: id });
                }
            });
        });

        historyList.querySelectorAll('.history-delete-btn').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                var id = this.getAttribute('data-id');
                if (id) {
                    sendToBackend('delete_conversation', { id: id });
                }
            });
        });
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
                    appendMessage('user', entry.content, entry.images);
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

    var initialSettingsLoaded = false;

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
        if (data.autoRefreshContext != null) document.getElementById('autoRefreshContextCheckbox').checked = data.autoRefreshContext;
        if (data.autoLoadLastConversation != null) document.getElementById('autoLoadLastConversationCheckbox').checked = data.autoLoadLastConversation;
        if (data.hasKey) document.getElementById('apiKeyInput').placeholder = '********** (key saved)';
        if (data.theme) applyTheme(data.theme);

        if (!initialSettingsLoaded) {
            initialSettingsLoaded = true;
            if (data.autoRefreshContext !== false) {
                contextDot.className = 'status-dot loading';
                contextText.textContent = 'Loading context...';
                sendToBackend('get_context');
            }
            if (data.autoLoadLastConversation !== false) {
                autoLoadPending = true;
                sendToBackend('get_history');
            }
        }
    }

    function handleSettingsSaved(data) {
        closeSettings();
    }

    function saveSettings() {
        var apiKey = document.getElementById('apiKeyInput').value;
        var model = document.getElementById('modelSelect').value;
        var depth = document.getElementById('contextDepthSelect').value;
        var tokens = parseInt(document.getElementById('maxTokensInput').value) || 8192;
        var theme = themeSelect ? themeSelect.value : currentTheme;

        var retryMaxAttempts = parseInt(document.getElementById('retryMaxAttemptsInput').value) || 20;
        var retryDelaySeconds = parseInt(document.getElementById('retryDelaySecondsInput').value) || 60;
        var maxToolRounds = parseInt(document.getElementById('maxToolRoundsInput').value) || 10;
        var promptCachingEnabled = document.getElementById('promptCachingCheckbox').checked;
        var autoRefreshContext = document.getElementById('autoRefreshContextCheckbox').checked;
        var autoLoadLastConversation = document.getElementById('autoLoadLastConversationCheckbox').checked;

        sendToBackend('save_settings', {
            apiKey: apiKey,
            selectedModel: model,
            contextDepth: depth,
            maxTokens: tokens,
            retryMaxAttempts: retryMaxAttempts,
            retryDelaySeconds: retryDelaySeconds,
            maxToolRounds: maxToolRounds,
            promptCachingEnabled: promptCachingEnabled,
            autoRefreshContext: autoRefreshContext,
            autoLoadLastConversation: autoLoadLastConversation,
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
        lines.push('*Exported from AIDE Lite v1.1.0*');

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
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // --- Image Attachment Handling ---
    var allowedImageTypes = { 'image/jpeg': true, 'image/png': true, 'image/gif': true, 'image/webp': true };

    function addImageFile(file) {
        if (!allowedImageTypes[file.type]) return;
        if (file.size > 20 * 1024 * 1024) return; // 20 MB limit
        var reader = new FileReader();
        reader.onload = function (e) {
            var dataUrl = e.target.result;
            var commaIdx = dataUrl.indexOf(',');
            var base64 = dataUrl.substring(commaIdx + 1);
            var mediaType = file.type;
            pendingImages.push({ base64: base64, mediaType: mediaType, name: file.name, dataUrl: dataUrl });
            renderImagePreviews();
        };
        reader.readAsDataURL(file);
    }

    function renderImagePreviews() {
        imagePreview.innerHTML = '';
        if (pendingImages.length === 0) {
            imagePreview.classList.remove('has-images');
            return;
        }
        imagePreview.classList.add('has-images');
        pendingImages.forEach(function (img, idx) {
            var item = document.createElement('div');
            item.className = 'image-preview-item';

            var thumb = document.createElement('img');
            thumb.className = 'image-preview-thumb';
            thumb.src = img.dataUrl;
            thumb.alt = img.name || 'image';
            thumb.title = img.name || 'Attached image';

            var removeBtn = document.createElement('button');
            removeBtn.className = 'image-preview-remove';
            removeBtn.innerHTML = '\u00D7';
            removeBtn.title = 'Remove image';
            removeBtn.setAttribute('data-idx', idx);
            removeBtn.addEventListener('click', function () {
                var i = parseInt(this.getAttribute('data-idx'), 10);
                pendingImages.splice(i, 1);
                renderImagePreviews();
            });

            item.appendChild(thumb);
            item.appendChild(removeBtn);
            imagePreview.appendChild(item);
        });
    }

    // Block WebView2 from navigating to dropped files globally —
    // without this, the browser tries to open the file URL before JS can handle it
    document.addEventListener('dragover', function (e) {
        e.preventDefault();
        e.stopPropagation();
    });
    document.addEventListener('drop', function (e) {
        e.preventDefault();
        e.stopPropagation();
    });

    // Drag-and-drop on input area (visual feedback + file capture)
    inputArea.addEventListener('dragover', function (e) {
        e.preventDefault();
        e.stopPropagation();
        inputArea.classList.add('drag-over');
    });
    inputArea.addEventListener('dragleave', function (e) {
        e.preventDefault();
        e.stopPropagation();
        inputArea.classList.remove('drag-over');
    });
    inputArea.addEventListener('drop', function (e) {
        e.preventDefault();
        e.stopPropagation();
        inputArea.classList.remove('drag-over');
        var files = e.dataTransfer && e.dataTransfer.files;
        if (files) {
            for (var i = 0; i < files.length; i++) {
                addImageFile(files[i]);
            }
        }
    });

    // Paste images from clipboard
    chatInput.addEventListener('paste', function (e) {
        var items = e.clipboardData && e.clipboardData.items;
        if (!items) return;
        for (var i = 0; i < items.length; i++) {
            if (items[i].kind === 'file' && allowedImageTypes[items[i].type]) {
                var file = items[i].getAsFile();
                if (file) addImageFile(file);
            }
        }
    });

    // Attach image via file picker button
    attachImageBtn.addEventListener('click', function () {
        imageFileInput.value = '';
        imageFileInput.click();
    });
    imageFileInput.addEventListener('change', function () {
        var files = imageFileInput.files;
        if (files) {
            for (var i = 0; i < files.length; i++) {
                addImageFile(files[i]);
            }
        }
        imageFileInput.value = '';
    });

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
        pendingImages = [];
        renderImagePreviews();
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
