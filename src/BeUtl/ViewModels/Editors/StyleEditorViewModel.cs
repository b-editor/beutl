using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using BeUtl.Framework;
using BeUtl.Services.PrimitiveImpls;
using BeUtl.Styling;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public class StyleEditorViewModel : IToolContext
{
    private static Type[]? s_cache;

    public StyleEditorViewModel(EditViewModel editViewModel, Style style)
    {
        Style = style;
        EditorContext = editViewModel;
        Header = new ReactivePropertySlim<string>("AAAA");

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
                    .Where(x => !x.IsAbstract
                        && x.IsPublic
                        && x.IsAssignableTo(typeof(IStyleable))
                        && x.GetConstructor(Array.Empty<Type>()) != null)
                    .ToArray();
            });
        }

        TargetType.Value = Style.TargetType;
        TargetType.Subscribe(v => Style.TargetType = v ?? typeof(Styleable));
    }

    public Style Style { get; }

    public ReactivePropertySlim<Type[]?> StyleableTypes { get; } = new();

    public ReactivePropertySlim<Type?> TargetType { get; } = new();

    public EditViewModel EditorContext { get; }

    public ToolTabExtension Extension => StyleEditorTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void Dispose()
    {

    }

    public void ReadFromJson(JsonNode json)
    {

    }

    public void WriteToJson(ref JsonNode json)
    {

    }
}
