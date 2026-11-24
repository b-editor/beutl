namespace Beutl.Editor.Components.WebBrowserTab;

internal static class BrowserReferrerPolicyScript
{
    internal const string Source = """
        (() => {
            if (!['https:', 'http:'].includes(location.protocol) || self.origin === 'null'
                || typeof window.__beutlReferrerPolicy === 'function') return;

            const policyValue = document.createElement('a');
            const legacy = { never: 'no-referrer', always: 'unsafe-url', default: 'strict-origin-when-cross-origin',
                'origin-when-crossorigin': 'origin-when-cross-origin' };
            let documentPolicy = '';

            function applyChanges(records) {
                if (!records.length) return;
                const attributes = new Map();
                const parents = new Map();
                const lastChildren = new Map();
                const previousSiblings = new Map();
                const parentOf = node => parents.has(node) ? parents.get(node) : node.parentNode;
                function inDocument(node) {
                    while (node && node !== document) node = parentOf(node);
                    return node === document;
                }
                function applyMeta(meta) {
                    const {name, content} = attributes.get(meta) ?? meta;
                    if (name.toLowerCase() !== 'referrer') return false;
                    const value = content.replace(/^[\t\n\f\r ]+|[\t\n\f\r ]+$/g, '').toLowerCase();
                    policyValue.referrerPolicy = Object.hasOwn(legacy, value) ? legacy[value] : value;
                    if (!policyValue.referrerPolicy) return false;
                    documentPolicy = policyValue.referrerPolicy;
                    return true;
                }
                function* metasInReverse(root) {
                    let node = root;
                    while (true) {
                        const child = lastChildren.has(node) ? lastChildren.get(node) : node.lastChild;
                        if (child) {
                            node = child;
                            continue;
                        }
                        while (true) {
                            if (node instanceof HTMLMetaElement) yield node;
                            if (node === root) return;
                            const sibling = previousSiblings.has(node) ? previousSiblings.get(node) : node.previousSibling;
                            if (sibling) {
                                node = sibling;
                                break;
                            }
                            node = parentOf(node);
                        }
                    }
                }

                // Find the last applied policy by undoing later mutations in the batch. An inserted meta
                // can already be detached or changed when the observer runs, even inside an inserted subtree.
                for (let i = records.length - 1; i >= 0; i--) {
                    const record = records[i], target = record.target;
                    if (record.type === 'attributes') {
                        if (!(target instanceof HTMLMetaElement)) continue;
                        if (inDocument(target) && applyMeta(target)) return;
                        // Both engines reapply the policy on a media change without evaluating the query.
                        if (record.attributeName === 'media') continue;
                        const state = attributes.get(target) ?? { name: target.name, content: target.content };
                        state[record.attributeName] = record.oldValue ?? '';
                        attributes.set(target, state);
                        continue;
                    }

                    if (record.addedNodes.length && inDocument(target)) {
                        for (let j = record.addedNodes.length - 1; j >= 0; j--) {
                            for (const meta of metasInReverse(record.addedNodes[j]))
                                if (applyMeta(meta)) return;
                        }
                    }
                    for (const node of record.addedNodes) parents.set(node, null);
                    let previous = record.previousSibling;
                    for (const node of record.removedNodes) {
                        parents.set(node, target);
                        previousSiblings.set(node, previous);
                        previous = node;
                    }
                    if (record.nextSibling) previousSiblings.set(record.nextSibling, previous);
                    else lastChildren.set(target, previous);
                }
            }
            const observer = new MutationObserver(applyChanges);
            observer.observe(document, {subtree:true, childList:true, attributes:true, attributeOldValue:true, attributeFilter:['name','content','media']});
            Object.defineProperty(window, '__beutlReferrerPolicy', {
                value: () => {
                    applyChanges(observer.takeRecords());
                    return documentPolicy;
                }
            });
        })();
        """;
}
