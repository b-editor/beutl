using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// The row of two properties that CombinedEditorAttribute joins. Each property keeps its own value and keyframes: an
// editor of each reads and writes the values, and the row's keyframe commands run on both. While the two differ in
// their keyframe times or do not share one expression the row cannot stand for both, so each takes a row of its own.
public sealed class CombinedEditorViewModel<T> : VectorEditorViewModel<(T, T), T, (T, T)>
    where T : struct, INumber<T>
{
    private const string KeepsSeparatedKey = "KeepsSeparated";

    private readonly CombinedPropertyAdapter<T> _adapter;
    private readonly CombinedExpressionFormat<T> _expressionFormat;
    private readonly ReactivePropertySlim<IReadOnlyList<IPropertyAdapter>?> _separatedProperties = new();
    private IServiceProvider? _services;

    internal CombinedEditorViewModel(IAnimatablePropertyAdapter<T> first, IAnimatablePropertyAdapter<T> second, string displayName,
        CombinedExpressionFormat<T> expressionFormat)
        : this(new CombinedPropertyAdapter<T>(first, second, displayName), expressionFormat)
    {
    }

    private CombinedEditorViewModel(CombinedPropertyAdapter<T> adapter, CombinedExpressionFormat<T> expressionFormat)
        : base(adapter, value => value, edit => edit, ValueHeader(adapter.Properties[0]), ValueHeader(adapter.Properties[1]))
    {
        _adapter = adapter;
        _expressionFormat = expressionFormat;
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

    internal override bool ShowsSeparatedHeader => !_adapter.IsInNode.Value;

    internal override string? ExpressionError => First.ExpressionError ?? Second.ExpressionError;

    internal override string ExpressionExample => _expressionFormat.Example;

    public override bool CanSeparateComponents => true;

    public override IReadOnlyReactiveProperty<bool> CanUnseparateComponents { get; }

    public override string UnseparateComponentsDescription => Strings.UnseparateComponents_SameTiming;

    // Only the view changes: the properties were separate all along, so this takes no history entry.
    public override void SeparateComponents()
    {
        _adapter.KeepsSeparated.Value = true;
        ShowSeparation();
    }

    public override void UnseparateComponents()
    {
        if (!CanUnseparateComponents.Value) return;

        _adapter.KeepsSeparated.Value = false;
        ShowSeparation();
    }

    public override void InsertKeyFrame(MediaTime keyTime) => EditBoth(editor => editor.InsertKeyFrame(keyTime));

    public override void RemoveKeyFrame(MediaTime keyTime) => EditBoth(editor => editor.RemoveKeyFrame(keyTime));

    public override void PrepareToEditAnimation() => EditBoth(editor => editor.PrepareToEditAnimation());

    public override void RemoveAnimation() => EditBoth(editor => editor.RemoveAnimation());

    public override string? GetExpressionString() => (_adapter.Expression as IComponentExpression)?.Source;

    // One expression, which returns a value with a component for each property.
    public override bool SetExpression(string expressionString, [NotNullWhen(false)] out string? error)
    {
        if (!_expressionFormat.Parse(expressionString, out IExpression<T>[]? expressions, out error))
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
            _adapter.KeepsSeparated.Value = keepsSeparated;
            ShowSeparation();
        }
    }

    public override void WriteToJson(JsonObject json) => json[KeepsSeparatedKey] = _adapter.KeepsSeparated.Value;

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

// The expression a row's two properties share, which returns a value with a component for each of them.
internal sealed record CombinedExpressionFormat<T>(string Example, CombinedExpressionFormat<T>.Parser Parse)
{
    public delegate bool Parser(string source, [NotNullWhen(true)] out IExpression<T>[]? expressions, [NotNullWhen(false)] out string? error);
}

// The two properties as the one value of the row. While the row shows, the first property's keyframes stand for both's,
// since the two have the same keyframe times then.
internal sealed class CombinedPropertyAdapter<T> : IPropertyAdapter<(T, T)>, IAnimatablePropertyAdapter, IExpressionPropertyAdapter, IDisposable
    where T : struct, INumber<T>
{
    private readonly IAnimatablePropertyAdapter<T> _first;
    private readonly IAnimatablePropertyAdapter<T> _second;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ReactivePropertySlim<bool> _canShareRow = new();
    private readonly ReactivePropertySlim<bool> _isSeparated = new();
    private bool _isEditingBoth;

    public CombinedPropertyAdapter(IAnimatablePropertyAdapter<T> first, IAnimatablePropertyAdapter<T> second, string displayName)
    {
        _first = first;
        _second = second;
        DisplayName = displayName;
        Properties = [first, second];
        First = new NumberEditorViewModel<T>(first);
        Second = new NumberEditorViewModel<T>(second);
        ObserveTiming(first);
        ObserveTiming(second);
        KeepsSeparated.Subscribe(_ => Update()).AddTo(_subscriptions);
        IsInNode.Subscribe(_ => Update()).AddTo(_subscriptions);
    }

    public IReadOnlyList<IPropertyAdapter> Properties { get; }

    // These read and write the values, so that a value goes where its property's own editor would put it.
    public NumberEditorViewModel<T> First { get; }

    public NumberEditorViewModel<T> Second { get; }

    // The user separated the two.
    public ReactivePropertySlim<bool> KeepsSeparated { get; } = new();

    public ReactivePropertySlim<bool> IsInNode { get; } = new();

    public IReadOnlyReactiveProperty<bool> CanShareRow => _canShareRow;

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
        get => _isSeparated.Value ? null : _first.Animation;
        set => throw new NotSupportedException();
    }

    public IObservable<IKeyFrameAnimation?> ObserveAnimation => _isSeparated
        .Select(separated => separated
            ? Observable.ReturnThenNever<IKeyFrameAnimation?>(null)
            : _first.ObserveAnimation.Select(IKeyFrameAnimation? (animation) => animation))
        .Switch()
        .DistinctUntilChanged();

    // The row's commands replace each property's expression through its editor.
    public IExpression? Expression
    {
        get => _isSeparated.Value ? null : _first.Expression;
        set => throw new NotSupportedException();
    }

    public bool HasExpression => Expression != null;

    public IObservable<IExpression?> ObserveExpression => _isSeparated
        .Select(separated => separated
            ? Observable.ReturnThenNever<IExpression?>(null)
            : ((IExpressionPropertyAdapter)_first).ObserveExpression)
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

    // Between the two edits the properties differ, which must not split the row.
    public void EditBoth(Action edit)
    {
        _isEditingBoth = First.DefersCommit = Second.DefersCommit = true;
        try
        {
            edit();
        }
        finally
        {
            _isEditingBoth = First.DefersCommit = Second.DefersCommit = false;
            Update();
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        First.Dispose();
        Second.Dispose();
        KeepsSeparated.Dispose();
        IsInNode.Dispose();
        _canShareRow.Dispose();
        _isSeparated.Dispose();
    }

    private void ObserveTiming(IAnimatablePropertyAdapter<T> property)
    {
        IProperty engineProperty = property.GetEngineProperty()!;
        Observable.FromEventPattern(h => engineProperty.Edited += h, h => engineProperty.Edited -= h)
            .Subscribe(_ => Update())
            .AddTo(_subscriptions);
        property.ObserveAnimation
            .Select(animation => animation?.KeyFrames.CollectionChangedAsObservable().Select(_ => 0) ?? Observable.Never<int>())
            .Switch()
            .Subscribe(_ => Update())
            .AddTo(_subscriptions);
    }

    private void Update()
    {
        if (_isEditingBoth || _subscriptions.IsDisposed) return;

        _canShareRow.Value = CanStandForBoth();
        _isSeparated.Value = KeepsSeparated.Value || IsInNode.Value || !_canShareRow.Value;
    }

    private bool ShareOneExpression()
        => _first.Expression is IComponentExpression { ComponentIndex: 0 } first
           && _second.Expression is IComponentExpression { ComponentIndex: 1 } second
           && first.GetType() == second.GetType()
           && first.Source == second.Source;

    private bool CanStandForBoth()
    {
        if ((_first.HasExpression || _second.HasExpression) && !ShareOneExpression()) return false;

        KeyFrameAnimation<T>? first = _first.Animation;
        KeyFrameAnimation<T>? second = _second.Animation;
        if (first == null || second == null) return first == second;
        if (first.UseGlobalClock.Value != second.UseGlobalClock.Value || first.KeyFrames.Count != second.KeyFrames.Count)
            return false;

        // The menu of a property's own row is where the user removes an easing that could not be read.
        for (int i = 0; i < first.KeyFrames.Count; i++)
        {
            if (first.KeyFrames[i].KeyTime.Value != second.KeyFrames[i].KeyTime.Value
                || SceneRecovery.IsRecoveryBlocker(first.KeyFrames[i])
                || SceneRecovery.IsRecoveryBlocker(second.KeyFrames[i]))
            {
                return false;
            }
        }

        return true;
    }
}
