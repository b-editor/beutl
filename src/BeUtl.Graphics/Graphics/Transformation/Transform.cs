using System.Runtime.CompilerServices;

namespace BEditorNext.Graphics.Transformation;

public abstract class Transform
{
    internal Drawable? _drawable;

    public abstract Matrix Value { get; }

    protected bool SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _drawable?.InvalidateVisual();

            return true;
        }
        else
        {
            return false;
        }
    }
}
