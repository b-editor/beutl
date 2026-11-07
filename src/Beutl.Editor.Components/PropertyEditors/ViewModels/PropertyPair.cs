using System.Diagnostics.CodeAnalysis;
using System.Numerics;
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

// The two or three properties that CombinedEditorAttribute joins, as the property panel, the inline view and the graph
// editor see them. Each property keeps its own value and keyframes. While they have the same keyframe times and easings
// and share one expression or have none they stand as one, unless the user separated them: Animation then gives the
// keyframes of their one value, which is only a view of the properties and is never saved.
internal abstract class PropertyPair : IAnimatablePropertyAdapter, IExpressionPropertyAdapter
{
    private static readonly ConditionalWeakTable<IProperty, object> s_pairs = [];
    private static readonly object s_noPair = new();

    private bool _restoredSeparation;

    protected PropertyPair(IReadOnlyList<IAnimatablePropertyAdapter> adapters, string displayName, string expressionExample)
    {
        Properties = [.. adapters.Select(adapter => adapter.GetEngineProperty()!)];
        Adapters = adapters;
        DisplayName = displayName;
        ExpressionExample = expressionExample;
    }

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
            if (first.GetAttributes()?.OfType<CombinedEditorAttribute>().FirstOrDefault() is not { } attribute) continue;

            string[] others = attribute.OtherNames;
            IProperty?[] members = [first, .. others.Select(name => owner.Properties.FirstOrDefault(other => other.Name == name))];
            if ((!ReferenceEquals(first, property) && !others.Contains(property.Name)) || members.Contains(null)) continue;

            IPropertyAdapter[] adapters = [.. members.Select(member => PropertyAdapterFactory.CreateAdapter(member!, owner))];
            // The property panel names an unnamed row the same way.
            string name = attribute.GetName() ?? adapters[0].DisplayName;
            // A width and a height are a size; two of anything else have an axis, which a point has. Three are a vector,
            // whose type the namespaces of an expression do not hold, so its example names it in full.
            bool isSize = first.Name.EndsWith("Width", StringComparison.Ordinal);
            return adapters switch
            {
                [IAnimatablePropertyAdapter<float> a, IAnimatablePropertyAdapter<float> b] when isSize
                    => new PropertyPair<float, Graphics.Size>([a, b], name, "new Size(Sin(Time) * 100, 100)"),
                [IAnimatablePropertyAdapter<float> a, IAnimatablePropertyAdapter<float> b]
                    => new PropertyPair<float, Graphics.Point>([a, b], name, "new Point(Sin(Time) * 100, 0)"),
                [IAnimatablePropertyAdapter<int> a, IAnimatablePropertyAdapter<int> b] when isSize
                    => new PropertyPair<int, PixelSize>([a, b], name, "new PixelSize(100, 100)"),
                [IAnimatablePropertyAdapter<int> a, IAnimatablePropertyAdapter<int> b]
                    => new PropertyPair<int, PixelPoint>([a, b], name, "new PixelPoint(100, 0)"),
                [IAnimatablePropertyAdapter<float> a, IAnimatablePropertyAdapter<float> b, IAnimatablePropertyAdapter<float> c]
                    => new PropertyPair<float, Vector3>([a, b, c], name,
                        "new System.Numerics.Vector3((float)Sin(Time) * 100, 100, 100)"),
                _ => null
            };
        }

        return null;
    }

    // The properties, in the order the attribute names them.
    public IReadOnlyList<IProperty> Properties { get; }

    // An adapter of each property, whose animation shows while the properties stand apart.
    public IReadOnlyList<IAnimatablePropertyAdapter> Adapters { get; }

    public string ExpressionExample { get; }

    // The user separated the properties.
    public ReactivePropertySlim<bool> KeepsSeparated { get; } = new();

    public void SetKeepsSeparated(bool keepsSeparated)
    {
        _restoredSeparation = true;
        KeepsSeparated.Value = keepsSeparated;
    }

    public abstract IReadOnlyReactiveProperty<bool> CanStandAsOne { get; }

    public abstract IReadOnlyReactiveProperty<bool> IsSeparated { get; }

    public Type ImplementedType => Adapters[0].ImplementedType;

    public abstract Type PropertyType { get; }

    public string DisplayName { get; }

    public string? Description => Adapters[0].Description;

    public bool IsReadOnly => Adapters.Any(adapter => adapter.IsReadOnly);

    public abstract IKeyFrameAnimation? Animation { get; set; }

    public abstract IObservable<IKeyFrameAnimation?> ObserveAnimation { get; }

    public abstract IExpression? Expression { get; set; }

    public bool HasExpression => Expression != null;

    public abstract IObservable<IExpression?> ObserveExpression { get; }

    // The panel's saved state of the properties, which must not undo a choice made since by an editor that is still open.
    public void RestoreSeparation(bool keepsSeparated)
    {
        if (!_restoredSeparation)
            SetKeepsSeparated(keepsSeparated);
    }

    public bool Contains(IPropertyAdapter adapter)
        => ReferenceEquals(adapter, this) || (adapter.GetEngineProperty() is { } property && Properties.Contains(property));

    // Between the edits of the properties they differ, which must not make them stand apart.
    public abstract void BeginEdit();

    public abstract void EndEdit();

    public abstract object? GetDefaultValue();

    public abstract void SetValue(object? value);

    public abstract object? GetValue();

    public abstract IObservable<object?> GetObservable();

    public Attribute[] GetAttributes() => Properties[0].GetAttributes() ?? [];

    // The default value of each property, or null while one has no default of type T.
    internal static T[]? DefaultValues<T>(IReadOnlyList<IPropertyAdapter> properties)
    {
        var values = new T[properties.Count];
        for (int i = 0; i < values.Length; i++)
        {
            if (properties[i].GetDefaultValue() is not T value) return null;
            values[i] = value;
        }

        return values;
    }

    // What source gives while separated is false, and null while it is true.
    internal static IObservable<TItem?> NullWhile<TItem>(IObservable<bool> separated, IObservable<TItem?> source)
        where TItem : class
        => separated
            .Select(value => value ? Observable.ReturnThenNever<TItem?>(null) : source)
            .Switch()
            .DistinctUntilChanged();
}

internal abstract class PropertyPair<T>(IReadOnlyList<IAnimatablePropertyAdapter> adapters, string displayName, string expressionExample)
    : PropertyPair(adapters, displayName, expressionExample)
{
    public abstract bool TryParseExpression(string source, [NotNullWhen(true)] out IExpression<T>[]? expressions, [NotNullWhen(false)] out string? error);
}

internal sealed class PropertyPair<T, TValue> : PropertyPair<T>
    where T : struct
    where TValue : struct
{
    // Takes each property's component out of the joined value and puts it back, in the order of the properties.
    private static readonly ComponentSet<TValue, T> s_set = ComponentAnimations.GetComponentSet<TValue, T>();

    private readonly IAnimatablePropertyAdapter<T>[] _properties;
    private readonly KeyFrameAnimation<TValue> _joined = new();
    // The key of each property that a joined key stands for, in the order of the properties.
    private readonly Dictionary<KeyFrame<TValue>, KeyFrame<T>[]> _links = [];
    // Undo brings a key back with its joined key, which the graph editor's selection holds on to.
    private readonly ConditionalWeakTable<KeyFrame<T>, KeyFrame<TValue>> _joinedKeys = [];
    private readonly HashSet<SplineEasing> _watchedEasings = [];
    private readonly ReactivePropertySlim<bool> _canStandAsOne = new();
    private readonly ReactivePropertySlim<bool> _isSeparated = new();
    private readonly ReactivePropertySlim<IKeyFrameAnimation?> _animation = new();
    private int _editDepth;
    private bool _syncing;
    private bool _updatePending;

    public PropertyPair(IAnimatablePropertyAdapter<T>[] properties, string displayName, string expressionExample)
        : base(properties, displayName, expressionExample)
    {
        _properties = properties;

        // Only its parent is set: the owner gives it the time origin of the properties' keyframes, but does not hold it.
        var owner = (IHierarchical)Properties[0].GetOwnerObject()!;
        var joined = (IModifiableHierarchical)_joined;
        joined.SetParent(owner);
        owner.AttachedToHierarchy += (_, e) => joined.NotifyAttachedToHierarchy(new HierarchyAttachmentEventArgs(e.Root, owner));
        owner.DetachedFromHierarchy += (_, e) => joined.NotifyDetachedFromHierarchy(new HierarchyAttachmentEventArgs(e.Root, owner));

        _joined.Edited += (_, _) => WriteToProperties();
        _joined.KeyFrames.CollectionChanged += (_, _) => WriteToProperties();
        foreach (IAnimatablePropertyAdapter<T> property in properties)
            Observe(property);
        KeepsSeparated.Subscribe(_ => Update());
    }

    public override IReadOnlyReactiveProperty<bool> CanStandAsOne => _canStandAsOne;

    public override IReadOnlyReactiveProperty<bool> IsSeparated => _isSeparated;

    public override Type PropertyType => typeof(TValue);

    public override IKeyFrameAnimation? Animation
    {
        get => _animation.Value;
        set
        {
            BeginEdit();
            try
            {
                var animation = value as KeyFrameAnimation<TValue>;
                for (int i = 0; i < _properties.Length; i++)
                    _properties[i].Animation = animation == null ? null : Split(animation, s_set.Components[i].Get);
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
        get => _isSeparated.Value ? null : _properties[0].Expression;
        set
        {
            if (value != null) throw new NotSupportedException();

            BeginEdit();
            try
            {
                foreach (IAnimatablePropertyAdapter<T> property in _properties)
                    property.Expression = null;
            }
            finally
            {
                EndEdit();
            }
        }
    }

    public override IObservable<IExpression?> ObserveExpression
        => NullWhile(_isSeparated, ((IExpressionPropertyAdapter)_properties[0]).ObserveExpression);

    public override void BeginEdit() => _editDepth++;

    public override void EndEdit()
    {
        _editDepth--;
        Update();
    }

    public override bool TryParseExpression(string source, [NotNullWhen(true)] out IExpression<T>[]? expressions,
        [NotNullWhen(false)] out string? error)
        => ComponentExpression<TValue, T>.TryParse(source, out expressions, out error);

    public override object? GetDefaultValue() => DefaultValues<T>(_properties) is { } values ? Compose(values) : null;

    public override void SetValue(object? value)
    {
        if (value is not TValue typed) return;

        for (int i = 0; i < _properties.Length; i++)
            _properties[i].SetValue(s_set.Components[i].Get(typed));
    }

    public override object? GetValue() => Compose([.. _properties.Select(property => property.GetValue())]);

    public override IObservable<object?> GetObservable()
        => _properties.Select(property => property.GetObservable()).CombineLatest(values => (object?)Compose([.. values]));

    private void Observe(IAnimatablePropertyAdapter<T> property)
    {
        property.GetEngineProperty()!.Edited += (_, _) => OnPropertyEdited();
        property.ObserveAnimation
            .Select(animation => animation?.KeyFrames.CollectionChangedAsObservable().Select(_ => 0) ?? Observable.Never<int>())
            .Switch()
            .Subscribe(_ => OnPropertyEdited());
    }

    // The keyframes of each property, or null while one has none.
    private KeyFrameAnimation<T>[]? Animations()
    {
        var animations = new KeyFrameAnimation<T>[_properties.Length];
        for (int i = 0; i < animations.Length; i++)
        {
            if (_properties[i].Animation is not { } animation) return null;
            animations[i] = animation;
        }

        return animations;
    }

    private void OnPropertyEdited()
    {
        if (_syncing || _editDepth > 0) return;

        // Undo can take the keyframes off every property before the joined ones go.
        if (Dispatcher.UIThread.CheckAccess() && _animation.Value != null && CanStand() && Animations() is { } animations)
            ReadFromProperties(animations);

        ScheduleUpdate();
    }

    // Undo restores the properties one after the other, so whether they stand as one waits until it has finished.
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
        _canStandAsOne.Value = canStand;
        if (!separated && Animations() is { } animations)
            ReadFromProperties(animations);
        else
            WatchEasings();

        // Whoever shows the joined keyframes hears of the rows of the properties first, and of the joined keyframes before
        // the one row.
        if (separated) _isSeparated.Value = true;
        _animation.Value = !separated && _properties[0].Animation != null ? _joined : null;
        _isSeparated.Value = separated;
    }

    // Each property has what the first has: keyframes at the same times with the same easings and clock, or none.
    private bool CanStand()
    {
        if (_properties.Any(property => property.HasExpression) && !ShareOneExpression()) return false;

        KeyFrameAnimation<T>? first = _properties[0].Animation;
        for (int p = 1; p < _properties.Length; p++)
        {
            KeyFrameAnimation<T>? other = _properties[p].Animation;
            if (first == null || other == null)
            {
                if (first != other) return false;
                continue;
            }

            if (first.UseGlobalClock.Value != other.UseGlobalClock.Value || first.KeyFrames.Count != other.KeyFrames.Count)
                return false;

            for (int i = 0; i < first.KeyFrames.Count; i++)
            {
                IKeyFrame a = first.KeyFrames[i];
                IKeyFrame b = other.KeyFrames[i];
                // The menu of a property's own row is where the user removes an easing that could not be read.
                if (a.KeyTime.Value != b.KeyTime.Value || !SameEasing(a.Easing.Value, b.Easing.Value)
                    || SceneRecovery.IsRecoveryBlocker(a) || SceneRecovery.IsRecoveryBlocker(b))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Each property has its own component of the one expression.
    private bool ShareOneExpression()
    {
        if (_properties[0].Expression is not IComponentExpression first) return false;

        for (int i = 0; i < _properties.Length; i++)
        {
            if (_properties[i].Expression is not IComponentExpression expression || expression.ComponentIndex != i
                || expression.GetType() != first.GetType() || expression.Source != first.Source)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameEasing(Easing first, Easing second)
        => first is SplineEasing a && second is SplineEasing b
            ? a.X1 == b.X1 && a.Y1 == b.Y1 && a.X2 == b.X2 && a.Y2 == b.Y2
            : first.GetType() == second.GetType();

    private static Easing CopyOf(Easing easing)
        => easing is SplineEasing spline ? new SplineEasing(spline.X1, spline.Y1, spline.X2, spline.Y2) : easing;

    // The joined value of the properties' values, in the order of the properties.
    private static TValue Compose(T[] values)
    {
        TValue value = default;
        for (int i = 0; i < values.Length; i++)
            value = s_set.Components[i].Set(value, values[i]);
        return value;
    }

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

    // An edit of the joined keyframes, which goes to every property.
    private void WriteToProperties()
    {
        if (_syncing || Animations() is not { } animations) return;

        _syncing = true;
        try
        {
            foreach ((KeyFrame<TValue> key, KeyFrame<T>[] link) in _links.ToArray())
            {
                if (_joined.KeyFrames.Contains(key)) continue;

                for (int i = 0; i < animations.Length; i++)
                    animations[i].KeyFrames.Remove(link[i]);
                _links.Remove(key);
            }

            foreach (KeyFrame<TValue> key in _joined.KeyFrames.Cast<KeyFrame<TValue>>())
            {
                bool added = !_links.TryGetValue(key, out KeyFrame<T>[]? link);
                link ??= [.. animations.Select(_ => new KeyFrame<T>())];
                if (added)
                {
                    _links[key] = link;
                    _joinedKeys.AddOrUpdate(link[0], key);
                }

                WriteKey(key, link);
                if (added)
                {
                    for (int i = 0; i < animations.Length; i++)
                        animations[i].KeyFrames.Add(link[i], out _);
                }
            }

            foreach (KeyFrameAnimation<T> animation in animations)
                animation.UseGlobalClock.Value = _joined.UseGlobalClock.Value;
        }
        finally
        {
            _syncing = false;
        }

        // Reads back what each property's range made of the value.
        Update();
    }

    // The first property's key takes the easing object of the joined key, and each other key a copy of it.
    private void WriteKey(KeyFrame<TValue> key, KeyFrame<T>[] link)
    {
        foreach (KeyFrame<T> property in link)
            property.KeyTime.Value = key.KeyTime.Value;
        link[0].Easing.Value = key.Easing.Value;
        for (int i = 1; i < link.Length; i++)
        {
            if (!SameEasing(link[i].Easing.Value, key.Easing.Value))
                link[i].Easing.Value = CopyOf(key.Easing.Value);
        }

        for (int i = 0; i < link.Length; i++)
            link[i].Value.Value = s_set.Components[i].Get(key.Value.Value);
    }

    // The joined keyframes as the properties have them. A joined key has the easing object of the first property's key.
    private void ReadFromProperties(KeyFrameAnimation<T>[] animations)
    {
        _syncing = true;
        try
        {
            KeyFrameAnimation<T> first = animations[0];
            // What saves and restores an inline layer finds the first property's keyframes by this.
            _joined.Id.Value = first.Id.Value;
            _joined.UseGlobalClock.Value = first.UseGlobalClock.Value;

            _links.Clear();
            var keys = new KeyFrame<TValue>[first.KeyFrames.Count];
            var values = new T[animations.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var link = new KeyFrame<T>[animations.Length];
                for (int p = 0; p < link.Length; p++)
                {
                    link[p] = (KeyFrame<T>)animations[p].KeyFrames[i];
                    values[p] = link[p].Value.Value;
                }

                KeyFrame<TValue> key = _joinedKeys.GetValue(link[0], _ => new KeyFrame<TValue>());
                key.KeyTime.Value = link[0].KeyTime.Value;
                key.Easing.Value = link[0].Easing.Value;
                key.Value.Value = Compose(values);
                _links[key] = link;
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
        HashSet<SplineEasing> easings = [.. _properties.SelectMany(property => Easings(property.Animation))];
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
            foreach (KeyFrame<T>[] link in _links.Values)
            {
                if (!ReferenceEquals(link[0].Easing.Value, source)) continue;

                for (int i = 1; i < link.Length; i++)
                {
                    if (link[i].Easing.Value is not SplineEasing target) continue;

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
