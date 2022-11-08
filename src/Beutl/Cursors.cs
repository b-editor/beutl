using Avalonia.Input;

namespace Beutl;

public static class Cursors
{
    public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
    public static readonly Cursor Wait = new(StandardCursorType.Wait);
    public static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);
    public static readonly Cursor SizeNorthSouth = new(StandardCursorType.SizeNorthSouth);
    public static readonly Cursor DragMove = new(StandardCursorType.DragMove);
    public static readonly Cursor DragCopy = new(StandardCursorType.DragCopy);
    public static readonly Cursor DragLink = new(StandardCursorType.DragLink);
}
