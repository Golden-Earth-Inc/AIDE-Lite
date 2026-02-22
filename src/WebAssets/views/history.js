// ============================================================================
// AIDE Lite - History & Export
// Conversation list, loading saved chats, markdown export, help modal.
// ============================================================================
(function (AIDE) {
    'use strict';

    var state = AIDE.state;

    // --- History ---
    AIDE.openHistory = function () {
        AIDE.dom.historyModal.classList.remove('hidden');
        AIDE.sendToBackend('get_history');
    };

    AIDE.closeHistory = function () {
        AIDE.dom.historyModal.classList.add('hidden');
    };

    AIDE.handleHistoryList = function (data) {
        if (!data || !data.conversations) return;
        var conversations = data.conversations;

        if (state.get('autoLoadPending')) {
            state.set('autoLoadPending', false);
            if (conversations.length > 0) {
                AIDE.sendToBackend('load_conversation', { id: conversations[0].id });
            }
            return;
        }

        var list = AIDE.dom.historyList;
        list.textContent = '';

        if (conversations.length === 0) {
            var emptyP = document.createElement('p');
            emptyP.className = 'history-empty';
            emptyP.textContent = 'No saved conversations yet.';
            list.appendChild(emptyP);
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
            list.appendChild(item);

            main.addEventListener('click', (function (convId) {
                return function () {
                    AIDE.closeHistory();
                    AIDE.sendToBackend('load_conversation', { id: convId });
                };
            })(conv.id));

            delBtn.addEventListener('click', (function (convId) {
                return function (e) {
                    e.stopPropagation();
                    var el = this.closest('.history-item');
                    if (el) el.remove();
                    AIDE.sendToBackend('delete_conversation', { id: convId });
                };
            })(conv.id));
        }
    };

    AIDE.handleConversationLoaded = function (data) {
        if (!data || !data.displayHistory) return;

        if (state.get('isStreaming')) {
            AIDE.sendToBackend('cancel');
            AIDE.endStream();
        }

        AIDE.dom.chatArea.innerHTML = '';
        state.set('chatHistory', []);
        state.set('cumulativeInputTokens', 0);
        state.set('cumulativeOutputTokens', 0);
        AIDE.dom.sendBtn.disabled = false;
        AIDE.resetContextUsage();

        var history = data.displayHistory;
        var chatHistory = state.get('chatHistory');
        for (var i = 0; i < history.length; i++) {
            var entry = history[i];
            chatHistory.push(entry);

            switch (entry.type) {
                case 'user':
                    AIDE.appendMessage('user', entry.content, entry.images, entry.documents, entry.files);
                    break;
                case 'assistant':
                    AIDE.appendMessage('assistant', entry.content);
                    break;
                case 'tool_start':
                    break;
                case 'tool_result':
                    break;
                case 'success': {
                    var sdiv = document.createElement('div');
                    sdiv.className = 'success-msg';
                    sdiv.textContent = entry.content;
                    AIDE.dom.chatArea.appendChild(sdiv);
                    break;
                }
                case 'error': {
                    var ediv = document.createElement('div');
                    ediv.className = 'error-msg';
                    ediv.textContent = entry.content;
                    AIDE.dom.chatArea.appendChild(ediv);
                    break;
                }
                case 'tokens': {
                    var tdiv = document.createElement('div');
                    tdiv.className = 'token-usage';
                    tdiv.textContent = entry.content;
                    AIDE.dom.chatArea.appendChild(tdiv);
                    break;
                }
            }
        }

        AIDE.scrollToBottom();
        AIDE.updateTokenBadge();
    };

    // --- Export ---
    AIDE.openExport = function () {
        if (state.get('chatHistory').length === 0) return;
        AIDE.dom.exportModal.classList.remove('hidden');
    };

    AIDE.closeExport = function () {
        AIDE.dom.exportModal.classList.add('hidden');
    };

    AIDE.exportChat = function () {
        var includeTools = AIDE.dom.exportToolActivity.checked;
        var modelName = AIDE.dom.modelBadge.textContent || 'Claude';
        var now = new Date();
        var dateStr = now.toISOString().slice(0, 10);
        var timeStr = now.toTimeString().slice(0, 5);
        var chatHistory = state.get('chatHistory');

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
        lines.push('*Exported from AIDE Lite v1.3.0*');

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
        AIDE.closeExport();
    };

    // --- Help ---
    AIDE.openHelp = function () {
        AIDE.dom.helpModal.classList.remove('hidden');
    };

    AIDE.closeHelp = function () {
        AIDE.dom.helpModal.classList.add('hidden');
    };

})(window.AIDE = window.AIDE || {});
