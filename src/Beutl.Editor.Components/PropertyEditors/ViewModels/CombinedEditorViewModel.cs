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

// The row of the two or three properties that CombinedEditorAttribute joins, whose values TTuple holds in their order.
// Each property keeps its own value and keyframes: an editor of each reads and writes the values, and the row's keyframe
// commands run on all of them. While PropertyPair says they stand apart the row cannot stand for them, so each takes a row
// of its own.
public sealed class CombinedEditorViewModel<T, TTuple> : VectorEditorViewModel<TTuple, T, TTuple>
    where T : struct, INumber<T>
    where TTuple : struct
{
    private const string KeepsSeparatedKey = "KeepsSeparated";

    private readonly CombinedPropertyAdapter<T, TTuple> _adapter;
    private readonly ReactivePropertySlim<IReadOnlyList<IPropertyAdapter>?> _separatedProperties = new();
    private IServiceProvider? _services;

    // split and compose turn the row's value into the value of each property, in their order, and back.
    internal CombinedEditorViewModel(IReadOnlyList<IAnimatablePropertyAdapter<T>> properties, string displayName,
        Func<TTuple, T[]> split, Func<T[], TTuple> compose)
        : this(new CombinedPropertyAdapter<T, TTuple>(properties, displayName, split, compose))
    {
    }

    private CombinedEditorViewModel(CombinedPropertyAdapter<T, TTuple> adapter)
        : base(adapter, value => value, edit => edit, [.. adapter.Properties.Select(ValueHeader)])
    {
        _adapter = adapter;
        Disposables.Add(adapter);
        CanUnseparateComponents = CanEditProperty
            .CombineLatest(adapter.Pair.CanStandAsOne, (canEdit, canShareRow) => canEdit && canShareRow)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
        _separatedProperties.AddTo(Disposables);
        ShowSeparation();
        // After the edit that changed this, which can be running in the editor of a row that showing this replaces.
        adapter.IsSeparated.Skip(1)
            .Subscribe(_ => Dispatcher.UIThread.Post(ShowSeparation))
            .AddTo(Disposables);
    }

    // An editor of each property, in their order.
    internal IReadOnlyList<NumberEditorViewModel<T>> Components => _adapter.Components;

    internal override IReadOnlyList<IPropertyAdapter> EditedProperties => _adapter.Properties;

    internal override IObservable<IReadOnlyList<IPropertyAdapter>?> SeparatedProperties => _separatedProperties;

    // Each property has a name of its own, so its row needs none above it, and its menu joins the two again. In a node
    // each has an input port of its own and stays apart.
    internal override bool ShowsSeparatedHeader => false;

    internal override bool UnseparatesFromNestedEditors => !_adapter.IsInNode.Value;

    internal override string? ExpressionError
        => _adapter.Components.Select(component => component.ExpressionError).FirstOrDefault(error => error != null);

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

    public override void InsertKeyFrame(MediaTime keyTime) => EditAll(editor => editor.InsertKeyFrame(keyTime));

    // Through the joined keyframes: an editor of each property would fit that property's next easing to what remains, and
    // the properties would then differ.
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

    public override void PrepareToEditAnimation() => EditAll(editor => editor.PrepareToEditAnimation());

    public override void RemoveAnimation() => EditAll(editor => editor.RemoveAnimation());

    public override string? GetExpressionString() => (_adapter.Expression as IComponentExpression)?.Source;

    // One expression, which returns a value with a component for each property.
    public override bool SetExpression(string expressionString, [NotNullWhen(false)] out string? error)
    {
        if (!_adapter.Pair.TryParseExpression(expressionString, out IExpression<T>[]? expressions, out error))
            return false;

        string? failure = null;
        _adapter.EditAll(() =>
        {
            foreach ((NumberEditorViewModel<T> component, IExpression<T> expression) in _adapter.Components.Zip(expressions))
            {
                if (!component.SetExpression(expression, out failure)) break;
            }
        });
        Commit();
        error = failure;
        return failure == null;
    }

    public override void RemoveExpression() => EditAll(editor => editor.RemoveExpression());

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (IsDisposed || visitor is not IServiceProvider services) return;

        _services = services;
        var child = new ChildVisitor(this);
        foreach (NumberEditorViewModel<T> component in _adapter.Components)
            component.Accept(child);
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

    private void EditAll(Action<PropertyEditorViewModel> edit)
    {
        if (IsDisposed) return;

        _adapter.EditAll(() =>
        {
            foreach (NumberEditorViewModel<T> component in _adapter.Components)
                edit(component);
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
        if (name.EndsWith('Z')) return "Z";
        return property.DisplayName;
    }
}

// The properties as the one value of the row, which is PropertyPair unless the row is in a node.
internal sealed class CombinedPropertyAdapter<T, TTuple>
    : IPropertyAdapter<TTuple>, IAnimatablePropertyAdapter, IExpressionPropertyAdapter, IDisposable
    where T : struct, INumber<T>
    where TTuple : struct
{
    private readonly IAnimatablePropertyAdapter<T> _first;
    private readonly Func<TTuple, T[]> _split;
    private readonly Func<T[], TTuple> _compose;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ReactivePropertySlim<bool> _isSeparated = new();

    public CombinedPropertyAdapter(IReadOnlyList<IAnimatablePropertyAdapter<T>> properties, string displayName,
        Func<TTuple, T[]> split, Func<T[], TTuple> compose)
    {
        _first = properties[0];
        _split = split;
        _compose = compose;
        DisplayName = displayName;
        Properties = properties;
        Pair = (PropertyPair<T>)PropertyPair.Find(_first)!;
        Components = [.. properties.Select(property => new NumberEditorViewModel<T>(property))];
        Pair.IsSeparated.Subscribe(_ => Update()).AddTo(_subscriptions);
        IsInNode.Subscribe(_ => Update()).AddTo(_subscriptions);
    }

    public PropertyPair<T> Pair { get; }

    public IReadOnlyList<IPropertyAdapter> Properties { get; }

    // These read and write the values, so that a value goes where its property's own editor would put it.
    public IReadOnlyList<NumberEditorViewModel<T>> Components { get; }

    public ReactivePropertySlim<bool> IsInNode { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsSeparated => _isSeparated;

    public Type ImplementedType => _first.ImplementedType;

    // What the row's hover text names; the value of each editor is one of these.
    public Type PropertyType => typeof(T);

    public string DisplayName { get; }

    public string? Description => _first.Description;

    public bool IsReadOnly => Properties.Any(property => property.IsReadOnly);

    // The row's commands replace each property's keyframes through its editor.
    public IKeyFrameAnimation? Animation
    {
        get => _isSeparated.Value ? null : Pair.Animation;
        set => throw new NotSupportedException();
    }

    public IObservable<IKeyFrameAnimation?> ObserveAnimation => PropertyPair.NullWhile(_isSeparated, Pair.ObserveAnimation);

    // The row's commands replace each property's expression through its editor.
    public IExpression? Expression
    {
        get => _isSeparated.Value ? null : Pair.Expression;
        set => throw new NotSupportedException();
    }

    public bool HasExpression => Expression != null;

    public IObservable<IExpression?> ObserveExpression => PropertyPair.NullWhile(_isSeparated, Pair.ObserveExpression);

    Attribute[] IPropertyAdapter.GetAttributes() => _first.GetAttributes();

    public object? GetDefaultValue() => PropertyPair.DefaultValues<T>(Properties) is { } values ? _compose(values) : null;

    public TTuple GetValue() => _compose([.. Components.Select(component => component.GetEditingValue())]);

    public void SetValue(TTuple value)
    {
        T[] values = _split(value);
        for (int i = 0; i < Components.Count; i++)
        {
            if (Components[i].GetEditingValue() != values[i])
                Components[i].SetCurrentValueAndGetCoerced(values[i]);
        }
    }

    public IObservable<TTuple> GetObservable()
        => Components.Select(component => component.EditValue).CombineLatest(values => _compose([.. values]));

    public void EditAll(Action edit)
    {
        Pair.BeginEdit();
        foreach (NumberEditorViewModel<T> component in Components)
            component.DefersCommit = true;
        try
        {
            edit();
        }
        finally
        {
            foreach (NumberEditorViewModel<T> component in Components)
                component.DefersCommit = false;
            Pair.EndEdit();
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (NumberEditorViewModel<T> component in Components)
            component.Dispose();
        IsInNode.Dispose();
        _isSeparated.Dispose();
    }

    private void Update()
    {
        if (!_subscriptions.IsDisposed)
            _isSeparated.Value = Pair.IsSeparated.Value || IsInNode.Value;
    }
}
