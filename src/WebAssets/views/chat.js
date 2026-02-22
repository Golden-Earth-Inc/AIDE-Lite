// ============================================================================
// AIDE Lite - Chat Core
// Sending messages, streaming responses, tool tracking, tokens, retry, errors.
// ============================================================================
(function (AIDE) {
    'use strict';

    var state = AIDE.state;

    // --- Send Message ---
    AIDE.sendMessage = function () {
        var text = AIDE.dom.chatInput.value.trim();
        if (!text || state.get('isStreaming')) return;

        var images = state.get('pendingImages').slice();
        var docs = state.get('pendingDocuments').slice();
        var files = state.get('pendingFiles').slice();
        AIDE.hideWelcome();
        AIDE.appendMessage('user', text, images, docs, files);
        state.get('chatHistory').push({ type: 'user', content: text, images: images, documents: docs, files: files });
        AIDE.dom.chatInput.value = '';
        AIDE.dom.chatInput.style.height = 'auto';
        state.set('pendingImages', []);
        state.set('pendingFiles', []);
        state.set('pendingDocuments', []);
        AIDE.renderImagePreviews();
        AIDE.renderFilePreviews();
        AIDE.renderDocumentPreviews();

        state.set('isStreaming', true);
        state.set('retryAttemptCount', 0);
        AIDE.dom.sendBtn.disabled = true;
        AIDE.dom.sendBtn.classList.add('hidden');
        AIDE.dom.stopBtn.classList.remove('hidden');
        AIDE.showProcessingBar('Thinking...');

        var payload = { message: text, mode: AIDE.dom.modeSelect.value };
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
        var activeDoc = state.get('activeDocument');
        if (activeDoc) {
            payload.activeDocument = {
                type: activeDoc.type,
                qualifiedName: activeDoc.qualifiedName
            };
        }
        AIDE.sendToBackend('chat', payload);
    };

    // --- Streaming ---
    AIDE.handleStreamChunk = function (data) {
        if (!data) return;

        var streamEl = state.get('streamElement');
        if (!streamEl) {
            state.set('isStreaming', true);
            AIDE.hideWelcome();
            state.set('streamBuffer', '');
            streamEl = document.createElement('div');
            streamEl.className = 'message assistant streaming';
            AIDE.dom.chatArea.appendChild(streamEl);
            state.set('streamElement', streamEl);
            AIDE.dom.sendBtn.classList.add('hidden');
            AIDE.dom.stopBtn.classList.remove('hidden');
        }

        if (data.token) {
            state.set('streamBuffer', state.get('streamBuffer') + data.token);
            streamEl.innerHTML = AIDE.renderMarkdown(state.get('streamBuffer'));
            AIDE.scrollToBottom();
            AIDE.updateProcessingLabel('Generating response...');
        }

        if (data.done) {
            AIDE.endStream();
        }
    };

    AIDE.endStream = function () {
        var interval = state.get('retryCountdownInterval');
        if (interval) {
            clearInterval(interval);
            state.set('retryCountdownInterval', null);
        }
        state.set('retryAttemptCount', 0);
        var streamEl = state.get('streamElement');
        var buf = state.get('streamBuffer');
        if (streamEl) {
            streamEl.classList.remove('streaming');
            streamEl.innerHTML = AIDE.renderMarkdown(buf);
            if (buf) AIDE.addCopyButton(streamEl, buf);
        }
        if (buf) {
            state.get('chatHistory').push({ type: 'assistant', content: buf });
        }
        state.set('isStreaming', false);
        state.set('streamBuffer', '');
        state.set('streamElement', null);
        AIDE.dom.sendBtn.disabled = false;
        AIDE.dom.sendBtn.classList.remove('hidden');
        AIDE.dom.stopBtn.classList.add('hidden');
        AIDE.hideToolActivity();
        AIDE.hideProcessingBar();
        AIDE.autoSaveChatState();
    };

    AIDE.autoSaveChatState = function () {
        var history = state.get('chatHistory');
        if (history.length === 0) return;
        AIDE.sendToBackend('save_chat_state', { displayHistory: history });
    };

    // --- Tool Activity ---
    AIDE.handleToolStart = function (data) {
        if (!data) return;
        var name = data.toolName || 'tool';
        var label = AIDE.formatToolName(name);
        AIDE.dom.toolLabel.textContent = label;
        AIDE.dom.toolActivity.classList.remove('hidden');
        AIDE.showProcessingBar(label);
        state.get('chatHistory').push({ type: 'tool_start', content: label, toolName: name });
    };

    AIDE.handleToolResult = function (data) {
        AIDE.hideToolActivity();
        if (data && data.summary) {
            state.get('chatHistory').push({ type: 'tool_result', content: data.summary, toolName: data.toolName });
        }
    };

    AIDE.hideToolActivity = function () {
        AIDE.dom.toolActivity.classList.add('hidden');
    };

    AIDE.formatToolName = function (name) {
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
    };

    // --- Token Usage ---
    AIDE.handleTokenUsage = function (data) {
        if (!data) return;
        var inputTokens = data.inputTokens || 0;
        var outputTokens = data.outputTokens || 0;
        var cacheCreation = data.cacheCreationTokens || 0;
        var cacheRead = data.cacheReadTokens || 0;
        state.set('cumulativeInputTokens', state.get('cumulativeInputTokens') + inputTokens);
        state.set('cumulativeOutputTokens', state.get('cumulativeOutputTokens') + outputTokens);

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
        div.appendChild(makeSpan('token-in', AIDE.formatTokens(inputTokens) + ' in'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-out', AIDE.formatTokens(outputTokens) + ' out'));
        div.appendChild(makeSep());
        div.appendChild(makeSpan('token-total', AIDE.formatTokens(total) + ' total'));

        if (cacheCreation > 0 || cacheRead > 0) {
            var cacheText = 'Cache: ' +
                (cacheRead > 0 ? AIDE.formatTokens(cacheRead) + ' hit' : '') +
                (cacheRead > 0 && cacheCreation > 0 ? ', ' : '') +
                (cacheCreation > 0 ? AIDE.formatTokens(cacheCreation) + ' written' : '');
            div.appendChild(makeSep());
            div.appendChild(makeSpan('token-cache', cacheText));
        }
        AIDE.dom.chatArea.appendChild(div);
        AIDE.scrollToBottom();

        AIDE.updateTokenBadge();

        if (data.contextUsedTokens && data.contextLimitTokens) {
            AIDE.updateContextUsage(data.contextUsedTokens, data.contextLimitTokens);
        }

        state.get('chatHistory').push({ type: 'tokens', content: 'Tokens: ' + AIDE.formatTokens(inputTokens) + ' in, ' + AIDE.formatTokens(outputTokens) + ' out, ' + AIDE.formatTokens(total) + ' total' });
    };

    AIDE.formatTokens = function (n) {
        if (n >= 1000) return (n / 1000).toFixed(1) + 'k';
        return '' + n;
    };

    AIDE.updateTokenBadge = function () {
        var badge = document.getElementById('tokenBadge');
        if (!badge) return;
        var total = state.get('cumulativeInputTokens') + state.get('cumulativeOutputTokens');
        if (total > 0) {
            badge.textContent = AIDE.formatTokens(total) + ' tokens';
            badge.classList.remove('hidden');
        } else {
            badge.classList.add('hidden');
        }
    };

    AIDE.updateContextUsage = function (used, limit) {
        var fill = AIDE.dom.contextUsageFill;
        var label = AIDE.dom.contextUsageLabel;
        var bar = AIDE.dom.contextUsage;
        if (!bar || !fill || !label) return;
        if (!limit || limit <= 0) return;
        var pct = Math.min(Math.round((used / limit) * 100), 100);
        fill.style.width = pct + '%';

        if (pct >= 80) {
            fill.className = 'context-usage-fill danger';
        } else if (pct >= 50) {
            fill.className = 'context-usage-fill warning';
        } else {
            fill.className = 'context-usage-fill';
        }

        label.textContent = pct + '% (' + AIDE.formatTokens(used) + ' / ' + AIDE.formatTokens(limit) + ')';
        bar.classList.remove('hidden');
    };

    AIDE.resetContextUsage = function () {
        var bar = AIDE.dom.contextUsage;
        if (!bar) return;
        bar.classList.add('hidden');
        var fill = AIDE.dom.contextUsageFill;
        if (fill) {
            fill.style.width = '0%';
            fill.className = 'context-usage-fill';
        }
        var label = AIDE.dom.contextUsageLabel;
        if (label) label.textContent = '0%';
    };

    // --- Retry Wait ---
    AIDE.handleRetryWait = function (data) {
        if (!data) return;
        state.set('retryAttemptCount', state.get('retryAttemptCount') + 1);
        var attempt = state.get('retryAttemptCount');
        var totalSec = data.delaySec || 15;
        var maxRetries = data.maxRetries || 20;
        var remaining = totalSec;

        var oldInterval = state.get('retryCountdownInterval');
        if (oldInterval) clearInterval(oldInterval);

        AIDE.showProcessingBar('Rate limited \u2014 retrying in ' + remaining + 's (attempt ' + attempt + '/' + maxRetries + ')');

        var interval = setInterval(function () {
            remaining--;
            if (remaining <= 0) {
                clearInterval(interval);
                state.set('retryCountdownInterval', null);
                AIDE.showProcessingBar('Retrying...');
            } else {
                AIDE.updateProcessingLabel('Rate limited \u2014 retrying in ' + remaining + 's (attempt ' + attempt + '/' + maxRetries + ')');
            }
        }, 1000);
        state.set('retryCountdownInterval', interval);
    };

    // --- Error ---
    AIDE.handleError = function (data) {
        if (!data) return;
        AIDE.endStream();
        var msg = data.message || 'An error occurred.';
        var div = document.createElement('div');
        div.className = 'error-msg';
        div.textContent = msg;
        AIDE.dom.chatArea.appendChild(div);
        AIDE.scrollToBottom();
        AIDE.dom.sendBtn.disabled = false;
        state.get('chatHistory').push({ type: 'error', content: msg });
    };

    // --- Microflow Created ---
    AIDE.handleMicroflowCreated = function (data) {
        if (!data) return;
        var msg = 'Microflow "' + (data.name || '') + '" created successfully. Press F4 to refresh.';
        var div = document.createElement('div');
        div.className = 'success-msg';
        div.textContent = msg;
        AIDE.dom.chatArea.appendChild(div);
        AIDE.scrollToBottom();
        state.get('chatHistory').push({ type: 'success', content: msg });
    };

})(window.AIDE = window.AIDE || {});
