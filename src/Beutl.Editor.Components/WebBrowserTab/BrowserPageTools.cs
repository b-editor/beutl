using System.Text.Json;

namespace Beutl.Editor.Components.WebBrowserTab;

internal static class BrowserPageTools
{
    internal sealed record FindResult(int Index, int Count);

    internal static string FindScript(string query, int direction) =>
        "(" + FindFunction + ")(" + JsonSerializer.Serialize(query) + "," + Math.Clamp(direction, -1, 1) + ")";

    internal static FindResult? ParseFindResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        try
        {
            using var outer = JsonDocument.Parse(result);
            string json = outer.RootElement.ValueKind == JsonValueKind.String ? outer.RootElement.GetString()! : result;
            return JsonSerializer.Deserialize<FindResult>(json);
        }
        catch (JsonException) { return null; }
    }

    internal static string ZoomScript(int percent) => """
        (() => {
            if (!document.documentElement || !CSS.supports('zoom', '1.2')) return JSON.stringify(false);
            const style = document.documentElement.style;
            const percent =
        """ + Math.Clamp(percent, 50, 200) + ";" + """
            if (!window.__beutlZoom && percent !== 100)
                window.__beutlZoom = {value:style.getPropertyValue('zoom'),priority:style.getPropertyPriority('zoom')};
            if (percent === 100) {
                const original = window.__beutlZoom;
                if (original) {
                    if (original.value) style.setProperty('zoom',original.value,original.priority);
                    else style.removeProperty('zoom');
                    delete window.__beutlZoom;
                }
            } else style.setProperty('zoom', String(percent / 100), 'important');
            return JSON.stringify(true);
        })()
        """;

    private const string FindFunction = """
        function(query, direction) {
            const previous = window.__beutlFind;
            function clearSelection() {
                const selection = window.getSelection();
                if (previous?.active && selection?.rangeCount) {
                    const range = selection.getRangeAt(0), old = previous.active;
                    if (range.startContainer === old.startContainer && range.startOffset === old.startOffset &&
                        range.endContainer === old.endContainer && range.endOffset === old.endOffset) selection.removeAllRanges();
                }
            }
            if (window.CSS?.highlights) {
                CSS.highlights.delete('beutl-find'); CSS.highlights.delete('beutl-find-active');
            }
            if (!query || !document.body) {
                clearSelection();
                document.getElementById('__beutlFindStyle')?.remove();
                delete window.__beutlFind;
                return JSON.stringify({Index:0,Count:0});
            }
            let text = '', lastBlock = null;
            const nodes = [];
            const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
            let node;
            while (node = walker.nextNode()) {
                const parent = node.parentElement;
                if (!parent || !node.nodeValue || parent.closest('script,style,noscript,template,textarea,input,select,[hidden]')) continue;
                if (getComputedStyle(parent).visibility !== 'visible') continue;
                const visible = document.createRange(); visible.selectNodeContents(node);
                if (!visible.getClientRects().length) continue;
                let block = parent;
                while (block.parentElement && ['inline','contents'].includes(getComputedStyle(block).display)) block = block.parentElement;
                if (lastBlock && block !== lastBlock) text += '\n';
                nodes.push({node,start:text.length,end:text.length+node.nodeValue.length});
                text += node.nodeValue;
                lastBlock = block;
            }
            const specials = '.*+?^${}()|[]' + String.fromCharCode(92);
            const pattern = Array.from(query, c => specials.includes(c) ? String.fromCharCode(92) + c : c).join('');
            const regex = new RegExp(pattern, 'giu');
            const ranges = [];
            let nodeIndex = 0;
            let match;
            while ((match = regex.exec(text)) !== null) {
                while (nodeIndex < nodes.length && nodes[nodeIndex].end <= match.index) nodeIndex++;
                const start = nodes[nodeIndex];
                let endIndex = nodeIndex;
                while (endIndex < nodes.length && nodes[endIndex].end < match.index + match[0].length) endIndex++;
                const end = nodes[endIndex];
                if (!start || !end) continue;
                const range = document.createRange();
                range.setStart(start.node, match.index - start.start);
                range.setEnd(end.node, match.index + match[0].length - end.start);
                ranges.push(range);
            }
            let index = previous?.query === query ? previous.index + direction : 0;
            index = ranges.length ? ((index % ranges.length) + ranges.length) % ranges.length : 0;
            const active = ranges[index];
            clearSelection();
            if (window.CSS?.highlights && typeof Highlight === 'function') {
                let style = document.getElementById('__beutlFindStyle');
                if (!style) { style = document.createElement('style'); style.id = '__beutlFindStyle'; document.head.appendChild(style); }
                style.textContent = '::highlight(beutl-find){background:#ffe066;color:#111}::highlight(beutl-find-active){background:#ff9632;color:#111}';
                const highlights = new Highlight();
                ranges.forEach(range => highlights.add(range));
                CSS.highlights.set('beutl-find', highlights);
                if (active) CSS.highlights.set('beutl-find-active', new Highlight(active));
            } else if (active) {
                const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(active);
            }
            if (active) { const rect = active.getBoundingClientRect(); window.scrollBy(0,rect.top-window.innerHeight/2); }
            window.__beutlFind = {query,index,active};
            return JSON.stringify({Index:active ? index+1 : 0,Count:ranges.length});
        }
        """;
}
