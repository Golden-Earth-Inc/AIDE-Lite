// ============================================================================
// AIDE Lite - Attachments (Images, Files & Documents)
// All attachment handling — add, preview, remove, drag/drop, paste.
// ============================================================================
(function (AIDE) {
    'use strict';

    var state = AIDE.state;
    var CONST = AIDE.CONST;

    // --- File Utilities ---
    AIDE.getFileExtension = function (filename) {
        var dot = filename.lastIndexOf('.');
        return dot >= 0 ? filename.substring(dot).toLowerCase() : '';
    };

    AIDE.fileLanguageCategory = function (language) {
        var code = { 'java': true, 'javascript': true, 'css': true, 'html': true, 'sql': true };
        var data = { 'json': true, 'xml': true, 'yaml': true, 'csv': true, 'properties': true };
        if (code[language]) return 'code';
        if (data[language]) return 'data';
        return 'docs';
    };

    AIDE.formatFileSize = function (bytes) {
        if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
        if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
        return bytes + ' B';
    };

    // --- Image Attachments ---
    AIDE.addImageFile = function (file) {
        var pending = state.get('pendingImages');
        if (pending.length >= 5) {
            AIDE.showToast('Maximum 5 images per message.');
            return;
        }
        if (!file.type || !CONST.ALLOWED_IMAGE_TYPES[file.type]) {
            AIDE.showToast('Unsupported image format. Use JPEG, PNG, GIF, or WebP.');
            return;
        }
        if (file.size > CONST.MAX_IMAGE_SIZE) {
            AIDE.showToast('Image too large (max 20 MB).');
            return;
        }
        var reader = new FileReader();
        reader.onload = function (e) {
            var dataUrl = e.target.result;
            var commaIdx = dataUrl.indexOf(',');
            var base64 = dataUrl.substring(commaIdx + 1);
            pending.push({ base64: base64, mediaType: file.type, name: file.name, dataUrl: dataUrl });
            AIDE.renderImagePreviews();
        };
        reader.onerror = function () {
            AIDE.showToast('Failed to read image: ' + file.name);
        };
        reader.readAsDataURL(file);
    };

    AIDE.renderImagePreviews = function () {
        var container = AIDE.dom.imagePreview;
        var pending = state.get('pendingImages');
        container.innerHTML = '';
        if (pending.length === 0) {
            container.classList.remove('has-images');
            return;
        }
        container.classList.add('has-images');
        pending.forEach(function (img, idx) {
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
                state.get('pendingImages').splice(i, 1);
                AIDE.renderImagePreviews();
            });

            item.appendChild(thumb);
            item.appendChild(removeBtn);
            container.appendChild(item);
        });
    };

    // --- Text File Attachments ---
    AIDE.addTextFile = function (file) {
        var pending = state.get('pendingFiles');
        if (pending.length >= 10) {
            AIDE.showToast('Maximum 10 files per message.');
            return;
        }
        var ext = AIDE.getFileExtension(file.name);
        var language = CONST.ALLOWED_FILE_EXTENSIONS[ext];
        if (!language) {
            AIDE.showToast('Unsupported file type: ' + ext + '. Use code, config, or text files.');
            return;
        }
        if (file.size > CONST.MAX_FILE_SIZE) {
            AIDE.showToast('File too large (max 500 KB): ' + file.name);
            return;
        }
        var alreadyAdded = pending.some(function (f) { return f.name === file.name; });
        if (alreadyAdded) {
            AIDE.showToast('File already added: ' + file.name);
            return;
        }
        var reader = new FileReader();
        reader.onload = function (e) {
            pending.push({
                name: file.name,
                language: language,
                content: e.target.result,
                size: file.size
            });
            AIDE.renderFilePreviews();
        };
        reader.onerror = function () {
            AIDE.showToast('Failed to read file: ' + file.name);
        };
        reader.readAsText(file);
    };

    AIDE.renderFilePreviews = function () {
        var container = AIDE.dom.filePreview;
        if (!container) return;
        var pending = state.get('pendingFiles');
        container.innerHTML = '';
        if (pending.length === 0) {
            container.classList.remove('has-files');
            return;
        }
        container.classList.add('has-files');
        pending.forEach(function (f, idx) {
            var chip = document.createElement('div');
            chip.className = 'file-chip file-chip-' + AIDE.fileLanguageCategory(f.language);

            var icon = document.createElement('span');
            icon.className = 'file-chip-icon';
            icon.textContent = '\uD83D\uDCC4';

            var nameSpan = document.createElement('span');
            nameSpan.className = 'file-chip-name';
            nameSpan.textContent = f.name;
            nameSpan.title = f.name;

            var sizeSpan = document.createElement('span');
            sizeSpan.className = 'file-chip-size';
            sizeSpan.textContent = AIDE.formatFileSize(f.size);

            var removeBtn = document.createElement('button');
            removeBtn.className = 'file-chip-remove';
            removeBtn.innerHTML = '\u00D7';
            removeBtn.title = 'Remove';
            removeBtn.setAttribute('aria-label', 'Remove ' + f.name);
            removeBtn.setAttribute('data-idx', idx);
            removeBtn.addEventListener('click', function () {
                var i = parseInt(this.getAttribute('data-idx'), 10);
                state.get('pendingFiles').splice(i, 1);
                AIDE.renderFilePreviews();
            });

            chip.appendChild(icon);
            chip.appendChild(nameSpan);
            chip.appendChild(sizeSpan);
            chip.appendChild(removeBtn);
            container.appendChild(chip);
        });
    };

    // --- Document References ---
    AIDE.handleDocumentReferenced = function (data) {
        if (!data || !data.qualifiedName) return;
        var pending = state.get('pendingDocuments');
        if (pending.length >= 10) {
            AIDE.showToast('Maximum 10 document references per message.');
            return;
        }
        var already = pending.some(function (d) { return d.qualifiedName === data.qualifiedName; });
        if (!already) {
            pending.push({ type: data.type || 'document', qualifiedName: data.qualifiedName });
            AIDE.renderDocumentPreviews();
        }
        AIDE.dom.chatInput.focus();
    };

    AIDE.renderDocumentPreviews = function () {
        var container = document.getElementById('docPreview');
        if (!container) return;
        var pending = state.get('pendingDocuments');
        container.innerHTML = '';
        if (pending.length === 0) {
            container.classList.remove('has-docs');
            return;
        }
        container.classList.add('has-docs');
        pending.forEach(function (doc, idx) {
            var chip = document.createElement('div');
            chip.className = 'doc-chip doc-chip-' + AIDE.safeElementType(doc.type);

            var label = document.createElement('span');
            label.className = 'doc-chip-name';
            label.textContent = '@' + doc.qualifiedName;

            var removeBtn = document.createElement('button');
            removeBtn.className = 'doc-chip-remove';
            removeBtn.innerHTML = '\u00D7';
            removeBtn.title = 'Remove';
            removeBtn.setAttribute('aria-label', 'Remove ' + doc.qualifiedName);
            removeBtn.setAttribute('data-idx', idx);
            removeBtn.addEventListener('click', function () {
                var i = parseInt(this.getAttribute('data-idx'), 10);
                state.get('pendingDocuments').splice(i, 1);
                AIDE.renderDocumentPreviews();
            });

            chip.appendChild(label);
            chip.appendChild(removeBtn);
            container.appendChild(chip);
        });
    };

    AIDE.handleAutoExplain = function (data) {
        if (!data || !data.qualifiedName) return;
        state.set('pendingDocuments', [{ type: data.type || 'document', qualifiedName: data.qualifiedName }]);
        AIDE.dom.chatInput.value = 'Explain @' + data.qualifiedName;
        AIDE.hideWelcome();
        AIDE.sendMessage();
    };

    // --- Drag & Drop Setup ---
    AIDE.setupDragDrop = function (inputArea, chatInput) {
        // Block WebView2 from navigating to dropped files globally
        document.addEventListener('dragover', function (e) {
            e.preventDefault();
            e.stopPropagation();
        });
        document.addEventListener('drop', function (e) {
            e.preventDefault();
            e.stopPropagation();
        });

        // Drag-and-drop on input area
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
            var files = e.dataTransfer && e.dataTransfer.files;
            if (files) {
                for (var i = 0; i < files.length; i++) {
                    var f = files[i];
                    if (f.type && CONST.ALLOWED_IMAGE_TYPES[f.type]) {
                        AIDE.addImageFile(f);
                    } else {
                        AIDE.addTextFile(f);
                    }
                }
            }
        });

        // Paste images or text files from clipboard
        chatInput.addEventListener('paste', function (e) {
            var items = e.clipboardData && e.clipboardData.items;
            if (!items) return;
            for (var i = 0; i < items.length; i++) {
                if (items[i].kind === 'file') {
                    var file = items[i].getAsFile();
                    if (!file) continue;
                    if (CONST.ALLOWED_IMAGE_TYPES[file.type]) {
                        AIDE.addImageFile(file);
                    } else {
                        AIDE.addTextFile(file);
                    }
                }
            }
        });
    };

    // --- File Picker Setup ---
    AIDE.setupFilePicker = function (attachBtn, attachFileInput) {
        attachBtn.addEventListener('click', function () {
            attachFileInput.value = '';
            attachFileInput.click();
        });
        attachFileInput.addEventListener('change', function () {
            var files = attachFileInput.files;
            if (files) {
                for (var i = 0; i < files.length; i++) {
                    var f = files[i];
                    if (f.type && CONST.ALLOWED_IMAGE_TYPES[f.type]) {
                        AIDE.addImageFile(f);
                    } else {
                        AIDE.addTextFile(f);
                    }
                }
            }
            attachFileInput.value = '';
        });
    };

})(window.AIDE = window.AIDE || {});
