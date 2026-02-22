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

    // --- Constants ---
    const MAX_IMAGE_SIZE = 20 * 1024 * 1024; // 20 MB
    const MAX_FILE_SIZE = 500 * 1024; // 500 KB
    const MAX_TEXTAREA_HEIGHT = 120;
    const ALLOWED_IMAGE_TYPES = { 'image/jpeg': true, 'image/png': true, 'image/gif': true, 'image/webp': true };
    const ALLOWED_FILE_EXTENSIONS = {
        '.json': 'json', '.xml': 'xml', '.yaml': 'yaml', '.yml': 'yaml',
        '.java': 'java', '.js': 'javascript', '.css': 'css', '.html': 'html',
        '.md': 'markdown', '.txt': 'text', '.log': 'log', '.csv': 'csv',
        '.sql': 'sql', '.properties': 'properties'
    };

    // --- State ---
    let isStreaming = false;
    let streamBuffer = '';
    let streamElement = null;
    let cumulativeInputTokens = 0;
    let cumulativeOutputTokens = 0;
    let currentTheme = 'light';
    let chatHistory = [];
    let pendingImages = [];
    let pendingFiles = [];
    let pendingDocuments = [];
    let retryCountdownInterval = null;
    let retryAttemptCount = 0;
    let autoLoadPending = false;
    let initialSettingsLoaded = false;
    let skipAutoLoadConversation = false;
    let activeDocument = null;

    // --- DOM References ---
    const chatArea = document.getElementById('chatArea');
    const chatInput = document.getElementById('chatInput');
    const modeSelect = document.getElementById('modeSelect');
    const sendBtn = document.getElementById('sendBtn');
    const refreshBtn = document.getElementById('refreshBtn');
    const newChatBtn = document.getElementById('newChatBtn');
    const settingsBtn = document.getElementById('settingsBtn');
    const settingsModal = document.getElementById('settingsModal');
    const saveSettingsBtn = document.getElementById('saveSettingsBtn');
    const cancelSettingsBtn = document.getElementById('cancelSettingsBtn');
    const toolActivity = document.getElementById('toolActivity');
    const toolLabel = document.getElementById('toolLabel');
    const stopBtn = document.getElementById('stopBtn');
    const contextDot = document.getElementById('contextDot');
    const contextText = document.getElementById('contextText');
    const modelBadge = document.getElementById('modelBadge');
    const welcomeScreen = document.getElementById('welcomeScreen');
    const processingBar = document.getElementById('processingBar');
    const processingLabel = document.getElementById('processingLabel');
    const themeToggleBtn = document.getElementById('themeToggleBtn');
    const themeSelect = document.getElementById('themeSelect');
    const historyBtn = document.getElementById('historyBtn');
    const historyModal = document.getElementById('historyModal');
    const historyList = document.getElementById('historyList');
    const historyCloseBtn = document.getElementById('historyCloseBtn');
    const historyOverlay = document.getElementById('historyOverlay');
    const contextUsage = document.getElementById('contextUsage');
    const contextUsageFill = document.getElementById('contextUsageFill');
    const contextUsageLabel = document.getElementById('contextUsageLabel');
    const exportBtn = document.getElementById('exportBtn');
    const exportModal = document.getElementById('exportModal');
    const exportDownloadBtn = document.getElementById('exportDownloadBtn');
    const exportCancelBtn = document.getElementById('exportCancelBtn');
    const exportOverlay = document.getElementById('exportOverlay');
    const exportToolActivity = document.getElementById('exportToolActivity');
    const inputArea = document.getElementById('inputArea');
    const filePreview = document.getElementById('filePreview');
    const imagePreview = document.getElementById('imagePreview');
    const attachBtn = document.getElementById('attachBtn');
    const attachFileInput = document.getElementById('attachFileInput');
    const helpBtn = document.getElementById('helpBtn');
    const helpModal = document.getElementById('helpModal');
    const helpCloseBtn = document.getElementById('helpCloseBtn');
    const helpOverlay = document.getElementById('helpOverlay');
    const activeDocBar = document.getElementById('activeDocBar');
    const activeDocIcon = document.getElementById('activeDocIcon');
    const activeDocText = document.getElementById('activeDocText');
    const activeDocAddBtn = document.getElementById('activeDocAddBtn');
    const consentModal = document.getElementById('consentModal');
    const consentAcceptBtn = document.getElementById('consentAcceptBtn');
    const consentDeclineBtn = document.getElementById('consentDeclineBtn');
    const privacyBtn = document.getElementById('privacyBtn');
    const privacyModal = document.getElementById('privacyModal');
    const privacyCloseBtn = document.getElementById('privacyCloseBtn');
    const privacyOverlay = document.getElementById('privacyOverlay');

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
        const envelope = event.data;
        if (!envelope) return;

        const type = envelope.message;
        const data = envelope.data;

        if (type) {
            handleBackendMessage(type, data);
        }
    }

    function handleBackendMessage(type, data) {
        switch (type) {
            case 'chat_streaming':
                handleStreamChunk(data);
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
            case 'skip_auto_load':
                skipAutoLoadConversation = true;
                autoLoadPending = false;
                break;
            case 'active_document_changed':
                handleActiveDocumentChanged(data);
                break;
            case 'auto_explain':
                handleAutoExplain(data);
                break;
            case 'document_referenced':
                handleDocumentReferenced(data);
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
        const text = chatInput.value.trim();
        if (!text || isStreaming) return;

        const images = pendingImages.slice();
        const docs = pendingDocuments.slice();
        const files = pendingFiles.slice();
        hideWelcome();
        appendMessage('user', text, images, docs, files);
        chatHistory.push({ type: 'user', content: text, images: images, documents: docs, files: files });
        chatInput.value = '';
        chatInput.style.height = 'auto';
        pendingImages = [];
        pendingFiles = [];
        pendingDocuments = [];
        renderImagePreviews();
        renderFilePreviews();
        renderDocumentPreviews();

        isStreaming = true;
        retryAttemptCount = 0;
        sendBtn.disabled = true;
        sendBtn.classList.add('hidden');
        stopBtn.classList.remove('hidden');
        showProcessingBar('Thinking...');

        const payload = { message: text, mode: modeSelect.value };
        if (images.length > 0) {
            payload.images = images.map(function (img) {
                return { base64: img.base64, mediaType: img.mediaType };
            });
        }
        if (docs.length > 0) {
            payload.documents = docs.map(function (d) {
                return { type: d.type, qualifiedName: d.qualifiedName };
            });
        }
        if (files.length > 0) {
            payload.files = files.map(function (f) {
                return { name: f.name, language: f.language, content: f.content };
            });
        }
        if (activeDocument) {
            payload.activeDocument = {
                type: activeDocument.type,
                qualifiedName: activeDocument.qualifiedName
            };
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
        const btn = document.createElement('button');
        btn.className = 'copy-btn';
        btn.title = 'Copy (rich text + markdown)';
        btn.innerHTML = '&#x2398;';
        btn.addEventListener('click', function () {
            const md = div.dataset.md;
            const clone = div.cloneNode(true);
            const cloneBtn = clone.querySelector('.copy-btn');
            if (cloneBtn) cloneBtn.remove();
            inlineStylesForCopy(clone);

            const styledHtml = '<div style="font-family:system-ui,-apple-system,sans-serif;font-size:13px;line-height:1.5;color:#1a1a2e;">' + clone.innerHTML + '</div>';
            const htmlBlob = new Blob([styledHtml], { type: 'text/html' });
            const textBlob = new Blob([md], { type: 'text/plain' });

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

    function appendMessage(role, content, images, documents, files) {
        const div = document.createElement('div');
        div.className = 'message ' + role;
        if (role === 'assistant') {
            div.innerHTML = renderMarkdown(content);
            addCopyButton(div, content);
        } else {
            if (documents && documents.length > 0) {
                const docContainer = document.createElement('div');
                docContainer.className = 'doc-attachments';
                documents.forEach(function (doc) {
                    const chip = document.createElement('span');
                    chip.className = 'doc-chip doc-chip-sent doc-chip-' + safeElementType(doc.type);
                    chip.textContent = '@' + doc.qualifiedName;
                    docContainer.appendChild(chip);
                });
                div.appendChild(docContainer);
            }
            if (files && files.length > 0) {
                const fileContainer = document.createElement('div');
                fileContainer.className = 'file-attachments';
                files.forEach(function (f) {
                    const chip = document.createElement('span');
                    chip.className = 'file-chip file-chip-sent file-chip-' + fileLanguageCategory(f.language);
                    chip.textContent = '\uD83D\uDCC4 ' + f.name;
                    chip.title = f.name + ' (' + formatFileSize(f.content.length) + ')';
                    fileContainer.appendChild(chip);
                });
                div.appendChild(fileContainer);
            }
            if (images && images.length > 0) {
                const imgContainer = document.createElement('div');
                imgContainer.className = 'image-attachments';
                images.forEach(function (img) {
                    const imgEl = document.createElement('img');
                    imgEl.src = img.dataUrl || ('data:' + img.mediaType + ';base64,' + img.base64);
                    imgEl.alt = img.name || 'Attached image';
                    imgEl.title = img.name || 'Attached image';
                    imgContainer.appendChild(imgEl);
                });
                div.appendChild(imgContainer);
            }
            const textNode = document.createElement('span');
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
        const name = data.toolName || 'tool';
        const label = formatToolName(name);
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
        const labels = {
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
        const msg = 'Microflow "' + (data.name || '') + '" created successfully. Press F4 to refresh.';
        const div = document.createElement('div');
        div.className = 'success-msg';
        div.textContent = msg;
        chatArea.appendChild(div);
        scrollToBottom();
        chatHistory.push({ type: 'success', content: msg });
    }

    // --- Model Changed (prompt user to refresh context) ---
    function handleModelChanged() {
        contextDot.className = 'status-dot stale';
        contextText.textContent = 'Context outdated — click \u21BB to refresh';
    }

    // --- Token Usage ---
    function handleTokenUsage(data) {
        if (!data) return;
        const inputTokens = data.inputTokens || 0;
        const outputTokens = data.outputTokens || 0;
        const cacheCreation = data.cacheCreationTokens || 0;
        const cacheRead = data.cacheReadTokens || 0;
        cumulativeInputTokens += inputTokens;
        cumulativeOutputTokens += outputTokens;

        const total = inputTokens + outputTokens;

        function makeSpan(cls, text) {
            const s = document.createElement('span');
            s.className = cls;
            s.textContent = text;
            return s;
        }

        function makeSep() {
            return document.createTextNode(' \u00B7 ');
        }

        const div = document.createElement('div');
        div.className = 'token-usage';
        div.appendChild(makeSpan('token-label', 'Tokens:'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-in', formatTokens(inputTokens) + ' in'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-out', formatTokens(outputTokens) + ' out'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-total', formatTokens(total) + ' total'));

        if (cacheCreation > 0 || cacheRead > 0) {
            const cacheText = 'Cache: ' +
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
        const badge = document.getElementById('tokenBadge');
        if (!badge) return;
        const total = cumulativeInputTokens + cumulativeOutputTokens;
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
        const pct = Math.min(Math.round((used / limit) * 100), 100);
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
    function handleRetryWait(data) {
        if (!data) return;
        retryAttemptCount++;
        const attempt = retryAttemptCount;
        const totalSec = data.delaySec || 15;
        const maxRetries = data.maxRetries || 20;
        let remaining = totalSec;

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
        const msg = data.message || 'An error occurred.';
        const div = document.createElement('div');
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
        const conversations = data.conversations;

        if (autoLoadPending) {
            autoLoadPending = false;
            if (conversations.length > 0) {
                sendToBackend('load_conversation', { id: conversations[0].id });
            }
            return;
        }

        historyList.textContent = '';

        if (conversations.length === 0) {
            const emptyP = document.createElement('p');
            emptyP.className = 'history-empty';
            emptyP.textContent = 'No saved conversations yet.';
            historyList.appendChild(emptyP);
            return;
        }

        for (let i = 0; i < conversations.length; i++) {
            const conv = conversations[i];
            const date = new Date(conv.updatedAt).toLocaleDateString(undefined, {
                month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
            });
            let title = conv.title || 'Untitled';
            if (title.length > 70) title = title.substring(0, 70) + '...';
            const msgCount = conv.messageCount || 0;

            const item = document.createElement('div');
            item.className = 'history-item';
            item.dataset.id = conv.id;

            const main = document.createElement('div');
            main.className = 'history-item-main';

            const titleDiv = document.createElement('div');
            titleDiv.className = 'history-item-title';
            titleDiv.textContent = title;

            const metaDiv = document.createElement('div');
            metaDiv.className = 'history-item-meta';
            metaDiv.textContent = date + ' \u00B7 ' + msgCount + ' messages';

            main.appendChild(titleDiv);
            main.appendChild(metaDiv);

            const delBtn = document.createElement('button');
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

        const history = data.displayHistory;
        for (let i = 0; i < history.length; i++) {
            const entry = history[i];
            chatHistory.push(entry);

            switch (entry.type) {
                case 'user':
                    appendMessage('user', entry.content, entry.images, entry.documents, entry.files);
                    break;
                case 'assistant':
                    appendMessage('assistant', entry.content);
                    break;
                case 'tool_start':
                    break;
                case 'tool_result':
                    break;
                case 'success': {
                    const sdiv = document.createElement('div');
                    sdiv.className = 'success-msg';
                    sdiv.textContent = entry.content;
                    chatArea.appendChild(sdiv);
                    break;
                }
                case 'error': {
                    const ediv = document.createElement('div');
                    ediv.className = 'error-msg';
                    ediv.textContent = entry.content;
                    chatArea.appendChild(ediv);
                    break;
                }
                case 'tokens': {
                    const tdiv = document.createElement('div');
                    tdiv.className = 'token-usage';
                    tdiv.textContent = entry.content;
                    chatArea.appendChild(tdiv);
                    break;
                }
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
            if (data.autoLoadLastConversation !== false && !skipAutoLoadConversation) {
                autoLoadPending = true;
                sendToBackend('get_history');
            }
        }
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
        const apiKey = document.getElementById('apiKeyInput').value;
        const model = document.getElementById('modelSelect').value;
        const depth = document.getElementById('contextDepthSelect').value;
        const tokens = parseInt(document.getElementById('maxTokensInput').value) || 8192;
        const theme = themeSelect ? themeSelect.value : currentTheme;

        var retryMaxAttemptsVal = parseInt(document.getElementById('retryMaxAttemptsInput').value);
        var retryMaxAttempts = isNaN(retryMaxAttemptsVal) ? 20 : retryMaxAttemptsVal;
        const retryDelaySeconds = parseInt(document.getElementById('retryDelaySecondsInput').value) || 60;
        const maxToolRounds = parseInt(document.getElementById('maxToolRoundsInput').value) || 10;
        const promptCachingEnabled = document.getElementById('promptCachingCheckbox').checked;
        const autoRefreshContext = document.getElementById('autoRefreshContextCheckbox').checked;
        const autoLoadLastConversation = document.getElementById('autoLoadLastConversationCheckbox').checked;

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
        const labels = {
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

    // --- Help ---
    function openHelp() {
        helpModal.classList.remove('hidden');
    }

    function closeHelp() {
        helpModal.classList.add('hidden');
    }

    function exportChat() {
        const includeTools = exportToolActivity.checked;
        const modelName = modelBadge.textContent || 'Claude';
        const now = new Date();
        const dateStr = now.toISOString().slice(0, 10);
        const timeStr = now.toTimeString().slice(0, 5);

        const lines = [];
        lines.push('# AIDE Lite Chat Export');
        lines.push('**Date:** ' + dateStr + ' ' + timeStr + ' | **Model:** ' + modelName);
        lines.push('');
        lines.push('---');
        lines.push('');

        for (let i = 0; i < chatHistory.length; i++) {
            const entry = chatHistory[i];
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
        lines.push('*Exported from AIDE Lite v1.2.0*');

        const markdown = lines.join('\n');
        const blob = new Blob([markdown], { type: 'text/markdown;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
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

        const codeBlocks = [];
        let html = text;

        html = html.replace(/```(\w*)\n([\s\S]*?)```/g, function (m, lang, code) {
            const idx = codeBlocks.length;
            codeBlocks.push('<pre><code class="language-' + (lang || '') + '">' + escapeHtml(code) + '</code></pre>');
            return '\x00CODEBLOCK' + idx + '\x00';
        });

        html = html.replace(/`([^`]+)`/g, function (m, code) {
            const idx = codeBlocks.length;
            codeBlocks.push('<code>' + escapeHtml(code) + '</code>');
            return '\x00CODEBLOCK' + idx + '\x00';
        });

        html = escapeHtml(html);

        html = html.replace(/\x00CODEBLOCK(\d+)\x00/g, function (m, idx) {
            return codeBlocks[parseInt(idx)] || '';
        });

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
        let tableIsHeader = true;
        html = html.replace(/^\|(.+)\|$/gm, function (match, content) {
            const cells = content.split('|').map(function (c) { return c.trim(); });
            if (cells.every(function (c) { return /^[-:]+$/.test(c); })) {
                tableIsHeader = false;
                return '<!-- table separator -->';
            }
            const tag = tableIsHeader ? 'th' : 'td';
            const row = cells.map(function (c) { return '<' + tag + '>' + c + '</' + tag + '>'; }).join('');
            return '<tr>' + row + '</tr>';
        });
        html = html.replace(/((<tr>[\s\S]*?<\/tr>\s*)+)/g, function () {
            tableIsHeader = true;
            return '<table>' + arguments[0] + '</table>';
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

    // --- Transient Toast Notification ---
    function showToast(message) {
        const toast = document.createElement('div');
        toast.className = 'toast-notification';
        toast.textContent = message;
        document.body.appendChild(toast);
        setTimeout(function () {
            toast.classList.add('fade-out');
            setTimeout(function () { toast.remove(); }, 300);
        }, 2500);
    }

    // --- Image Attachment Handling ---
    function addImageFile(file) {
        if (pendingImages.length >= 5) {
            showToast('Maximum 5 images per message.');
            return;
        }
        if (!file.type || !ALLOWED_IMAGE_TYPES[file.type]) {
            showToast('Unsupported image format. Use JPEG, PNG, GIF, or WebP.');
            return;
        }
        if (file.size > MAX_IMAGE_SIZE) {
            showToast('Image too large (max 20 MB).');
            return;
        }
        const reader = new FileReader();
        reader.onload = function (e) {
            const dataUrl = e.target.result;
            const commaIdx = dataUrl.indexOf(',');
            const base64 = dataUrl.substring(commaIdx + 1);
            pendingImages.push({ base64: base64, mediaType: file.type, name: file.name, dataUrl: dataUrl });
            renderImagePreviews();
        };
        reader.onerror = function () {
            showToast('Failed to read image: ' + file.name);
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
            const item = document.createElement('div');
            item.className = 'image-preview-item';

            const thumb = document.createElement('img');
            thumb.className = 'image-preview-thumb';
            thumb.src = img.dataUrl;
            thumb.alt = img.name || 'image';
            thumb.title = img.name || 'Attached image';

            const removeBtn = document.createElement('button');
            removeBtn.className = 'image-preview-remove';
            removeBtn.innerHTML = '\u00D7';
            removeBtn.title = 'Remove image';
            removeBtn.setAttribute('data-idx', idx);
            removeBtn.addEventListener('click', function () {
                const i = parseInt(this.getAttribute('data-idx'), 10);
                pendingImages.splice(i, 1);
                renderImagePreviews();
            });

            item.appendChild(thumb);
            item.appendChild(removeBtn);
            imagePreview.appendChild(item);
        });
    }

    // --- Text File Attachment Handling ---
    function getFileExtension(filename) {
        var dot = filename.lastIndexOf('.');
        return dot >= 0 ? filename.substring(dot).toLowerCase() : '';
    }

    function fileLanguageCategory(language) {
        var code = { 'java': true, 'javascript': true, 'css': true, 'html': true, 'sql': true };
        var data = { 'json': true, 'xml': true, 'yaml': true, 'csv': true, 'properties': true };
        if (code[language]) return 'code';
        if (data[language]) return 'data';
        return 'docs';
    }

    function formatFileSize(bytes) {
        if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
        if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
        return bytes + ' B';
    }

    function addTextFile(file) {
        if (pendingFiles.length >= 10) {
            showToast('Maximum 10 files per message.');
            return;
        }
        var ext = getFileExtension(file.name);
        var language = ALLOWED_FILE_EXTENSIONS[ext];
        if (!language) {
            showToast('Unsupported file type: ' + ext + '. Use code, config, or text files.');
            return;
        }
        if (file.size > MAX_FILE_SIZE) {
            showToast('File too large (max 500 KB): ' + file.name);
            return;
        }
        var alreadyAdded = pendingFiles.some(function (f) { return f.name === file.name; });
        if (alreadyAdded) {
            showToast('File already added: ' + file.name);
            return;
        }
        var reader = new FileReader();
        reader.onload = function (e) {
            pendingFiles.push({
                name: file.name,
                language: language,
                content: e.target.result,
                size: file.size
            });
            renderFilePreviews();
        };
        reader.onerror = function () {
            showToast('Failed to read file: ' + file.name);
        };
        reader.readAsText(file);
    }

    function renderFilePreviews() {
        if (!filePreview) return;
        filePreview.innerHTML = '';
        if (pendingFiles.length === 0) {
            filePreview.classList.remove('has-files');
            return;
        }
        filePreview.classList.add('has-files');
        pendingFiles.forEach(function (f, idx) {
            var chip = document.createElement('div');
            chip.className = 'file-chip file-chip-' + fileLanguageCategory(f.language);

            var icon = document.createElement('span');
            icon.className = 'file-chip-icon';
            icon.textContent = '\uD83D\uDCC4';

            var nameSpan = document.createElement('span');
            nameSpan.className = 'file-chip-name';
            nameSpan.textContent = f.name;
            nameSpan.title = f.name;

            var sizeSpan = document.createElement('span');
            sizeSpan.className = 'file-chip-size';
            sizeSpan.textContent = formatFileSize(f.size);

            var removeBtn = document.createElement('button');
            removeBtn.className = 'file-chip-remove';
            removeBtn.innerHTML = '\u00D7';
            removeBtn.title = 'Remove';
            removeBtn.setAttribute('aria-label', 'Remove ' + f.name);
            removeBtn.setAttribute('data-idx', idx);
            removeBtn.addEventListener('click', function () {
                var i = parseInt(this.getAttribute('data-idx'), 10);
                pendingFiles.splice(i, 1);
                renderFilePreviews();
            });

            chip.appendChild(icon);
            chip.appendChild(nameSpan);
            chip.appendChild(sizeSpan);
            chip.appendChild(removeBtn);
            filePreview.appendChild(chip);
        });
    }

    // --- Type Validation ---
    function safeElementType(type) {
        const allowed = { 'microflow': true, 'page': true, 'entity': true, 'constant': true, 'enumeration': true, 'java_action': true, 'document': true };
        return allowed[type] ? type : 'document';
    }

    // --- Active Document Tracking ---
    function handleActiveDocumentChanged(data) {
        if (!data || !data.name) {
            activeDocument = null;
            updateActiveDocBar();
            return;
        }
        activeDocument = {
            name: data.name,
            type: data.type || 'document',
            qualifiedName: data.qualifiedName || data.name
        };
        updateActiveDocBar();
    }

    function updateActiveDocBar() {
        if (!activeDocBar) return;
        if (!activeDocument) {
            activeDocBar.classList.add('hidden');
            return;
        }
        activeDocBar.classList.remove('hidden');
        activeDocBar.className = 'active-doc-bar active-doc-' + safeElementType(activeDocument.type);

        var iconMap = {
            'microflow': '\u2699',
            'page': '\uD83D\uDCC4',
            'entity': '\uD83D\uDCE6',
            'document': '\uD83D\uDCC1'
        };
        activeDocIcon.textContent = iconMap[activeDocument.type] || iconMap['document'];
        activeDocText.textContent = activeDocument.qualifiedName;
    }

    // --- Document Reference Handling ---
    function handleAutoExplain(data) {
        if (!data || !data.qualifiedName) return;
        pendingDocuments = [{ type: data.type || 'document', qualifiedName: data.qualifiedName }];
        chatInput.value = 'Explain @' + data.qualifiedName;
        hideWelcome();
        sendMessage();
    }

    function handleDocumentReferenced(data) {
        if (!data || !data.qualifiedName) return;
        if (pendingDocuments.length >= 10) {
            showToast('Maximum 10 document references per message.');
            return;
        }
        const already = pendingDocuments.some(function (d) { return d.qualifiedName === data.qualifiedName; });
        if (!already) {
            pendingDocuments.push({ type: data.type || 'document', qualifiedName: data.qualifiedName });
            renderDocumentPreviews();
        }
        chatInput.focus();
    }

    function renderDocumentPreviews() {
        const container = document.getElementById('docPreview');
        if (!container) return;
        container.innerHTML = '';
        if (pendingDocuments.length === 0) {
            container.classList.remove('has-docs');
            return;
        }
        container.classList.add('has-docs');
        pendingDocuments.forEach(function (doc, idx) {
            const chip = document.createElement('div');
            chip.className = 'doc-chip doc-chip-' + safeElementType(doc.type);

            const label = document.createElement('span');
            label.className = 'doc-chip-name';
            label.textContent = '@' + doc.qualifiedName;

            const removeBtn = document.createElement('button');
            removeBtn.className = 'doc-chip-remove';
            removeBtn.innerHTML = '\u00D7';
            removeBtn.title = 'Remove';
            removeBtn.setAttribute('aria-label', 'Remove ' + doc.qualifiedName);
            removeBtn.setAttribute('data-idx', idx);
            removeBtn.addEventListener('click', function () {
                const i = parseInt(this.getAttribute('data-idx'), 10);
                pendingDocuments.splice(i, 1);
                renderDocumentPreviews();
            });

            chip.appendChild(label);
            chip.appendChild(removeBtn);
            container.appendChild(chip);
        });
    }

    // Block WebView2 from navigating to dropped files globally
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
        if (inputArea.contains(e.relatedTarget)) return;
        inputArea.classList.remove('drag-over');
    });
    inputArea.addEventListener('drop', function (e) {
        e.preventDefault();
        e.stopPropagation();
        inputArea.classList.remove('drag-over');
        const files = e.dataTransfer && e.dataTransfer.files;
        if (files) {
            for (let i = 0; i < files.length; i++) {
                var f = files[i];
                if (f.type && ALLOWED_IMAGE_TYPES[f.type]) {
                    addImageFile(f);
                } else {
                    addTextFile(f);
                }
            }
        }
    });

    // Paste images or text files from clipboard
    chatInput.addEventListener('paste', function (e) {
        const items = e.clipboardData && e.clipboardData.items;
        if (!items) return;
        for (let i = 0; i < items.length; i++) {
            if (items[i].kind === 'file') {
                const file = items[i].getAsFile();
                if (!file) continue;
                if (ALLOWED_IMAGE_TYPES[file.type]) {
                    addImageFile(file);
                } else {
                    addTextFile(file);
                }
            }
        }
    });

    // Attach image or file via unified picker button
    attachBtn.addEventListener('click', function () {
        attachFileInput.value = '';
        attachFileInput.click();
    });
    attachFileInput.addEventListener('change', function () {
        const files = attachFileInput.files;
        if (files) {
            for (let i = 0; i < files.length; i++) {
                var f = files[i];
                if (f.type && ALLOWED_IMAGE_TYPES[f.type]) {
                    addImageFile(f);
                } else {
                    addTextFile(f);
                }
            }
        }
        attachFileInput.value = '';
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
        this.style.height = Math.min(this.scrollHeight, MAX_TEXTAREA_HEIGHT) + 'px';
    });

    refreshBtn.addEventListener('click', function () {
        contextDot.className = 'status-dot loading';
        contextText.textContent = 'Loading context...';
        sendToBackend('get_context');
    });

    newChatBtn.addEventListener('click', function () {
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
        pendingFiles = [];
        pendingDocuments = [];
        renderImagePreviews();
        renderFilePreviews();
        renderDocumentPreviews();
        updateTokenBadge();
        resetContextUsage();
        sendToBackend('new_chat');
    });

    settingsBtn.addEventListener('click', openSettings);
    saveSettingsBtn.addEventListener('click', saveSettings);
    cancelSettingsBtn.addEventListener('click', closeSettings);

    if (themeToggleBtn) {
        themeToggleBtn.addEventListener('click', function () {
            const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
            applyTheme(newTheme);
            sendToBackend('save_settings', { theme: newTheme });
        });
    }

    if (themeSelect) {
        themeSelect.addEventListener('change', function () {
            applyTheme(this.value);
        });
    }

    if (historyBtn) historyBtn.addEventListener('click', openHistory);
    if (historyCloseBtn) historyCloseBtn.addEventListener('click', closeHistory);
    if (historyOverlay) historyOverlay.addEventListener('click', closeHistory);

    if (exportBtn) exportBtn.addEventListener('click', openExport);
    if (exportDownloadBtn) exportDownloadBtn.addEventListener('click', exportChat);
    if (exportCancelBtn) exportCancelBtn.addEventListener('click', closeExport);
    if (exportOverlay) exportOverlay.addEventListener('click', closeExport);

    if (helpBtn) helpBtn.addEventListener('click', openHelp);
    if (helpCloseBtn) helpCloseBtn.addEventListener('click', closeHelp);
    if (helpOverlay) helpOverlay.addEventListener('click', closeHelp);

    if (activeDocAddBtn) {
        activeDocAddBtn.addEventListener('click', function () {
            if (!activeDocument) return;
            var already = pendingDocuments.some(function (d) { return d.qualifiedName === activeDocument.qualifiedName; });
            if (!already) {
                pendingDocuments.push({ type: activeDocument.type, qualifiedName: activeDocument.qualifiedName });
                renderDocumentPreviews();
            }
            chatInput.focus();
        });
    }

    if (consentAcceptBtn) consentAcceptBtn.addEventListener('click', function () {
        sendToBackend('consent_accepted');
        if (consentModal) consentModal.classList.add('hidden');
    });
    if (consentDeclineBtn) consentDeclineBtn.addEventListener('click', function () {
        if (consentModal) consentModal.classList.add('hidden');
    });

    if (privacyBtn) privacyBtn.addEventListener('click', openPrivacy);
    if (privacyCloseBtn) privacyCloseBtn.addEventListener('click', closePrivacy);
    if (privacyOverlay) privacyOverlay.addEventListener('click', closePrivacy);

    document.querySelector('#settingsModal .modal-overlay')?.addEventListener('click', closeSettings);

    document.querySelectorAll('.quick-action').forEach(function (btn) {
        btn.addEventListener('click', function () {
            const prompt = this.getAttribute('data-prompt');
            if (prompt) {
                chatInput.value = prompt;
                sendMessage();
            }
        });
    });

    // --- Initialize WebView Message Bridge ---
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', handleMessage);
        sendToBackend('MessageListenerRegistered');
    }

    sendToBackend('get_settings');
})();
