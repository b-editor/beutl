using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Services;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using ReactiveUI;

namespace Beutl.ViewModels.Editors;

public sealed class PathOperationEditorViewModel : ValueEditorViewModel<PathSegment?>
{
    public PathOperationEditorViewModel(IAbstractProperty<PathSegment?> property)
        : base(property)
    {
        OpName = Value.Select(v =>
            {
                if (v != null)
                {
                    var name = v switch
                    {
#pragma warning disable CS0618 // Type or member is obsolete
                        CloseOperation => Strings.Close,
                        MoveOperation => Strings.Move,
#pragma warning restore CS0618 // Type or member is obsolete
                        ArcSegment => Strings.EllipticalArc,
                        ConicSegment => Strings.Conic,
                        CubicBezierSegment => Strings.CubicBezierCurve,
                        LineSegment => Strings.Line,
                        QuadraticBezierSegment => Strings.QuadraticBezierCurve,
                        _ => null,
                    };

                    if (name == null)
                    {
                        Type type = v.GetType();
                        name = LibraryService.Current.FindItem(type)?.DisplayName ?? type.Name;
                    }

                    return name;
                }
                else
                {
                    return "Null";
                }
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.Subscribe(_ => ProgrammaticallyExpanded = false)
            .DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                {
                    Properties.Value?.Dispose();
                    Properties.Value = null;

                    if (v is PathSegment obj)
                    {
                        Properties.Value = new PropertiesEditorViewModel(obj, (p, m) => m.Browsable);
                    }

                    AcceptProperties();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .WhereNotNull()
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> OpName { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public bool ProgrammaticallyExpanded { get; set; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptProperties();
    }

    private void AcceptProperties()
    {
        var visitor = new Visitor(this);

        if (Properties.Value != null)
        {
            foreach (IPropertyEditorContext item in Properties.Value.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public void ChangeType(Type type)
    {
        if (Activator.CreateInstance(type) is PathSegment instance)
        {
            SetValue(Value.Value, instance);
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
            if (json.TryGetPropertyValue(nameof(ProgrammaticallyExpanded), out var pExpandedNode)
                && isExpandedNode is JsonValue pExpanded)
            {
                ProgrammaticallyExpanded = (bool)pExpanded;
            }

            Properties.Value?.ReadFromJson(json);
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
            json[nameof(ProgrammaticallyExpanded)] = ProgrammaticallyExpanded;
            Properties.Value?.WriteToJson(json);
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
    }

    private sealed record Visitor(PathOperationEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
