using Avalonia.Controls;
using Avalonia.Metadata;

namespace Beutl.Controls.Behaviors;

public class SharedContent : Control
{
    [Content]
    public object Content { get; set; }
}
