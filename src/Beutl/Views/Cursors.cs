using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Beutl.Views;

public static class Cursors
{
    public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
    public static readonly Cursor Wait = new(StandardCursorType.Wait);
    public static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);
    public static readonly Cursor SizeNorthSouth = new(StandardCursorType.SizeNorthSouth);
    public static readonly Cursor DragMove = new(StandardCursorType.DragMove);
    public static readonly Cursor DragCopy = new(StandardCursorType.DragCopy);
    public static readonly Cursor DragLink = new(StandardCursorType.DragLink);
    public static readonly Cursor Cross = new(StandardCursorType.Cross);
    public static readonly Cursor Hand;
    public static readonly Cursor HandGrab;

    static Cursors()
    {
        using (Stream handStream = AssetLoader.Open(new("avares://Beutl/Assets/cursor-hand.png")))
        using (Stream grabStream = AssetLoader.Open(new("avares://Beutl/Assets/cursor-hand-grab.png")))
        {
            Hand = new Cursor(new Bitmap(handStream), new(12, 12));
            HandGrab = new Cursor(new Bitmap(grabStream), new(12, 12));
        }
    }
}
