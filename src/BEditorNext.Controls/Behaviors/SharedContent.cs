using Avalonia.Controls;
using Avalonia.Metadata;

namespace BEditorNext.Controls.Behaviors;

public class SharedContent : Control
{
    [Content]
    public object Content { get; set; }
}
