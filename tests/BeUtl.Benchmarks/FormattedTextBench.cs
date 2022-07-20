using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

using BeUtl.Graphics;
using BeUtl.Graphics.Shapes;
using BeUtl.Media.TextFormatting;

using Compat = BeUtl.Media.TextFormatting.Compat;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart)]
public class FormattedTextBench
{
    static FormattedTextBench()
    {

    }

    private const string Str = @"<b>吾輩</b><size=70>は</size><#ff0000>猫</#><size=70>である。</size>
<i>名前</i><size=70>はまだ</size>無<size=70>い。</cspace>

<font='Roboto'>Roboto</font>
<noparse><font='Noto Sans JP'><bold>Noto Sans</font></bold></noparse>";
    [Benchmark]
    public void Old()
    {
#pragma warning disable CS0618
        var ft = Compat.FormattedText.Parse(Str, FormattedTextInfo.Default);
        ft.Measure(Size.Infinity);

        using var canvas = new Canvas((int)ft.Bounds.Width, (int)ft.Bounds.Height);

        ft.Draw(canvas);
#pragma warning restore CS0618
    }

    [Benchmark]
    public void New()
    {
        //var tokenizer = new FormattedTextTokenizer(Str);
        //tokenizer.Tokenize();

        //var builder = new TextElementsBuilder(FormattedTextInfo.Default);
        //builder.AppendTokens(CollectionsMarshal.AsSpan(tokenizer.Result));
        //ReadOnlySpan<TextElement> elements = builder.Items;

        var tb = new TextBlock
        {
            Size = FormattedTextInfo.Default.Size,
            Foreground = FormattedTextInfo.Default.Brush,
            Text = Str
        };
        tb.Measure(Size.Infinity);

        using var canvas = new Canvas((int)tb.Bounds.Width, (int)tb.Bounds.Height);

        tb.Draw(canvas);
    }
}
