using Beutl.Audio;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed partial class SceneCompositor
{
    private sealed class EvaluationServices(SceneCompositor owner, CompositionContext conditions,
        HashSet<(int? Layer, TimeRange Range)>? queryPath = null) : ICompositionEvaluationServices
    {
        // Samples own their resources but share the active query path with their originating evaluation.
        private readonly HashSet<(int? Layer, TimeRange Range)> _queryPath = queryPath ?? [];
        private readonly Dictionary<(int Layer, MediaTime Time), IReadOnlyList<EngineObject.Resource>> _queries = [];
        private const int MaxQueryDepth = 32;

        public EvaluationServices Fork(SceneCompositor compositor, CompositionContext context)
            => new(compositor, context, _queryPath);

        internal CompositionFrame? Frame { get; set; }

        public object SessionIdentity => owner;

        public PixelSize FrameSize => owner.Scene.FrameSize.Value;

        public Rational FrameRate => conditions.FrameRate;

        public bool TryReadFrame<T>(Func<CompositionFrame, T> read, out T result)
        {
            ArgumentNullException.ThrowIfNull(read);
            if (Frame is { } frame)
            {
                result = read(frame);
                return true;
            }
            result = default!;
            return false;
        }

        public IReadOnlyList<CompositionLayerInfo> GetLayers(MediaTime time)
        {
            using var elements = new PooledList<Element>();
            owner.SortLayers(time, elements, CompositionTarget.Graphics);
            return elements.Select(x => new CompositionLayerInfo(x.Id.Value, x.ZIndex.Value, x.Range)).ToArray();
        }

        public CompositionLayerLease EvaluateLayer(int zIndex, MediaTime time)
        {
            conditions.CancellationToken.ThrowIfCancellationRequested();
            var key = (zIndex, time);
            var range = new TimeRange(time, MediaTime.Zero);
            EnterQuery(zIndex, range);
            var compositor = new SceneCompositor(owner.Scene)
            {
                DisableResourceShare = true,
                ForceOriginalSource = owner.ForceOriginalSource
            };
            void Release()
            {
                try { compositor.Dispose(); }
                finally
                {
                    _queries.Remove(key);
                    _queryPath.Remove((zIndex, range));
                }
            }
            try
            {
                CompositionFrame frame = compositor.EvaluateGraphics(time, zIndex, this, conditions, conditions.CancellationToken, conditions.FrameRate);
                _queries[key] = frame.Objects;
                return new CompositionLayerLease(frame.Objects, Release);
            }
            catch
            {
                Release();
                throw;
            }
        }

        public T ReadLayer<T>(int zIndex, MediaTime time, Func<IReadOnlyList<EngineObject.Resource>, T> read)
        {
            ArgumentNullException.ThrowIfNull(read);
            if (_queries.TryGetValue((zIndex, time), out var resources))
                return read(resources);
            using var layer = EvaluateLayer(zIndex, time);
            return read(layer.Resources);
        }

        public AudioBuffer? ReadAudio(MediaTime start, MediaTime duration)
        {
            conditions.CancellationToken.ThrowIfCancellationRequested();
            if (duration <= MediaTime.Zero) return null;
            var range = new TimeRange(start, duration);
            EnterQuery(null, range);
            try
            {
                using var composer = new SceneComposer(owner.Scene, disableResourceShare: true,
                    forceOriginalSource: owner.ForceOriginalSource);
                CompositionFrame frame = composer.Compositor.EvaluateAudio(range, conditions);
                return composer.Compose(range, frame);
            }
            finally { _queryPath.Remove((null, range)); }
        }

        private void EnterQuery(int? layer, TimeRange range)
        {
            if (_queryPath.Contains((layer, range)))
            {
                string target = layer.HasValue ? $"layer {layer.Value}, time {range.Start}" : $"audio range {range}";
                throw new InvalidOperationException($"Circular composition query at {target}.");
            }
            if (_queryPath.Count >= MaxQueryDepth)
                throw new InvalidOperationException($"Composition queries exceeded {MaxQueryDepth} nested queries.");
            _queryPath.Add((layer, range));
        }
    }
}
