using System.ComponentModel;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.NodeTree.Rendering;
using Beutl.ProjectSystem;

namespace Beutl.NodeTree;

public class ElementNodeTreeModel : NodeTreeModel
{
    private readonly NodeTreeSnapshot _snapshot = new();
    private Element? _element;

    public ElementNodeTreeModel()
    {
        TopologyChanged += (_, _) => _snapshot.MarkDirty();
    }

    public PooledList<EngineObject> Evaluate(EvaluationTarget target, IRenderer renderer, Element element)
    {
        _snapshot.Build(this, renderer);

        var list = new PooledList<EngineObject>();
        try
        {
            _snapshot.Evaluate(target, list);

            // Todo: LayerOutputNodeに移動
            foreach (EngineObject item in list.Span)
            {
                item.ZIndex = ZIndex;
                item.TimeRange = TimeRange;
            }

            return list;
        }
        catch
        {
            list.Dispose();
            throw;
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        _element = this.FindHierarchicalParent<Element>();
        if (_element != null)
        {
            _element.PropertyChanged += OnParentElementPropertyChanged;
            UpdateValueFromElement();
        }
    }

    private void UpdateValueFromElement()
    {
        if (_element == null) return;

        IsTimeAnchor = true;
        ZIndex = _element.ZIndex;
        TimeRange = new TimeRange(_element.Start, _element.Length);
        IsEnabled = _element.IsEnabled && IsEnabled;
    }

    private void OnParentElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs args && _element != null)
        {
            if (args.Property.Id == Element.IsEnabledProperty.Id)
            {
                IsEnabled = _element.IsEnabled;
            }
            else if (args.Property.Id == Element.ZIndexProperty.Id)
            {
                ZIndex = _element.ZIndex;
            }
            else if (args.Property.Id == Element.StartProperty.Id ||
                     args.Property.Id == Element.LengthProperty.Id)
            {
                TimeRange = new TimeRange(_element.Start, _element.Length);
            }
        }
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        if (_element != null)
        {
            _element.PropertyChanged -= OnParentElementPropertyChanged;
        }

        _element = null;
    }
}
