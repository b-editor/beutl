namespace Beutl.Editor.Services.LibraryThumbnails;

// Gives one object to a property without making it the property owner's child, so several scenes can hold it.
// Thumbnails are never saved, so it is never written out.
internal sealed class SharedValueExpression<T>(T value) : IExpression<T>
{
    public string ExpressionString => string.Empty;

    public T Evaluate(ExpressionContext context) => value;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }
}
