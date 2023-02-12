using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Commands;
using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.Services;

using FluentAvalonia.UI.Media;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public NodeViewModel(Node node)
    {
        Node = node;
        Type nodeType = node.GetType();
        if (NodeRegistry.FindItem(nodeType) is { } regItem)
        {
            NodeName = regItem.DisplayName;

            var color = new Color2(regItem.AccentColor.ToAvalonia());
            Color = new ImmutableLinearGradientBrush(
                new[]
                {
                    new ImmutableGradientStop(0, color.WithAlphaf(0.1f)),
                    new ImmutableGradientStop(1, color.WithAlphaf(0.01f))
                },
                startPoint: RelativePoint.TopLeft,
                endPoint: RelativePoint.BottomRight);
        }
        else
        {
            NodeName = nodeType.Name;
            Color = Brushes.Transparent;
        }

        IsExpanded = node.GetObservable(Node.IsExpandedProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        IsExpanded.Subscribe(v => Node.IsExpanded = v)
            .DisposeWith(_disposables);

        Position = node.GetObservable(Node.PositionProperty)
            .Select(x => new Point(x.X, x.Y))
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Delete.Subscribe(() =>
        {
            NodeTreeSpace? tree = Node.FindLogicalParent<NodeTreeSpace>();
            if (tree != null)
            {
                new RemoveCommand<Node>(tree.Nodes, Node)
                    .DoAndRecord(CommandRecorder.Default);
            }
        });

        InitItems();
    }

    public Node Node { get; }

    public string NodeName { get; }

    public IBrush Color { get; }

    public ReactiveProperty<bool> IsSelected { get; } = new();

    public ReactiveProperty<Point> Position { get; }

    public ReactiveProperty<bool> IsExpanded { get; }

    public ReactiveCommand Delete { get; } = new();

    public CoreList<NodeItemViewModel> Items { get; } = new();

    public void Dispose()
    {
        foreach (NodeItemViewModel item in Items)
        {
            item.Dispose();
        }
        Items.Clear();
        Position.Dispose();
        _disposables.Dispose();
    }

    private void InitItems()
    {
        var ctmp = new CoreProperty[1];
        var atmp = new IAbstractProperty[1];
        foreach (INodeItem item in Node.Items)
        {
            IPropertyEditorContext? context = null;
            if (item.Property is { } aproperty)
            {
                ctmp[0] = aproperty.Property;
                atmp[0] = aproperty;
                (_, PropertyEditorExtension ext) = PropertyEditorService.MatchProperty(ctmp);
                ext?.TryCreateContextForNode(atmp, out context);
            }

            Items.Add(CreateNodeItemViewModel(item, context));
        }
    }

    private static NodeItemViewModel CreateNodeItemViewModel(INodeItem nodeItem, IPropertyEditorContext? propertyEditorContext)
    {
        return nodeItem switch
        {
            IOutputSocket osocket => new OutputSocketViewModel(osocket, propertyEditorContext),
            IInputSocket isocket => new InputSocketViewModel(isocket, propertyEditorContext),
            ISocket socket => new SocketViewModel(socket, propertyEditorContext),
            _ => new NodeItemViewModel(nodeItem, propertyEditorContext),
        };
    }

    public void NotifyPositionChange()
    {
        Node.Position = (Position.Value.X, Position.Value.Y);
    }
}
