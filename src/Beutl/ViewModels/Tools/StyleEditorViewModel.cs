using System.Buffers;
using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Operation;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Styling;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class StyleEditorViewModel : IToolContext
{
    private static Type[]? s_cache;
    private readonly IDisposable _disposable0;
    private IDisposable? _disposable1;

    public StyleEditorViewModel(EditViewModel editViewModel)
    {
        EditorContext = editViewModel;
        Header = new ReactivePropertySlim<string>(Strings.Style);

        if (s_cache != null)
        {
            StyleableTypes.Value = s_cache;
        }
        else
        {
            Task.Run(() =>
            {
                StyleableTypes.Value = s_cache = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .Where(x => x.IsPublic && x.IsAssignableTo(typeof(ICoreObject)))
                    .ToArray();
            });
        }

        IsStyleNotNull = Style.Select(x => x != null).ToReadOnlyReactivePropertySlim();
        TargetType = Style.Select(x => x?.TargetType).ToReactiveProperty();
        TargetType.Subscribe(v =>
        {
            if (Style.Value != null)
            {
                Style.Value.TargetType = v ?? typeof(ICoreObject);
            }
        });

        _disposable0 = Style.Subscribe(style =>
        {
            ClearItems();
            _disposable1?.Dispose();
            _disposable1 = null;
            if (style != null)
            {
                _disposable1 = style.Setters.ForEachItem(
                    (idx, item) =>
                    {
                        Type wrapperType = typeof(StylingSetterPropertyImpl<>);
                        wrapperType = wrapperType.MakeGenericType(item.Property.PropertyType);
                        var wrapper = (IAbstractProperty)Activator.CreateInstance(wrapperType, item)!;
                        CoreProperty[] tmp1 = ArrayPool<CoreProperty>.Shared.Rent(1);
                        var tmp2 = new IAbstractProperty[1];
                        tmp1[0] = item.Property;
                        tmp2[0] = wrapper;

                        if (PropertyEditorService.MatchProperty(tmp1) is { Extension: { } ext, Properties.Length: 1 }
                            && ext.TryCreateContext(tmp2, out IPropertyEditorContext? context))
                        {
                            Properties.Insert(idx, context);
                        }
                        else
                        {
                            Properties.Insert(idx, null);
                        }

                        ArrayPool<CoreProperty>.Shared.Return(tmp1);
                        ArrayPool<IAbstractProperty>.Shared.Return(tmp2);
                    },
                    (idx, _) =>
                    {
                        IPropertyEditorContext? vm = Properties[idx];
                        Properties.RemoveAt(idx);
                        vm?.Dispose();
                    },
                    () => ClearItems());
            }
        });
    }

    public ReactiveProperty<Style?> Style { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsStyleNotNull { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = new();

    public ReactivePropertySlim<Type[]?> StyleableTypes { get; } = new();

    public ReactiveProperty<Type?> TargetType { get; } = new();

    public EditViewModel EditorContext { get; }

    public ToolTabExtension Extension => StyleEditorTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    private void ClearItems()
    {
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }

        Properties.Clear();
    }

    public void Dispose()
    {
        _disposable0.Dispose();
        Header.Dispose();
        ClearItems();
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }
}
