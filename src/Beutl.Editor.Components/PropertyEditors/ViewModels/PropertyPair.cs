using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// Two properties that CombinedEditorAttribute joins, as the property panel, the inline view and the graph editor see them.
// Each property keeps its own value and keyframes. While the two have the same keyframe times and easings and share one
// expression or have none they stand as one, unless the user separated them: Animation then gives the keyframes of their
// one value, which is only a view of the two and is never saved.
internal abstract class PropertyPair : IAnimatablePropertyAdapter, IExpressionPropertyAdapter
{
    private static readonly ConditionalWeakTable<IProperty, object> s_pairs = [];
    private static readonly object s_noPair = new();

    private bool _restoredSeparation;

    public static PropertyPair? Find(IPropertyAdapter adapter) => adapter as PropertyPair ?? Find(adapter.GetEngineProperty());

    // The pair a property is in, which every editor of it shares.
    public static PropertyPair? Find(IProperty? property)
    {
        if (property == null) return null;
        if (s_pairs.TryGetValue(property, out object? found)) return found as PropertyPair;

        PropertyPair? pair = property.GetOwnerObject() is EngineObject owner ? Create(property, owner) : null;
        if (pair == null)
        {
            s_pairs.AddOrUpdate(property, s_noPair);
            return null;
        }

        foreach (IProperty member in pair.Properties)
            s_pairs.AddOrUpdate(member, pair);
        return pair;
    }

    private static PropertyPair? Create(IProperty property, EngineObject owner)
    {
        foreach (IProperty first in owner.Properties)
        {
            if (first.GetAttributes()?.OfType<CombinedEditorAttribute>().FirstOrDefault() is not { } attribute
                || (!ReferenceEquals(first, property) && attribute.Second != property.Name)
                || owner.Properties.FirstOrDefault(other => other.Name == attribute.Second) is not { } second)
            {
                continue;
            }

            string name = attribute.GetName() ?? first.Name;
            // A width and a height are a size; anything else has an axis, which a point has.
            bool isSize = first.Name.EndsWith("Width", StringComparison.Ordinal);
            return (PropertyAdapterFactory.CreateAdapter(first, owner), PropertyAdapterFactory.CreateAdapter(second, owner)) switch
            {
                (IAnimatablePropertyAdapter<float> a, IAnimatablePropertyAdapter<float> b) when isSize
                    => new PropertyPair<float, Graphics.Size>(a, b, name, v => v.Width, v => v.Height, (x, y) => new Graphics.Size(x, y),
                        "new Size(Sin(Time) * 100, 100)", ComponentExpression<Graphics.Size, float>.TryParse),
                (IAnimatablePropertyAdapter<float> a, IAnimatablePropertyAdapter<float> b)
                    => new PropertyPair<float, Graphics.Point>(a, b, name, v => v.X, v => v.Y, (x, y) => new Graphics.Point(x, y),
                        "new Point(Sin(Time) * 100, 0)", ComponentExpression<Graphics.Point, float>.TryParse),
                (IAnimatablePropertyAdapter<int> a, IAnimatablePropertyAdapter<int> b) when isSize
                    => new PropertyPair<int, PixelSize>(a, b, name, v => v.Width, v => v.Height, (x, y) => new PixelSize(x, y),
                        "new PixelSize(100, 100)", ComponentExpression<PixelSize, int>.TryParse),
                (IAnimatablePropertyAdapter<int> a, IAnimatablePropertyAdapter<int> b)
                    => new PropertyPair<int, PixelPoint>(a, b, name, v => v.X, v => v.Y, (x, y) => new PixelPoint(x, y),
                        "new PixelPoint(100, 0)", ComponentExpression<PixelPoint, int>.TryParse),
                _ => null
            };
        }

        return null;
    }

    // The two properties, in the order the attribute names them.
    public abstract IReadOnlyList<IProperty> Properties { get; }

    // An adapter of each property, whose animation shows while the two stand apart.
    public abstract IReadOnlyList<IAnimatablePropertyAdapter> Adapters { get; }

    public abstract string ExpressionExample { get; }

    // The user separated the two.
    public ReactivePropertySlim<bool> KeepsSeparated { get; } = new();

    public void SetKeepsSeparated(bool keepsSeparated)
    {
        _restoredSeparation = true;
        KeepsSeparated.Value = keepsSeparated;
    }

    public abstract IReadOnlyReactiveProperty<bool> CanStandForBoth { get; }

    public abstract IReadOnlyReactiveProperty<bool> IsSeparated { get; }

    public abstract Type ImplementedType { get; }

    public abstract Type PropertyType { get; }

    public abstract string DisplayName { get; }

    public abstract string? Description { get; }

    public abstract bool IsReadOnly { get; }

    public abstract IKeyFrameAnimation? Animation { get; set; }

    public abstract IObservable<IKeyFrameAnimation?> ObserveAnimation { get; }

    public abstract IExpression? Expression { get; set; }

    public bool HasExpression => Expression != null;

    public abstract IObservable<IExpression?> ObserveExpression { get; }

    // The panel's saved state of the two, which must not undo a choice made since by an editor that is still open.
    public void RestoreSeparation(bool keepsSeparated)
    {
        if (!_restoredSeparation)
            SetKeepsSeparated(keepsSeparated);
    }

    public bool Contains(IPropertyAdapter adapter)
        => ReferenceEquals(adapter, this) || (adapter.GetEngineProperty() is { } property && Properties.Contains(property));

    // Between the two edits the properties differ, which must not make them stand apart.
    public abstract void BeginEdit();

    public abstract void EndEdit();

    public abstract object? GetDefaultValue();

    public abstract void SetValue(object? value);

    public abstract object? GetValue();

    public abstract IObservable<object?> GetObservable();

    public Attribute[] GetAttributes() => Properties[0].GetAttributes() ?? [];
}

internal abstract class PropertyPair<T> : PropertyPair
{
    public delegate bool ExpressionParser(string source, [NotNullWhen(true)] out IExpression<T>[]? expressions, [NotNullWhen(false)] out string? error);

    public abstract bool TryParseExpression(string source, [NotNullWhen(true)] out IExpression<T>[]? expressions, [NotNullWhen(false)] out string? error);
}

internal sealed class PropertyPair<T, TValue> : PropertyPair<T>
    where T : struct
    where TValue : struct
{
    private readonly IAnimatablePropertyAdapter<T> _first;
    private readonly IAnimatablePropertyAdapter<T> _second;
    private readonly Func<TValue, T> _getFirst;
    private readonly Func<TValue, T> _getSecond;
    private readonly Func<T, T, TValue> _compose;
    private readonly ExpressionParser _parseExpression;
    private readonly KeyFrameAnimation<TValue> _joined = new();
    private readonly Dictionary<KeyFrame<TValue>, (KeyFrame<T> First, KeyFrame<T> Second)> _links = [];
    // Undo brings a key back with its joined key, which the graph editor's selection holds on to.
    private readonly ConditionalWeakTable<KeyFrame<T>, KeyFrame<TValue>> _joinedKeys = [];
    private readonly HashSet<SplineEasing> _watchedEasings = [];
    private readonly ReactivePropertySlim<bool> _canStandForBoth = new();
    private readonly ReactivePropertySlim<bool> _isSeparated = new();
    private readonly ReactivePropertySlim<IKeyFrameAnimation?> _animation = new();
    private int _editDepth;
    private bool _syncing;
    private bool _updatePending;

    public PropertyPair(IAnimatablePropertyAdapter<T> first, IAnimatablePropertyAdapter<T> second, string displayName,
        Func<TValue, T> getFirst, Func<TValue, T> getSecond, Func<T, T, TValue> compose,
        string expressionExample, ExpressionParser parseExpression)
    {
        _first = first;
        _second = second;
        _getFirst = getFirst;
        _getSecond = getSecond;
        _compose = compose;
        _parseExpression = parseExpression;
        DisplayName = displayName;
        ExpressionExample = expressionExample;
        Properties = [first.GetEngineProperty()!, second.GetEngineProperty()!];
        Adapters = [first, second];

        // Only its parent is set: the owner gives it the time origin of the properties' keyframes, but does not hold it.
        var owner = (IHierarchical)Properties[0].GetOwnerObject()!;
        var joined = (IModifiableHierarchical)_joined;
        joined.SetParent(owner);
        owner.AttachedToHierarchy += (_, e) => joined.NotifyAttachedToHierarchy(new HierarchyAttachmentEventArgs(e.Root, owner));
        owner.DetachedFromHierarchy += (_, e) => joined.NotifyDetachedFromHierarchy(new HierarchyAttachmentEventArgs(e.Root, owner));

        _joined.Edited += (_, _) => WriteToProperties();
        _joined.KeyFrames.CollectionChanged += (_, _) => WriteToProperties();
        Observe(first);
        Observe(second);
        KeepsSeparated.Subscribe(_ => Update());
    }

    public override IReadOnlyList<IProperty> Properties { get; }

    public override IReadOnlyList<IAnimatablePropertyAdapter> Adapters { get; }

    public override string ExpressionExample { get; }

    public override IReadOnlyReactiveProperty<bool> CanStandForBoth => _canStandForBoth;

    public override IReadOnlyReactiveProperty<bool> IsSeparated => _isSeparated;

    public override Type ImplementedType => _first.ImplementedType;

    public override Type PropertyType => typeof(TValue);

    public override string DisplayName { get; }

    public override string? Description => _first.Description;

    public override bool IsReadOnly => _first.IsReadOnly || _second.IsReadOnly;

    public override IKeyFrameAnimation? Animation
    {
        get => _animation.Value;
        set
        {
            BeginEdit();
            try
            {
                var animation = value as KeyFrameAnimation<TValue>;
                _first.Animation = animation == null ? null : Split(animation, _getFirst);
                _second.Animation = animation == null ? null : Split(animation, _getSecond);
            }
            finally
            {
                EndEdit();
            }
        }
    }

    public override IObservable<IKeyFrameAnimation?> ObserveAnimation => _animation;

    public override IExpression? Expression
    {
        get => _isSeparated.Value ? null : _first.Expression;
        set
        {
            if (value != null) throw new NotSupportedException();

            BeginEdit();
            try
            {
                _first.Expression = null;
                _second.Expression = null;
            }
            finally
            {
                EndEdit();
            }
        }
    }

    public override IObservable<IExpression?> ObserveExpression => _isSeparated
        .Select(separated => separated
            ? Observable.ReturnThenNever<IExpression?>(null)
            : ((IExpressionPropertyAdapter)_first).ObserveExpression)
        .Switch()
        .DistinctUntilChanged();

    public override void BeginEdit() => _editDepth++;

    public override void EndEdit()
    {
        _editDepth--;
        Update();
    }

    public override bool TryParseExpression(string source, [NotNullWhen(true)] out IExpression<T>[]? expressions,
        [NotNullWhen(false)] out string? error)
        => _parseExpression(source, out expressions, out error);

    public override object? GetDefaultValue()
        => _first.GetDefaultValue() is T first && _second.GetDefaultValue() is T second ? _compose(first, second) : null;

    public override void SetValue(object? value)
    {
        if (value is not TValue typed) return;

        _first.SetValue(_getFirst(typed));
        _second.SetValue(_getSecond(typed));
    }

    public override object? GetValue() => _compose(_first.GetValue(), _second.GetValue());

    public override IObservable<object?> GetObservable()
        => _first.GetObservable().CombineLatest(_second.GetObservable(), (first, second) => (object?)_compose(first, second));

    private void Observe(IAnimatablePropertyAdapter<T> property)
    {
        property.GetEngineProperty()!.Edited += (_, _) => OnPropertyEdited();
        property.ObserveAnimation
            .Select(animation => animation?.KeyFrames.CollectionChangedAsObservable().Select(_ => 0) ?? Observable.Never<int>())
            .Switch()
            .Subscribe(_ => OnPropertyEdited());
    }

    private void OnPropertyEdited()
    {
        if (_syncing || _editDepth > 0) return;

        if (Dispatcher.UIThread.CheckAccess() && _animation.Value != null && CanStand())
            ReadFromProperties(_first.Animation!, _second.Animation!);

        ScheduleUpdate();
    }

    // Undo restores the two one after the other, so whether they stand as one waits until it has finished.
    private void ScheduleUpdate()
    {
        if (_updatePending) return;
        _updatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _updatePending = false;
            Update();
        });
    }

    private void Update()
    {
        if (_syncing || _editDepth > 0) return;

        bool canStand = CanStand();
        bool separated = KeepsSeparated.Value || !canStand;
        _canStandForBoth.Value = canStand;
        if (!separated && _first.Animation is { } first && _second.Animation is { } second)
            ReadFromProperties(first, second);
        else
            WatchEasings();

        // Whoever shows the joined keyframes hears of the two rows first, and of the joined keyframes before the one row.
        if (separated) _isSeparated.Value = true;
        _animation.Value = !separated && _first.Animation != null ? _joined : null;
        _isSeparated.Value = separated;
    }

    private bool CanStand()
    {
        if ((_first.HasExpression || _second.HasExpression) && !ShareOneExpression()) return false;

        KeyFrameAnimation<T>? first = _first.Animation;
        KeyFrameAnimation<T>? second = _second.Animation;
        if (first == null || second == null) return first == second;
        if (first.UseGlobalClock.Value != second.UseGlobalClock.Value || first.KeyFrames.Count != second.KeyFrames.Count)
            return false;

        for (int i = 0; i < first.KeyFrames.Count; i++)
        {
            IKeyFrame a = first.KeyFrames[i];
            IKeyFrame b = second.KeyFrames[i];
            // The menu of a property's own row is where the user removes an easing that could not be read.
            if (a.KeyTime.Value != b.KeyTime.Value || !SameEasing(a.Easing.Value, b.Easing.Value)
                || SceneRecovery.IsRecoveryBlocker(a) || SceneRecovery.IsRecoveryBlocker(b))
            {
                return false;
            }
        }

        return true;
    }

    private bool ShareOneExpression()
        => _first.Expression is IComponentExpression { ComponentIndex: 0 } first
           && _second.Expression is IComponentExpression { ComponentIndex: 1 } second
           && first.GetType() == second.GetType()
           && first.Source == second.Source;

    private static bool SameEasing(Easing first, Easing second)
        => first is SplineEasing a && second is SplineEasing b
            ? a.X1 == b.X1 && a.Y1 == b.Y1 && a.X2 == b.X2 && a.Y2 == b.Y2
            : first.GetType() == second.GetType();

    private static Easing CopyOf(Easing easing)
        => easing is SplineEasing spline ? new SplineEasing(spline.X1, spline.Y1, spline.X2, spline.Y2) : easing;

    private static KeyFrameAnimation<T> Split(KeyFrameAnimation<TValue> source, Func<TValue, T> get)
    {
        var animation = new KeyFrameAnimation<T> { UseGlobalClock = { Value = source.UseGlobalClock.Value } };
        foreach (IKeyFrame key in source.KeyFrames)
        {
            animation.KeyFrames.Add(new KeyFrame<T>
            {
                KeyTime = { Value = key.KeyTime.Value },
                Easing = { Value = CopyOf(key.Easing.Value) },
                Value = { Value = get(((KeyFrame<TValue>)key).Value.Value) }
            }, out _);
        }

        return animation;
    }

    // An edit of the joined keyframes, which goes to both properties.
    private void WriteToProperties()
    {
        if (_syncing || _first.Animation is not { } first || _second.Animation is not { } second) return;

        _syncing = true;
        try
        {
            foreach ((KeyFrame<TValue> key, (KeyFrame<T> First, KeyFrame<T> Second) link) in _links.ToArray())
            {
                if (_joined.KeyFrames.Contains(key)) continue;

                first.KeyFrames.Remove(link.First);
                second.KeyFrames.Remove(link.Second);
                _links.Remove(key);
            }

            foreach (KeyFrame<TValue> key in _joined.KeyFrames.Cast<KeyFrame<TValue>>())
            {
                bool added = !_links.TryGetValue(key, out (KeyFrame<T> First, KeyFrame<T> Second) link);
                if (added)
                {
                    link = (new KeyFrame<T>(), new KeyFrame<T>());
                    _links[key] = link;
                    _joinedKeys.AddOrUpdate(link.First, key);
                }

                WriteKey(key, link);
                if (added)
                {
                    first.KeyFrames.Add(link.First, out _);
                    second.KeyFrames.Add(link.Second, out _);
                }
            }

            first.UseGlobalClock.Value = _joined.UseGlobalClock.Value;
            second.UseGlobalClock.Value = _joined.UseGlobalClock.Value;
        }
        finally
        {
            _syncing = false;
        }

        // Reads back what each property's range made of the value.
        Update();
    }

    private void WriteKey(KeyFrame<TValue> key, (KeyFrame<T> First, KeyFrame<T> Second) link)
    {
        link.First.KeyTime.Value = key.KeyTime.Value;
        link.Second.KeyTime.Value = key.KeyTime.Value;
        link.First.Easing.Value = key.Easing.Value;
        if (!SameEasing(link.Second.Easing.Value, key.Easing.Value))
            link.Second.Easing.Value = CopyOf(key.Easing.Value);
        link.First.Value.Value = _getFirst(key.Value.Value);
        link.Second.Value.Value = _getSecond(key.Value.Value);
    }

    // The joined keyframes as the two properties have them. A joined key has the easing object of the first property's key.
    private void ReadFromProperties(KeyFrameAnimation<T> first, KeyFrameAnimation<T> second)
    {
        _syncing = true;
        try
        {
            // What saves and restores an inline layer finds the first property's keyframes by this.
            _joined.Id.Value = first.Id.Value;
            _joined.UseGlobalClock.Value = first.UseGlobalClock.Value;

            _links.Clear();
            var keys = new KeyFrame<TValue>[first.KeyFrames.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                var a = (KeyFrame<T>)first.KeyFrames[i];
                var b = (KeyFrame<T>)second.KeyFrames[i];
                KeyFrame<TValue> key = _joinedKeys.GetValue(a, _ => new KeyFrame<TValue>());
                key.KeyTime.Value = a.KeyTime.Value;
                key.Easing.Value = a.Easing.Value;
                key.Value.Value = _compose(a.Value.Value, b.Value.Value);
                _links[key] = (a, b);
                keys[i] = key;
            }

            for (int i = _joined.KeyFrames.Count - 1; i >= 0; i--)
            {
                if (Array.IndexOf(keys, _joined.KeyFrames[i]) < 0)
                    _joined.KeyFrames.RemoveAt(i);
            }

            for (int i = 0; i < keys.Length; i++)
            {
                int index = _joined.KeyFrames.IndexOf(keys[i]);
                if (index < 0) _joined.KeyFrames.Insert(i, keys[i]);
                else if (index != i) _joined.KeyFrames.Move(index, i);
            }

            WatchEasings();
        }
        finally
        {
            _syncing = false;
        }
    }

    // Moving a control point of an easing edits neither its key nor the property.
    private void WatchEasings()
    {
        HashSet<SplineEasing> easings = [.. Easings(_first.Animation), .. Easings(_second.Animation)];
        foreach (SplineEasing easing in _watchedEasings.Except(easings).ToArray())
        {
            easing.Changed -= OnEasingChanged;
            _watchedEasings.Remove(easing);
        }

        foreach (SplineEasing easing in easings)
        {
            if (_watchedEasings.Add(easing))
                easing.Changed += OnEasingChanged;
        }
    }

    private static IEnumerable<SplineEasing> Easings(KeyFrameAnimation<T>? animation)
        => animation?.KeyFrames.Select(key => key.Easing.Value).OfType<SplineEasing>() ?? [];

    private void OnEasingChanged(object? sender, EventArgs e)
    {
        if (_syncing || sender is not SplineEasing source) return;

        // The graph editor moves a control point of the easing a joined key has, which is the first property's.
        if (_animation.Value != null)
        {
            foreach ((KeyFrame<T> first, KeyFrame<T> second) in _links.Values)
            {
                if (ReferenceEquals(first.Easing.Value, source) && second.Easing.Value is SplineEasing target)
                {
                    if (target.X1 != source.X1) target.X1 = source.X1;
                    if (target.Y1 != source.Y1) target.Y1 = source.Y1;
                    if (target.X2 != source.X2) target.X2 = source.X2;
                    if (target.Y2 != source.Y2) target.Y2 = source.Y2;
                }
            }
        }

        ScheduleUpdate();
    }
}
