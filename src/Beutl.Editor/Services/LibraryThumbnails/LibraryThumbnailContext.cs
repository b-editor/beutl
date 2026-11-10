using Beutl.Graphics;

namespace Beutl.Editor.Services.LibraryThumbnails;

/// <summary>
/// What a <see cref="LibraryThumbnailRecipes">recipe</see> sets a thumbnail up with: the picture a filter effect
/// applies to, its size, and how long the thumbnail plays before it starts over.
/// </summary>
/// <remarks>
/// The picture is the project's content at the size it has in the project, such as the frame the preview shows or
/// the object an effect is added to, or the sample when there is no content. An effect applies to it at that size, so
/// a recipe gives values in the project's units, and sizes them to <see cref="Size"/> with <see cref="Scale"/> to show
/// as much on a small object as on a whole frame. The thumbnail shows its middle frame while it is not playing, so a
/// recipe shows the type at its clearest there.
/// </remarks>
public sealed class LibraryThumbnailContext
{
    // The shorter side of the picture a recipe's values are given for.
    private const float ReferenceSize = 1080;

    internal LibraryThumbnailContext(Drawable? subject, MediaTime duration, Size size)
    {
        Subject = subject;
        Duration = duration;
        Size = size;
    }

    /// <summary>
    /// The picture a filter effect applies to, or <see langword="null"/> for a transition. A recipe can move it, for
    /// an effect that only shows on moving content.
    /// </summary>
    public Drawable? Subject { get; }

    /// <summary>
    /// The size of what a filter effect applies to, in the project's units; for a transition, the size of the
    /// frame.
    /// </summary>
    public Size Size { get; }

    /// <summary>How long the thumbnail plays before it starts over.</summary>
    public MediaTime Duration { get; }

    /// <summary>
    /// Scales <paramref name="value"/>, given for a picture 1080 units on its shorter side such as a full HD frame, to
    /// the size of what the effect applies to.
    /// </summary>
    public float Scale(float value) => value * Math.Min(Size.Width, Size.Height) / ReferenceSize;

    /// <summary>
    /// Makes <paramref name="property"/> take the value <paramref name="valueAt"/> gives for how far the thumbnail has
    /// played, from 0 at its start to 1 at its end.
    /// </summary>
    /// <remarks>
    /// The value follows time like a keyframe animation does, so an effect that reads its content at other times,
    /// such as a motion blur, sees it move. <paramref name="valueAt"/> runs on the render thread.
    /// </remarks>
    public void Animate<T>(IExpressionProperty<T> property, Func<float, T> valueAt)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(valueAt);
        property.Expression = new ProgressExpression<T>(valueAt, Duration);
    }

    /// <summary>
    /// Rises from 0 at the start to 1 at the middle and falls back to 0 at the end, easing in and out, so a value
    /// animated with it peaks in the frame shown at rest and loops without a jump.
    /// </summary>
    public static float Wave(float progress) => 0.5f - (0.5f * MathF.Cos(progress * MathF.Tau));

    // A function of the thumbnail's progress, standing in for keyframes. Thumbnails are never saved, so it is never
    // written out.
    private sealed class ProgressExpression<T>(Func<float, T> valueAt, MediaTime duration) : IExpression<T>
    {
        public string ExpressionString => string.Empty;

        public T Evaluate(ExpressionContext context)
        {
            double length = duration.ToSeconds();
            double time = context.Time.ToSeconds();
            double progress = length > 0 ? ((time % length) + length) % length / length : 0;
            return valueAt((float)progress);
        }

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }
}
