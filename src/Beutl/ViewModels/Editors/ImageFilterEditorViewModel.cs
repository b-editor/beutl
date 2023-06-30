using System.Text.Json.Nodes;

using Beutl.Commands;
using Beutl.Framework;
using Beutl.Graphics.Filters;
using Beutl.Operators.Configure;
using Beutl.ViewModels.Tools;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class ImageFilterEditorViewModel : ValueEditorViewModel<IImageFilter?>
{
    public ImageFilterEditorViewModel(IAbstractProperty<IImageFilter?> property)
        : base(property)
    {
        FilterName = Value.Select(v => v?.GetType().Name ?? "Null")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroup = Value.Select(v => v is ImageFilterGroup)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v is ImageFilterGroup || v == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                {
                    Properties.Value?.Dispose();
                    Properties.Value = null;
                    Group.Value?.Dispose();
                    Group.Value = null;

                    if (v is ImageFilterGroup group)
                    {
                        var prop = new CorePropertyImpl<ImageFilters>(ImageFilterGroup.ChildrenProperty, group);
                        Group.Value = new ListEditorViewModel<IImageFilter>(prop)
                        {
                            IsExpanded = { Value = true }
                        };
                    }
                    else if (v is ImageFilter filter)
                    {
                        Properties.Value = new PropertiesEditorViewModel(filter, (p, m) => m.Browsable && p != ImageFilter.IsEnabledProperty);
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        IsEnabled = Value.Select(x => (x as ImageFilter)?.GetObservable(ImageFilter.IsEnabledProperty) ?? Observable.Return(x?.IsEnabled ?? false))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        IsEnabled.Skip(1)
            .Subscribe(v =>
            {
                if (Value.Value is ImageFilter filter)
                {
                    var command = new ChangePropertyCommand<bool>(filter, ImageFilter.IsEnabledProperty, v, !v);
                    command.DoAndRecord(CommandRecorder.Default);
                }
            })
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FilterName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<IImageFilter>?> Group { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        Group.Value?.Accept(visitor);

        if (Properties.Value != null)
        {
            foreach (IPropertyEditorContext item in Properties.Value.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is IImageFilter instance)
        {
            SetValue(Value.Value, instance);
        }
    }

    public void AddItem(Type type)
    {
        if (Value.Value is ImageFilterGroup group
            && Activator.CreateInstance(type) is IImageFilter instance)
        {
            group.Children.BeginRecord<IImageFilter>()
                .Add(instance)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        try
        {
            if (json.TryGetPropertyValue(nameof(IsExpanded), out var isExpandedNode)
                && isExpandedNode is JsonValue isExpanded)
            {
                IsExpanded.Value = (bool)isExpanded;
            }
            Properties.Value?.ReadFromJson(json);

            if (Group.Value != null
                && json.TryGetPropertyValue(nameof(Group), out var groupNode)
                && groupNode is JsonObject group)
            {
                Group.Value.ReadFromJson(group);
            }
        }
        catch
        {
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            Properties.Value?.WriteToJson(json);
            if (Group.Value != null)
            {
                var group = new JsonObject();
                Group.Value.WriteToJson(group);
                json[nameof(Group)] = group;
            }
        }
        catch
        {
        }
    }

    private sealed record Visitor(ImageFilterEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
