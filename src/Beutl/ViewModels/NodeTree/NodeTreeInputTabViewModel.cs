using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Beutl.Collections;
using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using ReactiveUI;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeInputTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = new();

    public NodeTreeInputTabViewModel(EditViewModel editViewModel)
    {
        Layer = editViewModel.SelectedObject
            .Select(x => x as Layer)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Layer.CombineWithPrevious()
            .Subscribe(obj =>
            {
                InnerViewModel.Value?.Dispose();
                InnerViewModel.Value = null;
                if (obj.NewValue is Layer newValue)
                {
                    InnerViewModel.Value = new NodeTreeInputViewModel(newValue);
                }
            })
            .DisposeWith(_disposables);
    }

    public string Header => Strings.NodeTree;

    public ToolTabExtension Extension => NodeTreeInputTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public ReactiveProperty<Layer?> Layer { get; }

    public ReactivePropertySlim<NodeTreeInputViewModel?> InnerViewModel { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();

        InnerViewModel.Value?.Dispose();
        InnerViewModel.Value = null;
        Layer.Value = null;
    }

    public void ReadFromJson(JsonNode json)
    {

    }

    public void WriteToJson(ref JsonNode json)
    {

    }
}
