using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Beutl.AgentToolkit.Installation;

// Edit Tomlyn's lossless syntax tree; serializing the existing model would
// normalize the user's whitespace, comments, quoting, and table layout.
internal sealed class CodexMcpConfigEditor
{
    private readonly DocumentSyntax _document;
    private readonly string _newline;
    private readonly TomlTable _original;
    private readonly TomlTable _desired;
    private readonly string[][] _serverPaths;
    private readonly List<Setting> _pending;
    private readonly List<Container> _containers = [];

    private CodexMcpConfigEditor(string text, TomlTable original, string serversKey, TomlTable servers)
    {
        _document = SyntaxParser.ParseStrict(text);
        _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (_document.KeyValues.ChildrenCount == 0 && _document.Tables.ChildrenCount == 0)
        {
            // Tomlyn stores a comment-only document as trailing trivia. Keep
            // that file header before any tables we add.
            _document.LeadingTrivia = [.. _document.LeadingTrivia ?? [], .. _document.TrailingTrivia ?? []];
            _document.TrailingTrivia = null;
            if (text.Length > 0 && !text.EndsWith('\n'))
                _document.AddLeadingTrivia(new SyntaxTrivia(TokenKind.NewLine, _newline));
        }

        _original = original;
        _desired = new TomlTable { [serversKey] = servers };
        _serverPaths = servers.Keys.Select(name => new[] { serversKey, name }).ToArray();
        _pending = Flatten(_desired, []).ToList();
    }

    public static string Update(string text, TomlTable original, string serversKey, TomlTable servers)
        => new CodexMcpConfigEditor(text, original, serversKey, servers).Edit();

    private string Edit()
    {
        _containers.Add(new Container([], _document.KeyValues));
        VisitItems(_document.KeyValues, []);
        foreach (TableSyntaxBase table in _document.Tables.ToArray())
        {
            string[] path = KeyPath(table.Name!);
            if (!IsRelated(path))
                continue;

            if (IsSelected(path) && (table is TableArraySyntax || !TryGet(_desired, path, out object? value) || value is not TomlTable))
            {
                RemoveNode(_document.Tables, table, table.Tokens().SelectMany(token => token switch
                {
                    SyntaxTrivia trivia => new[] { trivia },
                    SyntaxToken { TokenKind: TokenKind.NewLine } tokenNewline => new[] { new SyntaxTrivia(TokenKind.NewLine, tokenNewline.Text!) },
                    _ => Array.Empty<SyntaxTrivia>(),
                }));
                continue;
            }

            _containers.Add(new Container(path, table.Items) { Table = table });
            VisitItems(table.Items, path);
        }

        foreach (Setting setting in _pending)
        {
            Container container = _containers.Where(c => IsPrefix(c.Path, setting.Path)).MaxBy(c => c.Path.Length)!;
            if (container.Inline is null && container.Path.Length < setting.Path.Length - 1)
            {
                string[] path = setting.Path[..^1];
                var table = new TableSyntax(CreateKey(path)) { EndOfLineToken = NewLine() };
                container = new Container(path, table.Items) { Table = table, IsNewTable = true };
                _containers.Add(container);
            }

            container.Additions.Add(setting);
        }

        foreach (Container container in _containers.Where(c => c.Additions.Count > 0))
        {
            if (container.Inline is { } inline)
            {
                foreach (Setting setting in container.Additions)
                {
                    if (inline.Items.LastOrDefault() is { Comma: null } last)
                        last.Comma = SyntaxFactory.Token(TokenKind.Comma).AddTrailingWhitespace();
                    inline.Items.Add(new InlineTableItemSyntax(CreateAssignment(container, setting, inline: true)));
                }
            }
            else
            {
                List<SyntaxTrivia> suffix = TakeSuffix(container);
                foreach (Setting setting in container.Additions)
                    container.Items!.Add(CreateAssignment(container, setting));
                RestoreSuffix(container, suffix);
            }
        }

        InsertNewTables();
        string updated = _document.ToString();
        SyntaxParser.ParseStrict(updated);
        return updated;
    }

    private void InsertNewTables()
    {
        TableSyntaxBase[] added = _containers.Where(c => c.IsNewTable).Select(c => c.Table!).ToArray();
        if (added.Length == 0)
            return;

        TableSyntaxBase[] existing = _document.Tables.ToArray();
        int index = Array.FindLastIndex(existing, table => _desired.ContainsKey(KeyPath(table.Name!)[0])) + 1;
        bool appendAtEnd = index == 0;
        if (appendAtEnd)
            index = existing.Length;
        Container previous = index == 0 ? _containers[0]
            : new Container([], existing[index - 1].Items) { Table = existing[index - 1] };
        List<SyntaxTrivia> suffix = TakeSuffix(previous);
        // At EOF, existing blank lines already provide the separator. Keep
        // footer/next-section comments after the newly inserted MCP tables.
        if (appendAtEnd || (index == existing.Length && suffix.All(t => t.Kind != TokenKind.Comment)))
        {
            RestoreSuffix(previous, suffix);
            suffix = [];
        }

        SyntaxNode preceding = previous.Table ?? (SyntaxNode)previous.Items!;
        foreach (TableSyntaxBase table in added)
        {
            string precedingText = preceding.ToString();
            if (precedingText.Length > 0 && !precedingText.EndsWith(_newline + _newline, StringComparison.Ordinal))
                table.AddLeadingTrivia(new SyntaxTrivia(TokenKind.NewLine, _newline));
            preceding = table;
        }

        RestoreSuffix(new Container([], added[^1].Items) { Table = added[^1] }, suffix);
        // SyntaxList supports removal/addition; reattach the nodes in order.
        foreach (TableSyntaxBase table in existing)
            _document.Tables.RemoveChild(table);
        foreach (TableSyntaxBase table in existing.Take(index).Concat(added).Concat(existing.Skip(index)))
            _document.Tables.Add(table);
    }

    private void VisitItems(SyntaxList<KeyValueSyntax> items, string[] parent)
    {
        foreach (KeyValueSyntax item in items.ToArray())
        {
            string[] path = [.. parent, .. KeyPath(item.Key!)];
            bool migrate = IsSelected(path) && item.Key!.DotKeys.Any();
            if (migrate || !VisitItem(item, path))
                RemoveNode(items, item, ItemTrivia(item, keepLine: !migrate));
        }
    }

    private bool VisitItem(KeyValueSyntax item, string[] path)
    {
        if (!IsRelated(path))
            return true;
        if (!TryGet(_desired, path, out object? desired))
            return false;
        if (TryGet(_original, path, out object? original) && ValuesEqual(original, desired))
        {
            _pending.RemoveAll(setting => IsPrefix(path, setting.Path));
            return true;
        }

        if (desired is TomlTable && item.Value is InlineTableSyntax inline)
        {
            _containers.Add(new Container(path, null) { Inline = inline });
            bool removed = false;
            foreach (InlineTableItemSyntax child in inline.Items.ToArray())
            {
                if (!VisitItem(child.KeyValue!, [.. path, .. KeyPath(child.KeyValue!.Key!)]))
                {
                    RemoveNode(inline.Items, child, ItemTrivia(child.KeyValue, keepLine: false));
                    removed = true;
                }
            }

            if (removed && inline.Items.LastOrDefault() is { Comma: { } comma } last)
            {
                inline.CloseBrace!.LeadingTrivia = [.. comma.TrailingTrivia ?? [], .. inline.CloseBrace.LeadingTrivia ?? []];
                last.Comma = null;
            }
        }
        else
        {
            ValueSyntax replacement = CreateValue(desired!);
            ValueSyntax current = item.Value!;
            SyntaxToken last = current.Tokens(false).OfType<SyntaxToken>().Last();
            replacement.Tokens(false).OfType<SyntaxToken>().Last().TrailingTrivia = last.TrailingTrivia;
            replacement.LeadingTrivia = current.LeadingTrivia;
            replacement.TrailingTrivia = current.TrailingTrivia;
            item.Value = replacement;
            _pending.RemoveAll(setting => IsPrefix(path, setting.Path));
        }

        return true;
    }

    private KeyValueSyntax CreateAssignment(Container container, Setting setting, bool inline = false)
        => new(CreateKey(setting.Path.Skip(container.Path.Length)), CreateValue(setting.Value))
        {
            EndOfLineToken = inline ? null : NewLine(),
        };

    private List<SyntaxTrivia> TakeSuffix(Container container)
    {
        SyntaxList<KeyValueSyntax> items = container.Items!;
        KeyValueSyntax? last = items.LastOrDefault();
        SyntaxToken? end = last?.EndOfLineToken ?? container.Table?.EndOfLineToken;
        if (last is not null && last.EndOfLineToken is null)
            end = last.EndOfLineToken = NewLine();
        else if (last is null && container.Table is { EndOfLineToken: null } table)
            end = table.EndOfLineToken = NewLine();
        List<SyntaxTrivia> suffix = [.. end?.TrailingTrivia ?? [], .. last?.TrailingTrivia ?? [], .. items.TrailingTrivia ?? []];
        if (end is not null)
            end.TrailingTrivia = null;
        if (last is not null)
            last.TrailingTrivia = null;
        items.TrailingTrivia = null;
        return suffix;
    }

    private static void RestoreSuffix(Container container, List<SyntaxTrivia> suffix)
    {
        SyntaxList<KeyValueSyntax> items = container.Items!;
        SyntaxToken? end = items.LastOrDefault()?.EndOfLineToken ?? container.Table?.EndOfLineToken;
        if (end is not null)
            end.TrailingTrivia = [.. end.TrailingTrivia ?? [], .. suffix];
        else
            items.TrailingTrivia = [.. items.TrailingTrivia ?? [], .. suffix];
    }

    private static IEnumerable<SyntaxTrivia> ItemTrivia(KeyValueSyntax item, bool keepLine)
    {
        ValueSyntax value = item.Value!;
        SyntaxToken last = value.Tokens(false).OfType<SyntaxToken>().Last();
        List<SyntaxTrivia> comment = [.. last.TrailingTrivia ?? [], .. value.TrailingTrivia ?? []];
        return [.. item.LeadingTrivia ?? [], .. comment,
            .. ((keepLine || comment.Any(t => t.Kind == TokenKind.Comment)) && item.EndOfLineToken is { } end
                ? new[] { new SyntaxTrivia(TokenKind.NewLine, end.Text!) } : []),
            .. item.EndOfLineToken?.TrailingTrivia ?? [], .. item.TrailingTrivia ?? []];
    }

    private static void RemoveNode<T>(SyntaxList<T> list, T node, IEnumerable<SyntaxTrivia> trivia) where T : SyntaxNode
    {
        List<SyntaxTrivia> preserved = trivia.ToList();
        T? next = list.SkipWhile(item => !ReferenceEquals(item, node)).Skip(1).FirstOrDefault();
        list.RemoveChild(node);
        if (next is not null)
            next.LeadingTrivia = [.. preserved, .. next.LeadingTrivia ?? []];
        else
            list.TrailingTrivia = [.. preserved, .. list.TrailingTrivia ?? []];
    }

    private SyntaxToken NewLine() => new(TokenKind.NewLine, _newline);

    private bool IsSelected(string[] path) => _serverPaths.Any(server => IsPrefix(server, path));
    private bool IsRelated(string[] path) => IsSelected(path) || _serverPaths.Any(server => IsPrefix(path, server));
    private static bool IsPrefix(string[] prefix, string[] path)
        => prefix.Length <= path.Length && path.Take(prefix.Length).SequenceEqual(prefix);

    private static string[] KeyPath(KeySyntax key)
        => new[] { KeyValue(key.Key!) }.Concat(key.DotKeys.Select(dot => KeyValue(dot.Key!))).ToArray();

    private static string KeyValue(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bare => bare.Key!.Text!,
        StringValueSyntax quoted => quoted.Value!,
        _ => throw new InvalidDataException("Invalid TOML key."),
    };

    private static KeySyntax CreateKey(IEnumerable<string> path)
    {
        // Tomlyn quotes and escapes each segment that is not a bare TOML key.
        string[] keys = path.ToArray();
        var key = new KeySyntax(keys[0]);
        foreach (string part in keys.Skip(1))
            key.DotKeys.Add(new DottedKeyItemSyntax(part));
        return key;
    }

    private static bool TryGet(TomlTable root, string[] path, out object? value)
    {
        value = root;
        foreach (string key in path)
        {
            if (value is not TomlTable table || !table.TryGetValue(key, out value))
                return false;
        }

        return true;
    }

    private static bool ValuesEqual(object? original, object? desired) => desired switch
    {
        TomlTable expected => original is TomlTable actual && actual.Count == expected.Count
                             && expected.All(pair => actual.TryGetValue(pair.Key, out object? value) && ValuesEqual(value, pair.Value)),
        TomlArray expected => original is TomlArray actual && actual.SequenceEqual(expected),
        _ => Equals(original, desired),
    };

    private static IEnumerable<Setting> Flatten(TomlTable table, string[] parent)
    {
        foreach (KeyValuePair<string, object> pair in table)
        {
            string[] path = [.. parent, pair.Key];
            if (pair.Value is TomlTable child)
            {
                foreach (Setting setting in Flatten(child, path))
                    yield return setting;
            }
            else
                yield return new Setting(path, pair.Value);
        }
    }

    private static ValueSyntax CreateValue(object value)
    {
        string text = TomlSerializer.Serialize(new TomlTable { ["value"] = value },
            new TomlSerializerOptions { InlineTablePolicy = TomlInlineTablePolicy.Always });
        KeyValueSyntax item = SyntaxParser.ParseStrict(text).KeyValues.Single();
        ValueSyntax syntax = item.Value!;
        item.Value = null;
        return syntax;
    }

    private sealed record Setting(string[] Path, object Value);

    private sealed class Container(string[] path, SyntaxList<KeyValueSyntax>? items)
    {
        public string[] Path { get; } = path;
        public SyntaxList<KeyValueSyntax>? Items { get; } = items;
        public TableSyntaxBase? Table { get; init; }
        public InlineTableSyntax? Inline { get; init; }
        public bool IsNewTable { get; init; }
        public List<Setting> Additions { get; } = [];
    }
}
