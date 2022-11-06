using Beutl.Graphics;
using Beutl.Streaming;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class ImageFileOperator : StreamStyledSource
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<ImageFile>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<FileInfo?>(ImageFile.SourceFileProperty, null));
    }
}
