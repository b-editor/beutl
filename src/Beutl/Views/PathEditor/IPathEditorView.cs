using Avalonia;
using Avalonia.Controls.Primitives;

using Beutl.Media;

using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Views;

// タブとフレームでBehaviorを共有するため
internal interface IPathEditorView
{
    bool SkipUpdatePosition { get; set; }

    object? DataContext { get; }

    Matrix Matrix { get; }

    double Scale { get; }

    Thumb? FindThumb(PathSegment segment, CoreProperty<BtlPoint> property);

    Thumb[] GetSelectedAnchors();
}
