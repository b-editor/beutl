using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;

using BeUtl.Media.TextFormatting;

[MemoryDiagnoser]
public class FormattedTextBench
{
    private const string Str = @"<b>吾輩</b><size=70>は</size><#ff0000>猫</#><size=70>である。</size>
<i>名前</i><size=70>はまだ</size>無<size=70>い。</cspace>

<font='Roboto'>Roboto</font>
<noparse><font='Noto Sans JP'><bold>Noto Sans</font></bold></noparse>";
    [Benchmark]
    public void Old()
    {
        var parser = new FormattedTextParser(Str);
        var lines = parser.ToLines(FormattedTextInfo.Default);
    }

    [Benchmark]
    public void New()
    {
        var tokenizer = new FormattedTextTokenizer(Str);
        tokenizer.Tokenize();

        var elements = FormattedTextParser.ToElements(FormattedTextInfo.Default, CollectionsMarshal.AsSpan(tokenizer.Result));
    }
}
