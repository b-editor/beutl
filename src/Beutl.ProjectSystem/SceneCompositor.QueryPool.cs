using Beutl.Media;

namespace Beutl;

public sealed partial class SceneCompositor
{
    // Released layer and audio query evaluators. Later queries reevaluate them in place, so their media
    // readers and resource caches survive across frames instead of being reopened for every query.
    private readonly object _queryPoolLock = new();
    private readonly QueryPool<SceneCompositor> _layerQueries;
    private readonly QueryPool<SceneComposer> _audioQueries;
    private int _queryGeneration;
    private bool _queryPoolsDisposed;

    // The idle evaluators of one kind, guarded by the owner's lock. Layer queries usually move with the frame clock,
    // and a contiguous audio request continues the composer's stateful processing as playback does, so a query
    // takes the evaluator whose last position is nearest its own.
    private sealed class QueryPool<T>(SceneCompositor owner, Func<T> create) where T : class, IDisposable
    {
        private readonly List<(T Evaluator, int? Layer, MediaTime Position, int LastUsed)> _idle = [];

        public T Rent(int? layer, MediaTime position)
        {
            lock (owner._queryPoolLock)
            {
                int best = -1;
                for (int i = 0; i < _idle.Count; i++)
                {
                    if (_idle[i].Layer != layer) continue;
                    if (best < 0 || Distance(_idle[i].Position, position) < Distance(_idle[best].Position, position))
                        best = i;
                }

                if (best >= 0)
                {
                    T evaluator = _idle[best].Evaluator;
                    _idle.RemoveAt(best);
                    return evaluator;
                }
            }

            return create();
        }

        public void Return(T evaluator, int? layer, MediaTime position)
        {
            lock (owner._queryPoolLock)
            {
                if (!owner._queryPoolsDisposed)
                {
                    _idle.Add((evaluator, layer, position, owner._queryGeneration));
                    return;
                }
            }

            evaluator.Dispose();
        }

        // Expire and Drain run under the owner's lock.
        public void Expire(int oldest, ref List<IDisposable>? expired)
        {
            for (int i = _idle.Count - 1; i >= 0; i--)
            {
                if (_idle[i].LastUsed >= oldest) continue;
                (expired ??= []).Add(_idle[i].Evaluator);
                _idle.RemoveAt(i);
            }
        }

        public void Drain(List<IDisposable> evaluators)
        {
            evaluators.AddRange(_idle.Select(static entry => entry.Evaluator));
            _idle.Clear();
        }
    }

    private SceneCompositor CreateLayerQuery()
        => new(Scene) { DisableResourceShare = true, ForceOriginalSource = ForceOriginalSource };

    private SceneComposer CreateAudioQuery()
        => new(Scene, disableResourceShare: true, forceOriginalSource: ForceOriginalSource);

    // Runs once per evaluation of this compositor's own frame. Evaluators no query used during the last two
    // evaluations belong to queries that stopped, so they release their readers.
    internal void BeginQueryGeneration()
    {
        List<IDisposable>? expired = null;
        lock (_queryPoolLock)
        {
            _queryGeneration++;
            int oldest = _queryGeneration - 2;
            _layerQueries.Expire(oldest, ref expired);
            _audioQueries.Expire(oldest, ref expired);
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
            _layerQueries.Drain(idle);
            _audioQueries.Drain(idle);
        }

        foreach (IDisposable evaluator in idle)
            evaluator.Dispose();
    }

    private static MediaTime Distance(MediaTime a, MediaTime b) => a > b ? a - b : b - a;
}
