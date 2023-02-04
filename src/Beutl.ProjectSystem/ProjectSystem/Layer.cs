using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json.Nodes;

using Beutl.Language;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Rendering;

namespace Beutl.ProjectSystem;

public class Layer : Element, IStorable, ILogicalElement
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> LengthProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<Color> AccentColorProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<bool> AllowOutflowProperty;
    public static readonly CoreProperty<RenderLayerSpan> SpanProperty;
    public static readonly CoreProperty<SourceOperators> OperatorsProperty;
    private TimeSpan _start;
    private TimeSpan _length;
    private int _zIndex;
    private string? _fileName;
    private bool _isEnabled = true;
    private bool _allowOutflow = false;
    private EventHandler? _saved;
    private EventHandler? _restored;
    private IDisposable? _disposable;

    static Layer()
    {
        StartProperty = ConfigureProperty<TimeSpan, Layer>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Display(Strings.StartTime)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("start")
            .Register();

        LengthProperty = ConfigureProperty<TimeSpan, Layer>(nameof(Length))
            .Accessor(o => o.Length, (o, v) => o.Length = v)
            .Display(Strings.DurationTime)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("length")
            .Register();

        ZIndexProperty = ConfigureProperty<int, Layer>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("zIndex")
            .Register();

        AccentColorProperty = ConfigureProperty<Color, Layer>(nameof(AccentColor))
            .DefaultValue(Colors.Teal)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("accentColor")
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, Layer>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("isEnabled")
            .Register();

        AllowOutflowProperty = ConfigureProperty<bool, Layer>(nameof(AllowOutflow))
            .Accessor(o => o.AllowOutflow, (o, v) => o.AllowOutflow = v)
            .DefaultValue(false)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("allowOutflow")
            .Register();

        SpanProperty = ConfigureProperty<RenderLayerSpan, Layer>(nameof(Span))
            .Accessor(o => o.Span, null)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        OperatorsProperty = ConfigureProperty<SourceOperators, Layer>(nameof(Operators))
            .Accessor(o => o.Operators, null)
            .Register();

        NameProperty.OverrideMetadata<Layer>(new CorePropertyMetadata<string>("name"));

        ZIndexProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Layer layer && layer.Parent is Scene { Renderer: { IsDisposed: false } renderer })
            {
                renderer[args.OldValue]?.RemoveSpan(layer.Span);
                if (args.NewValue >= 0)
                {
                    IRenderLayer? context = renderer[args.NewValue];
                    if (context == null)
                    {
                        context = new RenderLayer();
                        renderer[args.NewValue] = context;
                    }
                    context.AddSpan(layer.Span);
                }
            }
        });

        //RenderableProperty.Changed.Subscribe(args =>
        //{
        //    if (args.Sender is Layer layer && layer.Parent is Scene { Renderer: { IsDisposed: false } renderer } && layer.ZIndex >= 0)
        //    {
        //        renderer[layer.ZIndex] = args.NewValue;
        //    }
        //});

        IsEnabledProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Layer layer)
            {
                layer.ForceRender();
            }
        });
        AllowOutflowProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is Layer layer)
            {
                layer.ForceRender();
            }
        });

        StartProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is Layer layer)
            {
                layer.Span.Start = e.NewValue;
            }
        });

        LengthProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is Layer layer)
            {
                layer.Span.Duration = e.NewValue;
            }
        });
    }

    public Layer()
    {
        Operators = new SourceOperators(this);
        Operators.Attached += item => item.Invalidated += Operator_Invalidated;
        Operators.Detached += item => item.Invalidated -= Operator_Invalidated;
        Operators.CollectionChanged += OnOperatorsCollectionChanged;

        (Span as ILogicalElement).NotifyAttachedToLogicalTree(new(this));
    }

    private void OnOperatorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateName();
    }

    [Conditional("DEBUG")]
    private void UpdateName()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Operators.Count; i++)
        {
            SourceOperator op = Operators[i];
            if (op.IsEnabled)
            {
                Type type = op.GetType();
                string name = OperatorRegistry.FindItem(type)?.DisplayName ?? type.Name;

                sb.Append($"{name}, ");
            }
        }

        Name = sb.ToString();
    }

    event EventHandler IStorable.Saved
    {
        add => _saved += value;
        remove => _saved -= value;
    }

    event EventHandler IStorable.Restored
    {
        add => _restored += value;
        remove => _restored -= value;
    }

    // 0以上
    public TimeSpan Start
    {
        get => _start;
        set => SetAndRaise(StartProperty, ref _start, value);
    }

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

    public bool AllowOutflow
    {
        get => _allowOutflow;
        set => SetAndRaise(AllowOutflowProperty, ref _allowOutflow, value);
    }

    public RenderLayerSpan Span { get; } = new();

    [Obsolete("Use 'Layer.Span'.")]
    public RenderLayerSpan Node => Span;

    public string FileName
    {
        get => _fileName ?? throw new Exception("The file name is not set.");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fileName = value;
        }
    }

    public DateTime LastSavedTime { get; private set; }

    public SourceOperators Operators { get; }

    public void Save(string filename)
    {
        _fileName = filename;
        LastSavedTime = DateTime.UtcNow;
        this.JsonSave(filename);
        File.SetLastWriteTimeUtc(filename, LastSavedTime);

        _saved?.Invoke(this, EventArgs.Empty);
    }

    public void Restore(string filename)
    {
        _fileName = filename;

        this.JsonRestore(filename);
        LastSavedTime = File.GetLastWriteTimeUtc(filename);

        _restored?.Invoke(this, EventArgs.Empty);
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);

        if (json is JsonObject jobject)
        {
            // NOTE: リリース時に削除。互換性を保つためのコードなので
            if (!jobject.ContainsKey("zIndex") && jobject.TryGetPropertyValue("layer", out JsonNode? layerNode)
                && layerNode is JsonValue layerValue
                && layerValue.TryGetValue(out int layer))
            {
                ZIndex = layer;
            }

            //if (jobject.TryGetPropertyValue("renderable", out JsonNode? renderableNode)
            //    && renderableNode is JsonObject renderableObj
            //    && renderableObj.TryGetPropertyValue("@type", out JsonNode? renderableTypeNode)
            //    && renderableTypeNode is JsonValue renderableTypeValue
            //    && renderableTypeValue.TryGetValue(out string? renderableTypeStr)
            //    && TypeFormat.ToType(renderableTypeStr) is Type renderableType
            //    && renderableType.IsAssignableTo(typeof(Renderable))
            //    && Activator.CreateInstance(renderableType) is Renderable renderable)
            //{
            //    renderable.ReadFromJson(renderableObj);
            //    Span.Value = renderable;
            //}

            if (jobject.TryGetPropertyValue("operators", out JsonNode? operatorsNode)
                && operatorsNode is JsonArray operatorsArray)
            {
                foreach (JsonObject operatorJson in operatorsArray.OfType<JsonObject>())
                {
                    if (operatorJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType))
                    {
                        var type = TypeFormat.ToType(atType);
                        SourceOperator? @operator = null;

                        if (type?.IsAssignableTo(typeof(SourceOperator)) ?? false)
                        {
                            @operator = Activator.CreateInstance(type) as SourceOperator;
                        }

                        @operator ??= new SourceOperator();
                        @operator.ReadFromJson(operatorJson);
                        Operators.Add(@operator);
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);

        if (json is JsonObject jobject)
        {
            //if (Span.Value is Renderable renderable)
            //{
            //    JsonNode node = new JsonObject();
            //    renderable.WriteToJson(ref node);
            //    node["@type"] = TypeFormat.ToString(renderable.GetType());
            //    jobject["renderable"] = node;
            //}

            Span<SourceOperator> operators = Operators.GetMarshal().Value;
            if (operators.Length > 0)
            {
                var array = new JsonArray();

                foreach (SourceOperator item in operators)
                {
                    JsonNode node = new JsonObject();
                    item.WriteToJson(ref node);
                    node["@type"] = TypeFormat.ToString(item.GetType());

                    array.Add(node);
                }

                jobject["operators"] = array;
            }
        }
    }

    public IRecordableCommand AddChild(SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        return Operators.BeginRecord<SourceOperator>()
            .Add(@operator)
            .ToCommand();
    }

    public IRecordableCommand RemoveChild(SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        return Operators.BeginRecord<SourceOperator>()
            .Remove(@operator)
            .ToCommand();
    }

    public IRecordableCommand InsertChild(int index, SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        return Operators.BeginRecord<SourceOperator>()
            .Insert(index, @operator)
            .ToCommand();
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        if (args.Parent is Scene { Renderer: { IsDisposed: false } renderer } && ZIndex >= 0)
        {
            IRenderLayer? context = renderer[ZIndex];
            if (context == null)
            {
                context = new RenderLayer();
                renderer[ZIndex] = context;
            }
            context.AddSpan(Span);

            _disposable = SubscribeToLayerNode();
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (args.Parent is Scene { Renderer: { IsDisposed: false } renderer } && ZIndex >= 0)
        {
            renderer[ZIndex]?.RemoveSpan(Span);
            _disposable?.Dispose();
            _disposable = null;
        }
    }

    protected override IEnumerable<ILogicalElement> OnEnumerateChildren()
    {
        foreach (ILogicalElement item in base.OnEnumerateChildren())
        {
            yield return item;
        }

        foreach (SourceOperator item in Operators)
        {
            yield return item;
        }

        if (Span != null)
        {
            yield return Span;
        }
    }

    internal bool InRange(TimeSpan ts)
    {
        return Start <= ts && ts < Length + Start;
    }

    private void ForceRender()
    {
        Scene? scene = this.FindLogicalParent<Scene>();
        if (IsEnabled
            && scene != null
            && Start <= scene.CurrentFrame
            && scene.CurrentFrame < Start + Length
            && scene.Renderer is { IsDisposed: false, IsGraphicsRendering: false })
        {
            scene.Renderer.Invalidate(scene.CurrentFrame);
        }
    }

    private IDisposable SubscribeToLayerNode()
    {
        return Span.GetObservable(RenderLayerSpan.ValueProperty)
            .SelectMany(value => value != null
                ? Observable.FromEventPattern<RenderInvalidatedEventArgs>(h => value.Invalidated += h, h => value.Invalidated -= h)
                    .Select(_ => Unit.Default)
                    .Publish(Unit.Default)
                    .RefCount()
                : Observable.Return(Unit.Default))
            .Subscribe(_ => ForceRender());
    }

    private void Operator_Invalidated(object? sender, EventArgs e)
    {
        ForceRender();
    }

    internal Layer? GetBefore(int zindex, TimeSpan start)
    {
        if (Parent is Scene scene)
        {
            Layer? tmp = null;
            foreach (Layer? item in scene.Children.GetMarshal().Value)
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

    internal Layer? GetAfter(int zindex, TimeSpan end)
    {
        if (Parent is Scene scene)
        {
            Layer? tmp = null;
            foreach (Layer? item in scene.Children.GetMarshal().Value)
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

    internal (Layer? Before, Layer? After, Layer? Cover) GetBeforeAndAfterAndCover(int zindex, TimeSpan start, TimeSpan end)
    {
        if (Parent is Scene scene)
        {
            Layer? beforeTmp = null;
            Layer? afterTmp = null;
            Layer? coverTmp = null;
            var range = new TimeRange(start, end - start);

            foreach (Layer? item in scene.Children.GetMarshal().Value)
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

                    if (range.Contains(item.Range))
                    {
                        coverTmp = item;
                    }
                }
            }
            return (beforeTmp, afterTmp, coverTmp);
        }

        return (null, null, null);
    }

    internal (Layer? Before, Layer? After, Layer? Cover) GetBeforeAndAfterAndCover(int zindex, TimeSpan start, Layer[] excludes)
    {
        if (Parent is Scene scene)
        {
            Layer? beforeTmp = null;
            Layer? afterTmp = null;
            Layer? coverTmp = null;
            var range = new TimeRange(start, Length);

            foreach (Layer? item in scene.Children.Except(excludes))
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

                    if (range.Contains(item.Range))
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
