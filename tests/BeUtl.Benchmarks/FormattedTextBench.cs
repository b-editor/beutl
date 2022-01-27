
using BenchmarkDotNet.Attributes;

using BeUtl.Benchmarks.Media.TextFormatting;
using BeUtl.Media;
using BeUtl.Media.TextFormatting;

[MemoryDiagnoser]
public class FormattedTextBench
{
    private const string Str = @"
<b>吾輩</b><size=70>は</size><#ff0000>猫</#><size=70>である。</size>
<i>名前</i><size=70>はまだ</size>無<size=70>い。</cspace>

<font='Roboto'>Roboto</font>
<noparse><font='Noto Sans JP'><bold>Noto Sans</font></bold></noparse>
";
    [Benchmark]
    public void Old()
    {
        var tokenizer = new FormattedTextTokenizer(Str);
        var tokens = tokenizer.Tokenize();
    }

    [Benchmark]
    public void New()
    {
        var parser = new FormattedTextParser(Str);
        var tokens = parser.Tokenize();
    }
}
