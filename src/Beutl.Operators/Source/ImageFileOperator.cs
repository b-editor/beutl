using Beutl.Graphics;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class ImageFileOperator : DrawablePublishOperator<ImageFile>
{
    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<FileInfo?>(ImageFile.SourceFileProperty, null));
    }
}
