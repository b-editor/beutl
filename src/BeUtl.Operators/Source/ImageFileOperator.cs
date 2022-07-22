using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Graphics;
using BeUtl.Streaming;
using BeUtl.Styling;
using BeUtl.Language;

namespace BeUtl.Operators.Source;

public sealed class ImageFileOperator : StreamStyledSource
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<ImageFile>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<FileInfo?>(ImageFile.SourceFileProperty)
        {
            DefaultValue = null,
            Header = StringResources.Common.FileObservable
        });
    }
}
