using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Composition;

/// <summary>Read-only queries against the scene being evaluated, including export snapshots.</summary>
public interface ICompositionEvaluationServices
{
    object SessionIdentity { get; }
    /// <summary>Identifies one frame evaluation, including all its dependent layer queries.</summary>
    object FrameIdentity => this;
    PixelSize FrameSize { get; }
    Rational FrameRate { get; }
    IReadOnlyList<CompositionLayerInfo> GetLayers(MediaTime time);
    /// <summary>Evaluates one layer independently of the frame being evaluated.</summary>
    /// <remarks>An implementation may reevaluate a released lease's resources in place for a later query,
    /// so their media readers stay open across frames.</remarks>
    CompositionLayerLease EvaluateLayer(int zIndex, MediaTime time);
    /// <summary>Reads evaluated properties without creating a nested render dependency.</summary>
    /// <remarks>The callback must not retain or dispose resources. Their lifetime is limited to the callback.
    /// An implementation may reuse a layer lease already active on the current evaluation path.</remarks>
    T ReadLayer<T>(int zIndex, MediaTime time, Func<IReadOnlyList<EngineObject.Resource>, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        using var layer = EvaluateLayer(zIndex, time);
        return read(layer.Resources);
    }

    /// <summary>Reads the completed graphics frame that owns these services, when available.</summary>
    /// <remarks>Use this to inspect evaluated containment, including flow and time controllers.
    /// The callback must not retain or dispose resources. Returns false before evaluation completes,
    /// or when the provider has no graphics frame.</remarks>
    bool TryReadFrame<T>(Func<CompositionFrame, T> read, out T result)
    {
        ArgumentNullException.ThrowIfNull(read);
        result = default!;
        return false;
    }

    /// <summary>Composes the scene's audio for a range into a buffer the caller owns.</summary>
    /// <remarks>A range contiguous with an earlier query may continue its stateful processing, as playback
    /// does; any other range starts from a cleared state.</remarks>
    AudioBuffer? ReadAudio(MediaTime start, MediaTime duration);
}

public sealed record CompositionLayerInfo(Guid Id, int ZIndex, TimeRange TimeRange);

/// <summary>An owned evaluation. Disposing it ends the lease; its resources must not be used afterwards.</summary>
public sealed class CompositionLayerLease(IReadOnlyList<EngineObject.Resource> resources, Action release) : IDisposable
{
    private Action? _release = release ?? throw new ArgumentNullException(nameof(release));
    private readonly IReadOnlyList<EngineObject.Resource> _resources = resources
        ?? throw new ArgumentNullException(nameof(resources));

    public IReadOnlyList<EngineObject.Resource> Resources
    {
        get
        {
            ObjectDisposedException.ThrowIf(_release is null, this);
            return _resources;
        }
    }
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
