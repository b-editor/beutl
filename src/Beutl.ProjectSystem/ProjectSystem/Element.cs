using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Collections.Pooled;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeTree;
using Beutl.Operation;
using Beutl.Rendering;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

public class Element : ProjectItem
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> LengthProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<Color> AccentColorProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<SourceOperation> OperationProperty;
    public static readonly CoreProperty<ElementNodeTreeModel> NodeTreeProperty;
    public static readonly CoreProperty<bool> UseNodeProperty;
    private readonly InstanceClock _instanceClock = new();
    private TimeSpan _start;
    private TimeSpan _length;
    private int _zIndex;
    private bool _isEnabled = true;
    private bool _useNode;

    static Element()
    {
        StartProperty = ConfigureProperty<TimeSpan, Element>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Register();

        LengthProperty = ConfigureProperty<TimeSpan, Element>(nameof(Length))
            .Accessor(o => o.Length, (o, v) => o.Length = v)
            .Register();

        ZIndexProperty = ConfigureProperty<int, Element>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .Register();

        AccentColorProperty = ConfigureProperty<Color, Element>(nameof(AccentColor))
            .DefaultValue(Colors.Teal)
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, Element>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        OperationProperty = ConfigureProperty<SourceOperation, Element>(nameof(Operation))
            .Accessor(o => o.Operation, null)
            .Register();

        NodeTreeProperty = ConfigureProperty<ElementNodeTreeModel, Element>(nameof(NodeTree))
            .Accessor(o => o.NodeTree, null)
            .Register();

        UseNodeProperty = ConfigureProperty<bool, Element>(nameof(UseNode))
            .Accessor(o => o.UseNode, (o, v) => o.UseNode = v)
            .DefaultValue(false)
            .Register();

        IsEnabledProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Element element)
            {
                element.ForceRender();
            }
        });
        UseNodeProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Element element)
            {
                element.ForceRender();
            }
        });
    }

    public Element()
    {
        Operation = new SourceOperation();
        Operation.Invalidated += (_, _) => ForceRender();

        NodeTree = new ElementNodeTreeModel();
        NodeTree.Invalidated += (_, _) => ForceRender();

        HierarchicalChildren.Add(Operation);
        HierarchicalChildren.Add(NodeTree);
    }

    // 0以上
    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public TimeSpan Start
    {
        get => _start;
        set => SetAndRaise(StartProperty, ref _start, value);
    }

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
    public TimeSpan Length
    {
        get => _length;
        set => SetAndRaise(LengthProperty, ref _length, value);
    }

    public TimeRange Range => new(Start, Length);

    public int ZIndex
    {
        get => _zIndex;
        set => SetAndRaise(ZIndexProperty, ref _zIndex, value);
    }

    public Color AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public SourceOperation Operation { get; }

    public ElementNodeTreeModel NodeTree { get; }

    public bool UseNode
    {
        get => _useNode;
        set => SetAndRaise(UseNodeProperty, ref _useNode, value);
    }

    public IClock Clock => _instanceClock;

    protected override void SaveCore(string filename)
    {
        this.JsonSave2(filename);
    }

    protected override void RestoreCore(string filename)
    {
        this.JsonRestore2(filename);
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);

        if (json.TryGetPropertyValue(nameof(Operation), out JsonNode? operationNode)
            && operationNode is JsonObject operationObj)
        {
            Operation.ReadFromJson(operationObj);
        }

        if (json.TryGetPropertyValue(nameof(NodeTree), out JsonNode? nodeTreeNode)
            && nodeTreeNode is JsonObject nodeTreeObj)
        {
            NodeTree.ReadFromJson(nodeTreeObj);
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        var operationJson = new JsonObject();
        Operation.WriteToJson(operationJson);
        json[nameof(Operation)] = operationJson;

        var nodeTreeJson = new JsonObject();
        NodeTree.WriteToJson(nodeTreeJson);
        json[nameof(NodeTree)] = nodeTreeJson;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Operation), Operation);
        context.SetValue(nameof(NodeTree), NodeTree);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        context.Populate(nameof(Operation), Operation);
        context.Populate(nameof(NodeTree), NodeTree);
    }

    public PooledList<Renderable> Evaluate(EvaluationTarget target, IClock clock, IRenderer renderer)
    {
        lock (this)
        {
            _instanceClock.GlobalClock = clock;
            _instanceClock.BeginTime = Start;
            _instanceClock.DurationTime = Length;
            _instanceClock.CurrentTime = clock.CurrentTime - Start;
            _instanceClock.AudioStartTime = clock.AudioStartTime - Start;
            if (UseNode)
            {
                return NodeTree.Evaluate(target, renderer, this);
            }
            else
            {
                return Operation.Evaluate(target, renderer, this);
            }
        }
    }

    private void ForceRender()
    {
        Scene? scene = this.FindHierarchicalParent<Scene>();
        if (IsEnabled
            && scene != null
            && Start <= scene.CurrentFrame
            && scene.CurrentFrame < Start + Length
            && scene.Renderer is { IsDisposed: false })
        {
            scene.Renderer.RaiseInvalidated(scene.CurrentFrame);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);

        if (args is CorePropertyChangedEventArgs e)
        {
            TimeRange GetOldRange()
            {
                if (e.Property == StartProperty)
                {
                    return new TimeRange(((CorePropertyChangedEventArgs<TimeSpan>)args).OldValue, Length);
                }
                else if (e.Property == LengthProperty)
                {
                    return new TimeRange(Start, ((CorePropertyChangedEventArgs<TimeSpan>)args).OldValue);
                }
                else
                {
                    return default;
                }
            }

            if (e.Property == StartProperty || e.Property == LengthProperty)
            {
                if (IsEnabled)
                {
                    Scene? scene = this.FindHierarchicalParent<Scene>();
                    if (scene is { Renderer.IsDisposed: false } renderer)
                    {
                        TimeRange newRange = Range;
                        TimeRange oldRange = GetOldRange();
                        TimeSpan current = scene.CurrentFrame;

                        if (newRange.Contains(current) || oldRange.Contains(current))
                        {
                            renderer.Renderer.RaiseInvalidated(scene.CurrentFrame);
                        }
                    }
                }
            }
            else if (e.Property == ZIndexProperty)
            {
                ForceRender();
            }
        }
    }

    internal Element? GetBefore(int zindex, TimeSpan start)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? tmp = null;
            foreach (Element? item in scene.Children.GetMarshal().Value)
            {
                if (item != this && item.ZIndex == zindex && item.Start < start)
                {
                    if (tmp == null || tmp.Start <= item.Start)
                    {
                        tmp = item;
                    }
                }
            }
            return tmp;
        }

        return null;
    }

    internal Element? GetAfter(int zindex, TimeSpan end)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? tmp = null;
            foreach (Element? item in scene.Children.GetMarshal().Value)
            {
                if (item != this && item.ZIndex == zindex && item.Range.End > end)
                {
                    if (tmp == null || tmp.Range.End >= item.Range.End)
                    {
                        tmp = item;
                    }
                }
            }
            return tmp;
        }

        return null;
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(int zindex, TimeSpan start, TimeSpan end)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? beforeTmp = null;
            Element? afterTmp = null;
            Element? coverTmp = null;
            var range = TimeRange.FromRange(start, end);

            foreach (Element? item in scene.Children.GetMarshal().Value)
            {
                if (item != this && item.ZIndex == zindex)
                {
                    if (item.Start < start
                        && (beforeTmp == null || beforeTmp.Start <= item.Start))
                    {
                        beforeTmp = item;
                    }

                    if (item.Range.End > end
                        && (afterTmp == null || afterTmp.Range.End >= item.Range.End))
                    {
                        afterTmp = item;
                    }

                    if (range.Contains(item.Range) || range == item.Range)
                    {
                        coverTmp = item;
                    }
                }
            }
            return (beforeTmp, afterTmp, coverTmp);
        }

        return (null, null, null);
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(int zindex, TimeSpan start, Element[] excludes)
    {
        if (HierarchicalParent is Scene scene)
        {
            Element? beforeTmp = null;
            Element? afterTmp = null;
            Element? coverTmp = null;
            var range = new TimeRange(start, Length);

            foreach (Element? item in scene.Children.Except(excludes))
            {
                if (item != this && item.ZIndex == zindex)
                {
                    if (item.Start < start
                        && (beforeTmp == null || beforeTmp.Start <= item.Start))
                    {
                        beforeTmp = item;
                    }

                    if (item.Range.End > range.End
                        && (afterTmp == null || afterTmp.Range.End >= item.Range.End))
                    {
                        afterTmp = item;
                    }

                    if (range.Contains(item.Range) || range == item.Range)
                    {
                        coverTmp = item;
                    }
                }
            }
            return (beforeTmp, afterTmp, coverTmp);
        }

        return (null, null, null);
    }
}
