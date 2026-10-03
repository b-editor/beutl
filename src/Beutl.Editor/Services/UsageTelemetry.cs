using System.ComponentModel;
using System.Diagnostics;
using Beutl.Configuration;

namespace Beutl.Editor.Services;

// This schema is shared with beutl-web's desktop usage report. Only code-defined
// identifiers belong here: never pass paths, titles, search terms or user text.
internal sealed class UsageTelemetry : IDisposable
{
    internal const string SourceName = "Beutl.Usage";
    internal static readonly ActivitySource Source = new(SourceName);
    internal static UsageTelemetry? Current { get; set; }
    private static readonly TimeSpan s_interval = TimeSpan.FromMinutes(1);
    private readonly object _gate = new();
    private readonly TelemetryConfig _config;
    private readonly TimeProvider _time;
    private readonly Action<UsageSummary> _emit;
    private readonly ITimer _timer;
    private readonly Dictionary<UsageKey, PendingSummary> _pending = [];
    private bool _enabled;
    private bool _started;
    private bool _disposed;
    private long _epoch;
    private long _lastPulse;

    internal event Action? CollectionEnabled;

    internal UsageTelemetry(TelemetryConfig config, TimeProvider? time = null,
        Action<UsageSummary>? emit = null)
    {
        _config = config;
        _time = time ?? TimeProvider.System;
        _emit = emit ?? Emit;
        _config.PropertyChanged += OnConfigurationChanged;
        UpdateConsent();
        _timer = _time.CreateTimer(_ => Flush(), null, s_interval, s_interval);
    }

    internal static bool HasConsent(TelemetryConfig config) => config.Beutl_Application == true
        && config.Beutl_Api_Client.HasValue && config.Beutl_PackageManagement.HasValue
        && config.Beutl_Logging.HasValue;

    internal bool IsEnabled
    {
        get { lock (_gate) return _enabled && !_disposed; }
    }

    internal bool TryGetCollectionEpoch(out long epoch)
    {
        lock (_gate)
        {
            epoch = _epoch;
            return _enabled && !_disposed;
        }
    }

    private void OnConfigurationChanged(object? sender, PropertyChangedEventArgs e) => UpdateConsent();

    private void UpdateConsent()
    {
        lock (_gate)
        {
            bool enabled = !_disposed && HasConsent(_config);
            if (enabled == _enabled) return;
            _epoch++;
            _enabled = enabled;
            _pending.Clear();
            _lastPulse = _time.GetTimestamp();
            if (enabled && !_started)
            {
                Add(new("session.started"));
            }
            if (!enabled) return;
        }

        // Notify after releasing the queue lock: observers can inspect UI models
        // and record their inventory without holding up the timer or other edits.
        if (CollectionEnabled is { } handlers)
        {
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); } catch { }
            }
        }
    }

    internal void Record(string eventName, string tool = "", string feature = "", string outcome = "", long? epoch = null)
    {
        lock (_gate)
        {
            if (_enabled && !_disposed && (epoch is null || epoch == _epoch))
                Add(new(eventName, tool, feature, outcome));
        }
    }

    internal Operation? Begin(string feature)
    {
        lock (_gate)
            return _enabled && !_disposed ? new Operation(this, feature, _epoch, _time.GetTimestamp()) : null;
    }

    internal Observation? RecordOnce(UsageKey key, Observation? previous, long epoch)
    {
        lock (_gate)
        {
            if (!_enabled || _disposed || epoch != _epoch
                || previous is { WasEmitted: true }
                || previous is { IsPending: true } && previous.Epoch == _epoch)
                return previous;
            return Add(key);
        }
    }

    private Observation? Add(UsageKey key, double durationMs = 0)
    {
        // Bound memory and network traffic even if a future caller accidentally
        // introduces an unbounded dimension. Counts are batched, not sampled.
        if (_pending.TryGetValue(key, out PendingSummary? pending))
            pending.Summary = pending.Summary with
            {
                Count = pending.Summary.Count + 1,
                DurationMs = pending.Summary.DurationMs + durationMs
            };
        else
        {
            if (_pending.Count >= 1024) return null;
            pending = new(new UsageSummary(key, 1, durationMs), _epoch);
            _pending.Add(key, pending);
        }
        return pending.Observation;
    }

    internal void Flush()
    {
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            long now = _time.GetTimestamp();
            Add(new("session.heartbeat"), _time.GetElapsedTime(_lastPulse, now).TotalMilliseconds);
            _lastPulse = now;
            PendingSummary[] summaries = [.. _pending.Values];
            _pending.Clear();
            long epoch = _epoch;
            foreach (PendingSummary pending in summaries)
            {
                // Activity listeners can re-enter and change consent during emit.
                if (!_enabled || _epoch != epoch) break;
                // Telemetry must never break an editing action or a timer thread.
                try
                {
                    // Mark before invoking listeners, which can re-enter an
                    // inventory scan. Roll back if emission itself fails.
                    pending.Observation.IsPending = false;
                    pending.Observation.WasEmitted = true;
                    UsageSummary summary = pending.Summary;
                    _emit(summary);
                    if (summary.Key.Event == "session.started") _started = true;
                }
                catch { pending.Observation.WasEmitted = false; }
            }
        }
    }

    private static void Emit(UsageSummary summary)
    {
        Activity? previous = Activity.Current;
        try
        {
            // Flush is synchronous. Keep summaries out of whichever unrelated
            // operation happens to be current, then restore that activity.
            Activity.Current = null;
            using Activity? activity = Source.StartActivity("Usage.Summary", ActivityKind.Internal);
            activity?.SetTag("beutl.usage.schema_version", 1);
            activity?.SetTag("beutl.usage.event", summary.Key.Event);
            activity?.SetTag("beutl.usage.tool", summary.Key.Tool);
            activity?.SetTag("beutl.usage.feature", summary.Key.Feature);
            activity?.SetTag("beutl.usage.outcome", summary.Key.Outcome);
            activity?.SetTag("beutl.usage.count", summary.Count);
            activity?.SetTag("beutl.usage.duration_ms", summary.DurationMs);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_enabled)
            {
                Add(new("session.ended"));
                Flush();
            }
            _disposed = true;
            _pending.Clear();
        }
        _timer.Dispose();
        _config.PropertyChanged -= OnConfigurationChanged;
        if (ReferenceEquals(Current, this)) Current = null;
    }

    internal sealed class Operation(UsageTelemetry owner, string feature, long epoch, long started) : IDisposable
    {
        private string _outcome = "failed";
        private int _disposed;
        internal void Complete(string outcome = "succeeded") => _outcome = outcome;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._gate)
            {
                // A revoke/re-enable cycle must not resurrect an in-flight measurement.
                if (owner._enabled && !owner._disposed && owner._epoch == epoch)
                    owner.Add(new("operation", Feature: feature, Outcome: _outcome),
                        owner._time.GetElapsedTime(started).TotalMilliseconds);
            }
        }
    }

    // Shared by all observations of the same batched key. Only access these
    // fields while holding the collector gate, including during consent changes.
    internal sealed class Observation(long epoch)
    {
        internal readonly long Epoch = epoch;
        internal bool IsPending = true;
        internal bool WasEmitted;
    }

    private sealed class PendingSummary(UsageSummary summary, long epoch)
    {
        internal UsageSummary Summary = summary;
        internal readonly Observation Observation = new(epoch);
    }
}

internal readonly record struct UsageKey(string Event, string Tool = "", string Feature = "", string Outcome = "");
internal sealed record UsageSummary(UsageKey Key, long Count, double DurationMs);
