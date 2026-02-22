// ============================================================================
// AIDE Lite - Markdown Rendering & Formatting
// Pure rendering functions — no state dependencies.
// ============================================================================
(function (AIDE) {
    'use strict';

    AIDE.escapeHtml = function (text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    };

    AIDE.renderMarkdown = function (text) {
        if (!text) return '';

        var codeBlocks = [];
        var html = text;

        html = html.replace(/```(\w*)\n([\s\S]*?)```/g, function (m, lang, code) {
            var idx = codeBlocks.length;
            var escaped = AIDE.escapeHtml(code);
            codeBlocks.push(
                '<div class="code-block-wrapper">' +
                '<button class="code-copy-btn" title="Copy code" onclick="AIDE.copyCodeBlock(this)">&#x2398;</button>' +
                '<pre><code class="language-' + (lang || '') + '">' + escaped + '</code></pre>' +
                '</div>'
            );
            return '\x00CODEBLOCK' + idx + '\x00';
        });

        html = html.replace(/`([^`]+)`/g, function (m, code) {
            var idx = codeBlocks.length;
            codeBlocks.push('<code>' + AIDE.escapeHtml(code) + '</code>');
            return '\x00CODEBLOCK' + idx + '\x00';
        });

        html = AIDE.escapeHtml(html);

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
        html = html.replace(/<p>\s*(<div class="code-block-wrapper">)/g, '$1');
        html = html.replace(/(<\/div>)\s*<\/p>/g, '$1');
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
    };

    AIDE.inlineStylesForCopy = function (clone) {
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
    };

    AIDE.copyCodeBlock = function (btn) {
        var wrapper = btn.closest('.code-block-wrapper');
        if (!wrapper) return;
        var code = wrapper.querySelector('code');
        if (!code) return;
        var text = code.textContent;
        navigator.clipboard.writeText(text).then(function () {
            btn.textContent = '\u2713';
            setTimeout(function () { btn.innerHTML = '&#x2398;'; }, 1500);
        }).catch(function () {
            btn.textContent = '!';
            setTimeout(function () { btn.innerHTML = '&#x2398;'; }, 1500);
        });
    };

    AIDE.addCopyButton = function (div, markdown) {
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
            AIDE.inlineStylesForCopy(clone);

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
    };

})(window.AIDE = window.AIDE || {});
