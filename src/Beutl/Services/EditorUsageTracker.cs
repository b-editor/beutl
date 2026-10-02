using System.Collections.Specialized;
using System.Reflection;
using Avalonia.Threading;
using Beutl.Audio.Effects;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.ProjectSystem;

namespace Beutl.Services;

internal sealed class EditorUsageTracker : IDisposable
{
    private static readonly HashSet<string> s_commands = typeof(CommandNames)
        .GetProperties(BindingFlags.Static | BindingFlags.Public)
        .Where(p => p.PropertyType == typeof(string))
        .Select(p => "CommandNames." + p.Name).ToHashSet(StringComparer.Ordinal);
    private readonly Scene _scene;
    private readonly IDisposable _subscription;
    private readonly UsageTelemetry? _usageTelemetry;
    private readonly Dictionary<string, UsageTelemetry.Observation?> _seenEffects = [];
    private WeakReference<object>? _activeTool;
    private long _activeToolEpoch = -1;
    private bool _observedEffects;
    private volatile bool _disposed;
    internal string ActiveTool { get; private set; } = "Editor";

    internal EditorUsageTracker(Scene scene, HistoryManager history)
    {
        _scene = scene;
        _usageTelemetry = UsageTelemetry.Current;
        if (_usageTelemetry is not null) _usageTelemetry.CollectionEnabled += OnCollectionEnabled;
        // Only new committed entries count. Snapshot, rollback, undo and redo
        // do not publish Add, so replay cannot masquerade as a new edit.
        _subscription = history.SubscribeEntries((_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add || args.NewItems is null) return;
            foreach (HistoryEntry entry in args.NewItems)
            {
                if (entry.IsInitial) continue;
                IReadOnlyList<ChangeOperation> operations = history.GetLatestCommittedOperations(entry.TransactionId);
                UsageTelemetry.Current?.Record("editor.edit", ActiveTool, GetCommandId(entry.Name));
                foreach (string property in operations.OfType<IUpdatePropertyValueOperation>()
                             .Select(GetPropertyId).OfType<string>().Distinct())
                    UsageTelemetry.Current?.Record("editor.property", ActiveTool, property);

                if (!_observedEffects || operations.Any(operation => operation is ICollectionChangeOperation
                    || operation is IUpdatePropertyValueOperation { NewValue: EngineObject or bool }))
                    ObserveEffects();
            }
        }).Subscription;
        ObserveEffects();
    }

    private void OnCollectionEnabled()
    {
        if (_disposed) return;
        if (Dispatcher.UIThread.CheckAccess()) ObserveEffects();
        else Dispatcher.UIThread.Post(ObserveEffects);
    }

    internal static string GetCommandId(string? expression) => expression is not null && s_commands.Contains(expression)
        ? expression["CommandNames.".Length..] : "PropertyEdit";

    private static string? GetPropertyId(IUpdatePropertyValueOperation operation)
    {
        Type type = operation.Object.GetType();
        if (type.Assembly != typeof(EngineObject).Assembly && type.Assembly != typeof(Scene).Assembly) return null;
        string[] parts = operation.PropertyPath.Split('.');
        string name = parts[^1];
        // Match the operation resolver: Animation/Expression belong to the
        // preceding property, not to the owning EngineObject's CLR members.
        if (parts.Length > 1 && name is "Animation" or "Expression") name = parts[^2];
        // Match a compiled property, not arbitrary node-member names or paths.
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not null
            ? type.Name + "." + name : null;
    }

    internal void ActivateTool(object instance, string tool)
    {
        if (_disposed) return;
        ActiveTool = tool;
        if (_usageTelemetry is not { } usage || !usage.TryGetCollectionEpoch(out long epoch)) return;
        // Scope deduplication to enabled collection, including opt-in from a
        // background thread before the queued UI inventory scan has run.
        if (_activeToolEpoch == epoch && _activeTool is not null
            && _activeTool.TryGetTarget(out object? current) && ReferenceEquals(current, instance))
            return;
        _activeTool = new(instance);
        _activeToolEpoch = epoch;
        usage.Record("tool.activated", tool, epoch: epoch);
    }

    private void ObserveEffects()
    {
        if (_disposed || _usageTelemetry is not { } usage || !usage.TryGetCollectionEpoch(out long epoch)) return;
        _observedEffects = true;
        foreach (EngineObject effect in _scene.EnumerateAllChildren<EngineObject>())
        {
            if (effect is not (FilterEffect or AudioEffect)
                || effect is FilterEffectGroup or AudioEffectGroup
                || !effect.IsEnabled
                || effect.EnumerateAncestors<Element>().Any(parent => !parent.IsEnabled)
                || effect.EnumerateAncestors<EngineObject>().Any(parent => !parent.IsEnabled)) continue;
            Type type = effect.GetType();
            string category = effect is FilterEffect ? "Video" : "Audio";
            string id = type.Assembly == typeof(FilterEffect).Assembly ? type.Name : "Extension";
            // Once per effect type and editor lifetime, including an existing
            // project's effects. This measures presence, not rendered frames.
            // An unflushed observation discarded by revocation can be retried;
            // types already handed to the exporter remain deduplicated.
            string key = category + "." + id;
            _seenEffects.TryGetValue(key, out UsageTelemetry.Observation? previous);
            _seenEffects[key] = usage.RecordOnce(new("effect.used", category, id), previous, epoch);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_usageTelemetry is not null) _usageTelemetry.CollectionEnabled -= OnCollectionEnabled;
        _subscription.Dispose();
    }
}
