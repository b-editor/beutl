using System.Text.Json.Nodes;

using BeUtl.Framework;
using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Services.PrimitiveImpls;
using BeUtl.Styling;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class StyleEditorViewModel : IToolContext
{
    private static Type[]? s_cache;
    private readonly IDisposable _disposable0;
    private IDisposable? _disposable1;

    public StyleEditorViewModel(EditViewModel editViewModel)
    {
        EditorContext = editViewModel;
        Header = new ReactivePropertySlim<string>("Style");

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
                    .Where(x => x.IsPublic && x.IsAssignableTo(typeof(IStyleable)))
                    .ToArray();
            });
        }

        IsStyleNotNull = Style.Select(x => x != null).ToReadOnlyReactivePropertySlim();
        TargetType = Style.Select(x => x?.TargetType).ToReactiveProperty();
        TargetType.Subscribe(v =>
        {
            if (Style.Value != null)
            {
                Style.Value.TargetType = v ?? typeof(Styleable);
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
                        Type wrapperType = typeof(StylingSetterWrapper<>);
                        wrapperType = wrapperType.MakeGenericType(item.Property.PropertyType);
                        var wrapper = (IWrappedProperty)Activator.CreateInstance(wrapperType, item)!;

                        BaseEditorViewModel? itemViewModel = PropertyEditorService.CreateEditorViewModel(wrapper);

                        Properties.Insert(idx, itemViewModel);
                    },
                    (idx, _) =>
                    {
                        BaseEditorViewModel? vm = Properties[idx];
                        Properties.RemoveAt(idx);
                        vm?.Dispose();
                    },
                    () => ClearItems());
            }
        });
    }

    public ReactiveProperty<Style?> Style { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsStyleNotNull { get; }

    public CoreList<BaseEditorViewModel?> Properties { get; } = new()
    {
        ResetBehavior = ResetBehavior.Remove
    };

    public ReactivePropertySlim<Type[]?> StyleableTypes { get; } = new();

    public ReactiveProperty<Type?> TargetType { get; } = new();

    public EditViewModel EditorContext { get; }

    public ToolTabExtension Extension => StyleEditorTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    private void ClearItems()
    {
        BaseEditorViewModel?[] tmp = Properties.GetMarshal().Value.ToArray();
        Properties.Clear();
        foreach (BaseEditorViewModel? item in tmp)
        {
            item?.Dispose();
        }
    }

    public void Dispose()
    {
        _disposable0.Dispose();
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }
}
