namespace BeUtl.Graphics.Transformation;

public abstract class Transform
{
    private bool _isEnabled = true;

    public abstract Matrix Value { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public Drawable? Parent { get; internal set; }

    protected bool SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Parent?.InvalidateVisual();

            return true;
        }
        else
        {
            return false;
        }
    }
}
