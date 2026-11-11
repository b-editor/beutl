using Avalonia;
using Avalonia.Media;

namespace Beutl.Controls;

/// <summary>
/// A picture that moves: a frame to show at rest and frames to play, which the source loads only while an
/// <see cref="AnimatedThumbnail"/> asks for them.
/// </summary>
/// <remarks>Its members are used on the UI thread.</remarks>
public interface IAnimatedThumbnailSource
{
    /// <summary>The frame shown at rest, or <see langword="null"/> until it is loaded.</summary>
    IImage? Still { get; }

    /// <summary>The frames to play, or <see langword="null"/> until they are loaded.</summary>
    AnimatedThumbnailFrames? Frames { get; }

    /// <summary>Raised when <see cref="Still"/> or <see cref="Frames"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Asks for <see cref="Still"/> until the returned handle is disposed.</summary>
    IDisposable RequestStill();

    /// <summary>Asks for <see cref="Frames"/> until the returned handle is disposed.</summary>
    IDisposable RequestFrames();
}

/// <summary>
/// Frames of one size, laid out left to right and then top to bottom in one image, with how fast they play.
/// </summary>
public sealed class AnimatedThumbnailFrames
{
    public AnimatedThumbnailFrames(
        IImage sheet, Size frameSize, int columns, int count, int firstIndex, TimeSpan frameDuration)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(firstIndex, count);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frameDuration, TimeSpan.Zero);
        Sheet = sheet;
        FrameSize = frameSize;
        Columns = columns;
        Count = count;
        FirstIndex = firstIndex;
        FrameDuration = frameDuration;
    }

    public IImage Sheet { get; }

    /// <summary>The size of one frame in the sheet, in the units of <see cref="IImage.Size"/>.</summary>
    public Size FrameSize { get; }

    public int Columns { get; }

    public int Count { get; }

    /// <summary>The frame playing starts from: the one shown at rest, so that starting does not jump.</summary>
    public int FirstIndex { get; }

    public TimeSpan FrameDuration { get; }

    public Rect GetSourceRect(int index)
    {
        return new Rect(
            index % Columns * FrameSize.Width,
            index / Columns * FrameSize.Height,
            FrameSize.Width,
            FrameSize.Height);
    }
}
