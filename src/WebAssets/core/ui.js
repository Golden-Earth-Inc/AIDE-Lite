// ============================================================================
// AIDE Lite - Display & UI Utilities
// Message display, toast, processing indicators, active document bar, scroll.
// ============================================================================
(function (AIDE) {
    'use strict';

    var state = AIDE.state;

    AIDE.safeElementType = function (type) {
        var allowed = { 'microflow': true, 'page': true, 'entity': true, 'constant': true, 'enumeration': true, 'java_action': true, 'document': true };
        return allowed[type] ? type : 'document';
    };

    AIDE.hideWelcome = function () {
        var ws = AIDE.dom.welcomeScreen;
        if (ws) ws.style.display = 'none';
    };

    AIDE.scrollToBottom = function () {
        var ca = AIDE.dom.chatArea;
        ca.scrollTop = ca.scrollHeight;
    };

    AIDE.showProcessingBar = function (label) {
        AIDE.dom.processingLabel.textContent = label || 'Processing...';
        AIDE.dom.processingBar.classList.remove('hidden');
    };

    AIDE.updateProcessingLabel = function (label) {
        AIDE.dom.processingLabel.textContent = label;
    };

    AIDE.hideProcessingBar = function () {
        AIDE.dom.processingBar.classList.add('hidden');
    };

    AIDE.showToast = function (message) {
        var toast = document.createElement('div');
        toast.className = 'toast-notification';
        toast.textContent = message;
        document.body.appendChild(toast);
        setTimeout(function () {
            toast.classList.add('fade-out');
            setTimeout(function () { toast.remove(); }, 300);
        }, 2500);
    };

    AIDE.appendMessage = function (role, content, images, documents, files) {
        var div = document.createElement('div');
        div.className = 'message ' + role;
        if (role === 'assistant') {
            div.innerHTML = AIDE.renderMarkdown(content);
            AIDE.addCopyButton(div, content);
        } else {
            if (documents && documents.length > 0) {
                var docContainer = document.createElement('div');
                docContainer.className = 'doc-attachments';
                documents.forEach(function (doc) {
                    var chip = document.createElement('span');
                    chip.className = 'doc-chip doc-chip-sent doc-chip-' + AIDE.safeElementType(doc.type);
                    chip.textContent = '@' + doc.qualifiedName;
                    docContainer.appendChild(chip);
                });
                div.appendChild(docContainer);
            }
            if (files && files.length > 0) {
                var fileContainer = document.createElement('div');
                fileContainer.className = 'file-attachments';
                files.forEach(function (f) {
                    var chip = document.createElement('span');
                    chip.className = 'file-chip file-chip-sent file-chip-' + AIDE.fileLanguageCategory(f.language);
                    chip.textContent = '\uD83D\uDCC4 ' + f.name;
                    chip.title = f.name + ' (' + AIDE.formatFileSize(f.content.length) + ')';
                    fileContainer.appendChild(chip);
                });
                div.appendChild(fileContainer);
            }
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
        AIDE.dom.chatArea.appendChild(div);
        AIDE.scrollToBottom();
        return div;
    };

    AIDE.handleActiveDocumentChanged = function (data) {
        if (!data || !data.name) {
            state.set('activeDocument', null);
            AIDE.updateActiveDocBar();
            return;
        }
        state.set('activeDocument', {
            name: data.name,
            type: data.type || 'document',
            qualifiedName: data.qualifiedName || data.name
        });
        AIDE.updateActiveDocBar();
    };

    AIDE.updateActiveDocBar = function () {
        var bar = AIDE.dom.activeDocBar;
        if (!bar) return;
        var doc = state.get('activeDocument');
        if (!doc) {
            bar.classList.add('hidden');
            return;
        }
        bar.classList.remove('hidden');
        bar.className = 'active-doc-bar active-doc-' + AIDE.safeElementType(doc.type);

        var iconMap = {
            'microflow': '\u2699',
            'page': '\uD83D\uDCC4',
            'entity': '\uD83D\uDCE6',
            'document': '\uD83D\uDCC1'
        };
        AIDE.dom.activeDocIcon.textContent = iconMap[doc.type] || iconMap['document'];
        AIDE.dom.activeDocText.textContent = doc.qualifiedName;
    };

    AIDE.handleContextLoaded = function (data) {
        if (!data) return;
        AIDE.dom.contextDot.className = 'status-dot online';
        AIDE.dom.contextText.textContent = data.summary || 'Context loaded';
    };

    AIDE.handleModelChanged = function () {
        AIDE.dom.contextDot.className = 'status-dot stale';
        AIDE.dom.contextText.textContent = 'Context outdated \u2014 click \u21BB to refresh';
    };

    // --- View Toggle ---

    AIDE.updateToggleButton = function () {
        var btn = AIDE.dom.toggleViewBtn;
        if (!btn) return;
        var mode = state.get('viewMode');
        if (mode === AIDE.CONST.VIEW_MODES.TAB) {
            btn.innerHTML = '&#x29C9;'; // ⧉ restore down / windowed
            btn.title = 'Collapse to sidebar pane';
        } else {
            btn.innerHTML = '&#x26F6;'; // ⛶ expand to tab
            btn.title = 'Expand to editor tab';
        }
    };

    AIDE.handleSetViewMode = function (data) {
        if (!data || !data.mode) return;
        state.set('viewMode', data.mode);
        AIDE.updateToggleButton();
    };

})(window.AIDE = window.AIDE || {});
