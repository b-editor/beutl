using System.Collections;
using System.Collections.Concurrent;
using System.Reactive;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

// Runs the edits of the copy's editors on each element too: setting a value, adding a keyframe, animating a property,
// setting an expression or separating components. An edit reads the values it works on, so each element runs it through
// an editor of its own on its own values, while the element the copy was made from takes what the copy's editor did.
// Times move with the element: a keyframe added at the playhead lies as far into each element as into the one shown, and
// a value goes to the keyframe that element's editor edits there.
internal sealed class MultiElementMirror : IPropertyEditorMirror, IDisposable
{
    // How many elements the description of differing values names.
    private const int MaxListedElements = 8;
    private static readonly object s_missing = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> s_componentValueMethods = new();
    private readonly MultiElementReplicator _replicator;
    private readonly MultiElementCorrespondence _correspondence;
    private readonly IReadOnlyList<Element> _compared;
    private readonly IObservable<Unit> _valuesChanged;
    private readonly IServiceProvider _services;
    private readonly IEditorClock? _clock;
    private readonly Scene? _scene;
    // The editors of the other elements a dragged value goes through, until it is set.
    private readonly Dictionary<PropertyEditorViewModel, TargetEditor[]> _targets = new(ReferenceEqualityComparer.Instance);
    private bool _running;

    // compared are the elements whose values the editors compare with the one they show.
    public MultiElementMirror(
        MultiElementReplicator replicator, MultiElementCorrespondence correspondence, IReadOnlyList<Element> compared,
        IObservable<Unit> valuesChanged, IServiceProvider services)
    {
        _replicator = replicator;
        _correspondence = correspondence;
        _compared = compared;
        _valuesChanged = valuesChanged;
        _services = services;
        _clock = services.GetService<IEditorClock>();
        _scene = services.GetService<Scene>();
    }

    public bool Run(
        PropertyEditorViewModel editor, Action<PropertyEditorViewModel, MediaTime> command, bool commit = true,
        string? commitName = null)
    {
        if (_running || editor.IsDisposed) return false;

        _running = true;
        bool defers = editor.DefersCommit;
        editor.DefersCommit = true;
        try
        {
            // The others go first, so that what the copy shows next, as the editors of an object that took the place of
            // another, finds its like in each of them.
            foreach (TargetEditor other in TargetsOf(editor))
                command(other.Editor, other.Shift);

            _replicator.ReachesShownOnly = true;
            try
            {
                command(editor, MediaTime.Zero);
            }
            finally
            {
                _replicator.ReachesShownOnly = false;
            }
        }
        finally
        {
            editor.DefersCommit = defers;
            _running = false;
        }

        if (commit)
        {
            // An edit that is set can change what the elements hold, which the next edit's editors follow.
            Forget();
            editor.Commit(commitName);
        }

        return true;
    }

    public object? CopyValue(object? value) => _replicator.CopyValue(value, exact: false);

    public IObservable<string?> ObserveDifference(PropertyEditorViewModel editor)
        => _valuesChanged
            .StartWith(Unit.Default)
            .Select(_ => DescribeDifference(editor))
            .DistinctUntilChanged();

    // Lets go of the editors of the other elements, after the elements or the playhead changed.
    public void Forget()
    {
        foreach (TargetEditor[] editors in _targets.Values)
        {
            foreach (TargetEditor editor in editors)
                editor.Dispose();
        }

        _targets.Clear();
    }

    public void Dispose() => Forget();

    private TargetEditor[] TargetsOf(PropertyEditorViewModel editor)
    {
        if (_targets.TryGetValue(editor, out TargetEditor[]? targets)) return targets;

        targets = [.. _replicator.EditableOthers()
            .Select(target => TargetEditor.Create(editor, target, _correspondence, _replicator.Copy, _services))
            .OfType<TargetEditor>()];
        _targets[editor] = targets;
        return targets;
    }

    private string? DescribeDifference(PropertyEditorViewModel editor)
    {
        if (editor.IsDisposed || _compared.Count == 0) return null;

        var properties = new List<IProperty>(editor.EditedProperties.Count);
        foreach (IPropertyAdapter adapter in editor.EditedProperties)
        {
            if (adapter.GetEngineProperty() is not { } property || property.GetOwnerObject() is null) return null;
            properties.Add(property);
        }

        if (properties.Count == 0) return null;

        object?[] shown = [.. properties.Select(property => ShownValue(property, MediaTime.Zero))];
        List<(Element Element, object?[] Values)>? differing = null;
        foreach (Element element in _compared)
        {
            MediaTime shift = element.Start.Value - _replicator.Copy.Start.Value;
            object?[] values = [.. properties.Select(property => OtherValue(property, element, shift))];
            if (!values.SequenceEqual(shown, ValueComparer.Instance))
                (differing ??= []).Add((element, values));
        }

        if (differing is null) return null;

        var text = new StringBuilder(MessageStrings.MultipleElements_ValuesDiffer);
        AppendValues(text, _replicator.Shown, shown);
        foreach ((Element element, object?[] values) in differing.Take(MaxListedElements - 1))
            AppendValues(text, element, values);

        if (differing.Count > MaxListedElements - 1)
        {
            text.AppendLine().Append(string.Format(
                CultureInfo.CurrentCulture, MessageStrings.MultipleElements_MoreValues, differing.Count - (MaxListedElements - 1)));
        }

        return text.ToString();
    }

    private object? OtherValue(IProperty property, Element element, MediaTime shift)
    {
        if (property.GetOwnerObject() is not { } owner
            || _correspondence.Find(owner, element) is not { } targetOwner
            || MultiElementCorrespondence.FindProperty(targetOwner, property) is not { } targetProperty)
        {
            return s_missing;
        }

        // Keyframes on the scene's clock lie at the same time in every element.
        if (property.GetPropertyAnimation() is IKeyFrameAnimation { UseGlobalClock.Value: true })
            shift = MediaTime.Zero;

        return ShownValue(targetProperty, shift);
    }

    // What an editor of property shows with the playhead moved by shift: its expression, the value of the keyframe it
    // edits there, or its value.
    private object? ShownValue(IProperty property, MediaTime shift)
    {
        if (property is IExpressionProperty { HasExpression: true } expressionProperty)
            return new ExpressionValue(DescribeExpression(expressionProperty.SerializeExpression()));

        MediaTime time = (_clock?.CurrentTime.Value ?? MediaTime.Zero) + shift;
        return property.GetPropertyAnimation() switch
        {
            IComponentAnimation components => ComponentValue(components, time),
            IKeyFrameAnimation { KeyFrames.Count: > 0 } animation
                => AnimationOperations.FindEditingKeyFrame(animation, time, AnimationOperations.GetFrameRate(_scene))?.Value,
            _ => property.Value,
        };
    }

    // The value separated components make at time, which IComponentAnimation<T> gives for its own T.
    private static object? ComponentValue(IComponentAnimation components, MediaTime time)
    {
        MethodInfo? method = s_componentValueMethods.GetOrAdd(components.GetType(), type => type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IComponentAnimation<>))
            ?.GetMethod(nameof(IComponentAnimation<int>.GetEditingValue)));
        return method?.Invoke(components, [time]);
    }

    private static string? DescribeExpression(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out string? source) ? source : node?.ToJsonString();

    private static void AppendValues(StringBuilder text, Element element, object?[] values)
    {
        string label = ToolTabHeaderHelper.ElementLabel(element.Name.Value, element);
        text.AppendLine()
            .Append(string.IsNullOrWhiteSpace(label) ? Strings.Element : label)
            .Append(": ")
            .Append(string.Join(", ", values.Select(Format)));
    }

    private static string Format(object? value) => value switch
    {
        null => "-",
        ExpressionValue expression => "= " + expression.Source,
        CoreObject obj => TypeDisplayHelpers.GetLocalizedName(obj.GetType()),
        float number => number.ToString("0.###", CultureInfo.CurrentCulture),
        double number => number.ToString("0.###", CultureInfo.CurrentCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ when ReferenceEquals(value, s_missing) => "-",
        _ => value.ToString() ?? "-",
    };

    // An expression stands for itself: two properties show the same when they have the same one.
    private readonly record struct ExpressionValue(string? Source);

    // Objects are told apart by their type only: an editor of an object property shows of what type it is, and the
    // editors of its properties show what differs inside. Each element holds a list of its own, so lists are told apart
    // by their items.
    private sealed class ValueComparer : IEqualityComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public new bool Equals(object? x, object? y) => (x, y) switch
        {
            (CoreObject a, _) => y is CoreObject b && a.GetType() == b.GetType(),
            (IEnumerable a, IEnumerable b) when x is not string => a.Cast<object?>().SequenceEqual(b.Cast<object?>(), this),
            _ => object.Equals(x, y),
        };

        public int GetHashCode(object? obj) => obj is CoreObject ? obj.GetType().GetHashCode() : obj?.GetHashCode() ?? 0;
    }

    // An editor of one element's properties that are at the places of the copy's, which no control shows.
    private sealed class TargetEditor : IDisposable
    {
        private readonly IReadOnlyList<IPropertyEditorContext?> _contexts;
        private readonly ShiftedClock _clock;

        private TargetEditor(PropertyEditorViewModel editor, MediaTime shift, IReadOnlyList<IPropertyEditorContext?> contexts, ShiftedClock clock)
        {
            Editor = editor;
            Shift = shift;
            _contexts = contexts;
            _clock = clock;
        }

        public PropertyEditorViewModel Editor { get; }

        public MediaTime Shift { get; }

        public static TargetEditor? Create(
            PropertyEditorViewModel editor, Element target, MultiElementCorrespondence correspondence, Element copy,
            IServiceProvider services)
        {
            var adapters = new List<IPropertyAdapter>(editor.EditedProperties.Count);
            foreach (IPropertyAdapter source in editor.EditedProperties)
            {
                if (source.GetEngineProperty() is not { } property
                    || property.GetOwnerObject() is not { } owner
                    || correspondence.Find(owner, target) is not { } targetOwner
                    || MultiElementCorrespondence.FindProperty(targetOwner, property) is not { } targetProperty)
                {
                    return null;
                }

                adapters.Add(PropertyAdapterFactory.CreateAdapter(targetProperty, targetOwner));
            }

            // Keyframes on the scene's clock lie at the same time in every element.
            MediaTime shift = editor.GetAnimation() is { UseGlobalClock.Value: true }
                ? MediaTime.Zero
                : target.Start.Value - copy.Start.Value;
            var clock = new ShiftedClock(services.GetService<IEditorClock>(), shift);
            var targetServices = new TargetServices(services, target, clock);
            IReadOnlyList<IPropertyEditorContext?> contexts = services.GetRequiredService<IPropertyEditorFactory>()
                .CreatePropertyEditorContexts(adapters, targetServices);
            if (Flatten(contexts).FirstOrDefault(context => context.GetType() == editor.GetType())
                is not PropertyEditorViewModel found)
            {
                Dispose(contexts);
                clock.Dispose();
                return null;
            }

            found.DefersCommit = true;
            return new TargetEditor(found, shift, contexts, clock);
        }

        public void Dispose()
        {
            Dispose(_contexts);
            _clock.Dispose();
        }

        private static void Dispose(IReadOnlyList<IPropertyEditorContext?> contexts)
        {
            foreach (IPropertyEditorContext? context in contexts)
            {
                context?.Dispose();
            }
        }

        private static IEnumerable<IPropertyEditorContext> Flatten(IEnumerable<IPropertyEditorContext?> contexts)
        {
            foreach (IPropertyEditorContext? context in contexts)
            {
                if (context is PropertyEditorGroupContext group)
                {
                    foreach (IPropertyEditorContext nested in Flatten(group.Properties))
                        yield return nested;
                }
                else if (context is not null)
                {
                    yield return context;
                }
            }
        }
    }

    // The services of an element's hidden editor: its element, the playhead moved by how much later the element
    // starts, and no tool tab to open or edit to repeat.
    private sealed class TargetServices(IServiceProvider services, Element element, IEditorClock clock)
        : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Element)) return element;
            if (serviceType == typeof(IEditorClock)) return clock;
            if (serviceType == typeof(IPropertyEditorMirror)
                || serviceType == typeof(IPropertyEditorControlHost)
                || serviceType == typeof(IEditorContext))
            {
                return null;
            }

            return services.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }

    // The playhead as an element that starts shift later sees it. A hidden editor never moves it.
    private sealed class ShiftedClock : IEditorClock, IDisposable
    {
        private readonly ReadOnlyReactivePropertySlim<MediaTime> _currentTime;
        private readonly ReactivePropertySlim<MediaTime> _maximumTime;

        public ShiftedClock(IEditorClock? clock, MediaTime shift)
        {
            _currentTime = (clock?.CurrentTime ?? Observable.Return(MediaTime.Zero))
                .Select(time => time + shift)
                .ToReadOnlyReactivePropertySlim();
            _maximumTime = new ReactivePropertySlim<MediaTime>(clock?.MaximumTime.Value ?? MediaTime.Zero);
        }

        public IReadOnlyReactiveProperty<MediaTime> CurrentTime => _currentTime;

        public IReadOnlyReactiveProperty<MediaTime> MaximumTime => _maximumTime;

        public void Seek(MediaTime time)
        {
        }

        public void Dispose()
        {
            _currentTime.Dispose();
            _maximumTime.Dispose();
        }
    }
}
