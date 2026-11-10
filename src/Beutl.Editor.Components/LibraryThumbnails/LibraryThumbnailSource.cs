using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Beutl.Controls;
using Beutl.Editor.Services.LibraryThumbnails;

namespace Beutl.Editor.Components.LibraryThumbnails;

// The moving thumbnail of one library type in a set, as the views see it. It counts who asks for its still and its
// frames, and its set draws what is asked for and drops the frames no one plays. Once the set's content changes it
// keeps showing what it has until it is drawn again. Used on the UI thread.
internal sealed class LibraryThumbnailSource(LibraryThumbnailSet set, Type type, LibraryThumbnailLayout layout)
    : IAnimatedThumbnailSource
{
    private int _stillRequests;
    private int _framesRequests;
    private Bitmap? _still;
    private int _stillGeneration;
    private Bitmap? _sheet;
    private int _sheetGeneration;
    private AnimatedThumbnailFrames? _frames;
    private int _failedGeneration = -1;

    public Type Type => type;

    public LibraryThumbnailLayout Layout => layout;

    public IImage? Still => _still;

    public AnimatedThumbnailFrames? Frames => _frames;

    // Moves when what the set shows types on changes, so a drawing started before is not taken for the new one.
    public int Generation { get; private set; }

    // Whether the type could not be drawn on the set's current content. It is not tried again until that changes.
    public bool Failed => _failedGeneration == Generation;

    public bool WantsStill => !Failed && _stillRequests > 0 && (_still == null || _stillGeneration != Generation);

    public bool WantsFrames => !Failed && _framesRequests > 0 && (_sheet == null || _sheetGeneration != Generation);

    public bool IsPlayed => _framesRequests > 0;

    public bool HasSheet => _sheet != null;

    public event EventHandler? Changed;

    public IDisposable RequestStill()
    {
        _stillRequests++;
        if (WantsStill)
            set.Schedule(this);

        return Disposable.Create(() => _stillRequests--);
    }

    public IDisposable RequestFrames()
    {
        _framesRequests++;
        if (WantsFrames)
            set.Schedule(this);

        return Disposable.Create(() =>
        {
            _framesRequests--;
            if (_framesRequests == 0)
                set.OnStoppedPlaying(this);
        });
    }

    // Takes what a drawing made, replacing what was shown, with the layout it was drawn in. A drawing for an earlier
    // generation is disposed.
    public void SetLoaded(int generation, Bitmap still, Bitmap? sheet, LibraryThumbnailLayout drawn)
    {
        if (generation != Generation)
        {
            still.Dispose();
            sheet?.Dispose();
            return;
        }

        Bitmap? oldStill = _still;
        _still = still;
        _stillGeneration = generation;

        Bitmap? oldSheet = null;
        if (sheet != null)
        {
            oldSheet = _sheet;
            _sheet = sheet;
            _sheetGeneration = generation;
            _frames = new AnimatedThumbnailFrames(
                sheet,
                new Size(LibraryThumbnailLayout.FrameSize.Width, LibraryThumbnailLayout.FrameSize.Height),
                LibraryThumbnailLayout.Columns,
                drawn.FrameCount,
                drawn.StillIndex,
                drawn.FrameDuration);
            set.OnSheetLoaded(this);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        oldStill?.Dispose();
        oldSheet?.Dispose();
    }

    public void SetFailed(int generation)
    {
        if (generation != Generation)
            return;

        _failedGeneration = generation;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // The set's content changed: what is shown stays until it is drawn again.
    public void MarkStale()
    {
        Generation++;
    }

    // Frees the frames once nothing plays them; the still stays, and playing again draws them again.
    public void DropSheet()
    {
        if (_sheet == null)
            return;

        Bitmap sheet = _sheet;
        _sheet = null;
        _frames = null;
        Changed?.Invoke(this, EventArgs.Empty);
        sheet.Dispose();
    }

    // Leaves the source empty for good, for a set that closes or a type that unloads.
    public void Release()
    {
        Generation++;
        _failedGeneration = Generation;
        Bitmap? still = _still;
        Bitmap? sheet = _sheet;
        _still = null;
        _sheet = null;
        _frames = null;
        Changed?.Invoke(this, EventArgs.Empty);
        still?.Dispose();
        sheet?.Dispose();
    }
}
