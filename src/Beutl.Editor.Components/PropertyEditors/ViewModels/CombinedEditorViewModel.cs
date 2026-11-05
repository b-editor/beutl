using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// The row of two properties that CombinedEditorAttribute joins. Each property keeps its own value and keyframes: an
// editor of each reads and writes the values, and the row's keyframe commands run on both. While PropertyPair says the
// two stand apart the row cannot stand for both, so each takes a row of its own.
public sealed class CombinedEditorViewModel<T> : VectorEditorViewModel<(T, T), T, (T, T)>
    where T : struct, INumber<T>
{
    private const string KeepsSeparatedKey = "KeepsSeparated";

    private readonly CombinedPropertyAdapter<T> _adapter;
    private readonly ReactivePropertySlim<IReadOnlyList<IPropertyAdapter>?> _separatedProperties = new();
    private IServiceProvider? _services;

    public CombinedEditorViewModel(IAnimatablePropertyAdapter<T> first, IAnimatablePropertyAdapter<T> second, string displayName)
        : this(new CombinedPropertyAdapter<T>(first, second, displayName))
    {
    }

    private CombinedEditorViewModel(CombinedPropertyAdapter<T> adapter)
        : base(adapter, value => value, edit => edit, ValueHeader(adapter.Properties[0]), ValueHeader(adapter.Properties[1]))
    {
        _adapter = adapter;
        Disposables.Add(adapter);
        CanUnseparateComponents = CanEditProperty
            .CombineLatest(adapter.CanShareRow, (canEdit, canShareRow) => canEdit && canShareRow)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
        _separatedProperties.AddTo(Disposables);
        ShowSeparation();
        // After the edit that changed this, which can be running in the editor of a row that showing this replaces.
        adapter.IsSeparated.Skip(1)
            .Subscribe(_ => Dispatcher.UIThread.Post(ShowSeparation))
            .AddTo(Disposables);
    }

    internal NumberEditorViewModel<T> First => _adapter.First;

    internal NumberEditorViewModel<T> Second => _adapter.Second;

    internal override IReadOnlyList<IPropertyAdapter> EditedProperties => _adapter.Properties;

    internal override IObservable<IReadOnlyList<IPropertyAdapter>?> SeparatedProperties => _separatedProperties;

    // Each property has a name of its own, so its row needs none above it, and its menu joins the two again. In a node
    // each has an input port of its own and stays apart.
    internal override bool ShowsSeparatedHeader => false;

    internal override bool UnseparatesFromNestedEditors => !_adapter.IsInNode.Value;

    internal override string? ExpressionError => First.ExpressionError ?? Second.ExpressionError;

    internal override string ExpressionExample => _adapter.Pair.ExpressionExample;

    internal override IAnimatablePropertyAdapter InlineProperty => _adapter.Pair;

    public override bool CanSeparateComponents => true;

    public override IReadOnlyReactiveProperty<bool> CanUnseparateComponents { get; }

    public override string UnseparateComponentsDescription => Strings.UnseparateComponents_SameTiming;

    // Only the view changes: the properties were separate all along, so this takes no history entry.
    public override void SeparateComponents()
    {
        _adapter.Pair.SetKeepsSeparated(true);
        ShowSeparation();
    }

    public override void UnseparateComponents()
    {
        if (!CanUnseparateComponents.Value) return;

        _adapter.Pair.SetKeepsSeparated(false);
        ShowSeparation();
    }

    public override void InsertKeyFrame(MediaTime keyTime) => EditBoth(editor => editor.InsertKeyFrame(keyTime));

    // Through the joined keyframes: an editor of each property would fit that property's next easing to what remains, and
    // the two would then differ.
    public override void RemoveKeyFrame(MediaTime keyTime)
    {
        if (!IsElementEditable || GetAnimation() is not { } animation
            || AnimationOperations.FindKeyFrame(animation, keyTime, Logger, AnimationOwner) is not { } keyFrame)
        {
            return;
        }

        AnimationOperations.RemoveKeyFrame(animation, keyFrame, Logger);
        Commit();
    }

    public override void PrepareToEditAnimation() => EditBoth(editor => editor.PrepareToEditAnimation());

    public override void RemoveAnimation() => EditBoth(editor => editor.RemoveAnimation());

    public override string? GetExpressionString() => (_adapter.Expression as IComponentExpression)?.Source;

    // One expression, which returns a value with a component for each property.
    public override bool SetExpression(string expressionString, [NotNullWhen(false)] out string? error)
    {
        if (!_adapter.Pair.TryParseExpression(expressionString, out IExpression<T>[]? expressions, out error))
            return false;

        string? failure = null;
        _adapter.EditBoth(() =>
        {
            if (First.SetExpression(expressions[0], out failure))
                Second.SetExpression(expressions[1], out failure);
        });
        Commit();
        error = failure;
        return failure == null;
    }

    public override void RemoveExpression() => EditBoth(editor => editor.RemoveExpression());

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (IsDisposed || visitor is not IServiceProvider services) return;

        _services = services;
        var child = new ChildVisitor(this);
        _adapter.First.Accept(child);
        _adapter.Second.Accept(child);
        // Each property of an object in a node has an input port of its own, which needs a row of its own.
        _adapter.IsInNode.Value = services.GetService<IPropertyEditorControlHost>() != null;
        ShowSeparation();
    }

    public override object? GetService(Type serviceType)
    {
        // The properties belong to the object that holds this row, so a host follows them from there.
        return serviceType == typeof(IPropertyEditorControlHost)
            ? _services?.GetService(serviceType)
            : base.GetService(serviceType);
    }

    public override void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue(KeepsSeparatedKey, out bool keepsSeparated))
        {
            _adapter.Pair.RestoreSeparation(keepsSeparated);
            ShowSeparation();
        }
    }

    public override void WriteToJson(JsonObject json) => json[KeepsSeparatedKey] = _adapter.Pair.KeepsSeparated.Value;

    private void ShowSeparation()
    {
        if (!IsDisposed)
            _separatedProperties.Value = _adapter.IsSeparated.Value ? EditedProperties : null;
    }

    private void EditBoth(Action<PropertyEditorViewModel> edit)
    {
        if (IsDisposed) return;

        _adapter.EditBoth(() =>
        {
            edit(First);
            edit(Second);
        });
        Commit();
    }

    // The axis or dimension the property's name ends with, as the row already names what it is: "Scale X" shows as "X".
    private static string ValueHeader(IPropertyAdapter property)
    {
        string name = property.GetEngineProperty()!.Name;
        if (name.EndsWith("Width", StringComparison.Ordinal)) return Strings.Width;
        if (name.EndsWith("Height", StringComparison.Ordinal)) return Strings.Height;
        if (name.EndsWith('X')) return "X";
        if (name.EndsWith('Y')) return "Y";
        return property.DisplayName;
    }
}

// The two properties as the one value of the row, which is PropertyPair unless the row is in a node.
internal sealed class CombinedPropertyAdapter<T> : IPropertyAdapter<(T, T)>, IAnimatablePropertyAdapter, IExpressionPropertyAdapter, IDisposable
    where T : struct, INumber<T>
{
    private readonly IAnimatablePropertyAdapter<T> _first;
    private readonly IAnimatablePropertyAdapter<T> _second;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ReactivePropertySlim<bool> _isSeparated = new();

    public CombinedPropertyAdapter(IAnimatablePropertyAdapter<T> first, IAnimatablePropertyAdapter<T> second, string displayName)
    {
        _first = first;
        _second = second;
        DisplayName = displayName;
        Properties = [first, second];
        Pair = (PropertyPair<T>)PropertyPair.Find(first)!;
        First = new NumberEditorViewModel<T>(first);
        Second = new NumberEditorViewModel<T>(second);
        Pair.IsSeparated.Subscribe(_ => Update()).AddTo(_subscriptions);
        IsInNode.Subscribe(_ => Update()).AddTo(_subscriptions);
    }

    public PropertyPair<T> Pair { get; }

    public IReadOnlyList<IPropertyAdapter> Properties { get; }

    // These read and write the values, so that a value goes where its property's own editor would put it.
    public NumberEditorViewModel<T> First { get; }

    public NumberEditorViewModel<T> Second { get; }

    public ReactivePropertySlim<bool> IsInNode { get; } = new();

    public IReadOnlyReactiveProperty<bool> CanShareRow => Pair.CanStandForBoth;

    public IReadOnlyReactiveProperty<bool> IsSeparated => _isSeparated;

    public Type ImplementedType => _first.ImplementedType;

    // What the row's hover text names; the value of each editor is one of these.
    public Type PropertyType => typeof(T);

    public string DisplayName { get; }

    public string? Description => _first.Description;

    public bool IsReadOnly => _first.IsReadOnly || _second.IsReadOnly;

    // The row's commands replace each property's keyframes through its editor.
    public IKeyFrameAnimation? Animation
    {
        get => _isSeparated.Value ? null : Pair.Animation;
        set => throw new NotSupportedException();
    }

    public IObservable<IKeyFrameAnimation?> ObserveAnimation => _isSeparated
        .Select(separated => separated ? Observable.ReturnThenNever<IKeyFrameAnimation?>(null) : Pair.ObserveAnimation)
        .Switch()
        .DistinctUntilChanged();

    // The row's commands replace each property's expression through its editor.
    public IExpression? Expression
    {
        get => _isSeparated.Value ? null : Pair.Expression;
        set => throw new NotSupportedException();
    }

    public bool HasExpression => Expression != null;

    public IObservable<IExpression?> ObserveExpression => _isSeparated
        .Select(separated => separated ? Observable.ReturnThenNever<IExpression?>(null) : Pair.ObserveExpression)
        .Switch()
        .DistinctUntilChanged();

    Attribute[] IPropertyAdapter.GetAttributes() => _first.GetAttributes();

    public object? GetDefaultValue()
        => _first.GetDefaultValue() is T first && _second.GetDefaultValue() is T second ? (first, second) : null;

    public (T, T) GetValue() => (First.GetEditingValue(), Second.GetEditingValue());

    public void SetValue((T, T) value)
    {
        if (First.GetEditingValue() != value.Item1)
            First.SetCurrentValueAndGetCoerced(value.Item1);
        if (Second.GetEditingValue() != value.Item2)
            Second.SetCurrentValueAndGetCoerced(value.Item2);
    }

    public IObservable<(T, T)> GetObservable()
        => First.EditValue.CombineLatest(Second.EditValue, (first, second) => (first, second));

    public void EditBoth(Action edit)
    {
        Pair.BeginEdit();
        First.DefersCommit = Second.DefersCommit = true;
        try
        {
            edit();
        }
        finally
        {
            First.DefersCommit = Second.DefersCommit = false;
            Pair.EndEdit();
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        First.Dispose();
        Second.Dispose();
        IsInNode.Dispose();
        _isSeparated.Dispose();
    }

    private void Update()
    {
        if (!_subscriptions.IsDisposed)
            _isSeparated.Value = Pair.IsSeparated.Value || IsInNode.Value;
    }
}
