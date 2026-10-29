using Beutl.Media;

namespace Beutl;

public sealed partial class SceneCompositor
{
    // Released layer and audio query evaluators. Later queries reevaluate them in place, so their media
    // readers and resource caches survive across frames instead of being reopened for every query.
    private readonly object _queryPoolLock = new();
    private readonly List<LayerQuery> _idleLayerQueries = [];
    private readonly List<AudioQuery> _idleAudioQueries = [];
    private int _queryGeneration;
    private bool _queryPoolsDisposed;

    private sealed class LayerQuery(SceneCompositor compositor, int layer)
    {
        public SceneCompositor Compositor { get; } = compositor;
        public int Layer { get; } = layer;
        public MediaTime Time;
        public int LastUsed;
    }

    private sealed class AudioQuery(SceneComposer composer)
    {
        public SceneComposer Composer { get; } = composer;
        public MediaTime End;
        public int LastUsed;
    }

    // Queries usually move with the frame clock, so the nearest previous time continues a reader's position.
    private LayerQuery RentLayerQuery(int layer, MediaTime time)
    {
        lock (_queryPoolLock)
        {
            int best = -1;
            for (int i = 0; i < _idleLayerQueries.Count; i++)
            {
                if (_idleLayerQueries[i].Layer != layer) continue;
                if (best < 0 || Distance(_idleLayerQueries[i].Time, time) < Distance(_idleLayerQueries[best].Time, time))
                    best = i;
            }

            if (best >= 0)
            {
                LayerQuery query = _idleLayerQueries[best];
                _idleLayerQueries.RemoveAt(best);
                return query;
            }
        }

        return new LayerQuery(
            new SceneCompositor(Scene) { DisableResourceShare = true, ForceOriginalSource = ForceOriginalSource },
            layer);
    }

    private void ReturnLayerQuery(LayerQuery query, MediaTime time)
    {
        lock (_queryPoolLock)
        {
            if (!_queryPoolsDisposed)
            {
                query.Time = time;
                query.LastUsed = _queryGeneration;
                _idleLayerQueries.Add(query);
                return;
            }
        }

        query.Compositor.Dispose();
    }

    // A contiguous request continues the composer's stateful processing as playback does.
    private AudioQuery RentAudioQuery(MediaTime start)
    {
        lock (_queryPoolLock)
        {
            int best = -1;
            for (int i = 0; i < _idleAudioQueries.Count; i++)
            {
                if (best < 0 || Distance(_idleAudioQueries[i].End, start) < Distance(_idleAudioQueries[best].End, start))
                    best = i;
            }

            if (best >= 0)
            {
                AudioQuery query = _idleAudioQueries[best];
                _idleAudioQueries.RemoveAt(best);
                return query;
            }
        }

        return new AudioQuery(new SceneComposer(Scene, disableResourceShare: true, forceOriginalSource: ForceOriginalSource));
    }

    private void ReturnAudioQuery(AudioQuery query, MediaTime end)
    {
        lock (_queryPoolLock)
        {
            if (!_queryPoolsDisposed)
            {
                query.End = end;
                query.LastUsed = _queryGeneration;
                _idleAudioQueries.Add(query);
                return;
            }
        }

        query.Composer.Dispose();
    }

    // Runs once per evaluation of this compositor's own frame. Evaluators no query used during the last two
    // evaluations belong to queries that stopped, so they release their readers.
    internal void BeginQueryGeneration()
    {
        List<IDisposable>? expired = null;
        lock (_queryPoolLock)
        {
            _queryGeneration++;
            int oldest = _queryGeneration - 2;
            for (int i = _idleLayerQueries.Count - 1; i >= 0; i--)
            {
                if (_idleLayerQueries[i].LastUsed >= oldest) continue;
                (expired ??= []).Add(_idleLayerQueries[i].Compositor);
                _idleLayerQueries.RemoveAt(i);
            }

            for (int i = _idleAudioQueries.Count - 1; i >= 0; i--)
            {
                if (_idleAudioQueries[i].LastUsed >= oldest) continue;
                (expired ??= []).Add(_idleAudioQueries[i].Composer);
                _idleAudioQueries.RemoveAt(i);
            }
        }

        if (expired is null) return;
        foreach (IDisposable evaluator in expired)
            evaluator.Dispose();
    }

    private void DisposeQueryPools()
    {
        var idle = new List<IDisposable>();
        lock (_queryPoolLock)
        {
            _queryPoolsDisposed = true;
            idle.AddRange(_idleLayerQueries.Select(static query => query.Compositor));
            idle.AddRange(_idleAudioQueries.Select(static query => query.Composer));
            _idleLayerQueries.Clear();
            _idleAudioQueries.Clear();
        }

        foreach (IDisposable evaluator in idle)
            evaluator.Dispose();
    }

    private static MediaTime Distance(MediaTime a, MediaTime b) => a > b ? a - b : b - a;
}
