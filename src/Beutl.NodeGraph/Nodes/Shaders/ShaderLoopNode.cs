using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Beutl.Editor;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>Which iteration of a loop is being evaluated.</summary>
/// <param name="Time">Seconds on the node's own clock, like <see cref="Utilities.TimeNode"/>.</param>
internal readonly record struct ShaderLoopStep(int Index, int Count, double Time, double Delta);

/// <summary>A group that runs repeatedly: each state output feeds the state input of the same position.</summary>
/// <remarks>Evaluating the group only builds wire values, so the loop needs no recording of its own. What
/// its outputs carry is recorded when something asks for them, through one value per iteration.</remarks>
public abstract partial class ShaderLoopNode : GroupNode, IShaderPictureOwner
{
    private ShaderErrorReports? _errors;

    protected ShaderLoopNode()
    {
        var input = new ShaderLoopInput();
        var output = new ShaderLoopOutput();
        var info = new ShaderLoopInfoNode();
        var state = new GroupInput.GroupInputPort<ShaderGraphValue<ShaderImage>>
        {
            Name = { Value = "Image" },
            Display = { Value = ShaderPortDisplays.Get("Image") }
        };
        var next = new GroupOutput.GroupOutputPort<ShaderGraphValue<ShaderImage>>
        {
            Name = { Value = "Image" },
            Display = { Value = ShaderPortDisplays.Get("Image") }
        };
        input.Items.Add(state);
        output.Items.Add(next);
        Group.Value.Nodes.Add(input);
        Group.Value.Nodes.Add(output);
        Group.Value.Nodes.Add(info);
        input.Position.Value = (20, 20);
        info.Position.Value = (20, 200);
        output.Position.Value = (360, 20);
        MirrorBoundaryPorts();
    }

    public NodeMonitor<string?> Error { get; private protected set; } = null!;

    private ShaderErrorReports Errors => _errors ??= new ShaderErrorReports(Error);

    void IShaderPictureOwner.ReportPictureFailure(string? message) => Errors.ReportPicture(message);

    public override void Deserialize(ICoreSerializationContext context)
    {
        // What the constructor mirrored belongs to the group the save is about to replace.
        int mirrored = Items.Count - FixedPortCount;
        if (mirrored > 0) Items.RemoveRange(0, mirrored);
        base.Deserialize(context);
    }

    public partial class Resource
    {
        // Far past what a frame can render; the check exists so a mistyped time reports instead of hanging.
        private const int MaximumSteps = 100_000;

        /// <summary>The iterations to evaluate, or null with a message for a request that cannot run.</summary>
        private protected virtual ShaderLoopStep[]? GetSteps(GraphCompositionContext context, out string? error)
        {
            error = null;
            return [];
        }

        private protected static string TooManySteps(long count)
            => string.Format(NodeGraphStrings.ShaderError_TooManySteps, count, MaximumSteps);

        private protected static bool IsTooMany(long count) => count > MaximumSteps;

        public override void Update(GraphCompositionContext context)
        {
            ShaderLoopNode node = RequireOriginal();
            if (InnerSnapshot is not { } snapshot) return;

            GraphGroup group = node.Group.Value;
            snapshot.Build(group, context);
            GroupInput? inputNode = group.Input.Value;
            GroupOutput? outputNode = group.Output.Value;
            int inputCount = inputNode?.Items.Count ?? 0;
            int outputCount = outputNode?.Items.Count ?? 0;

            // The items hold the outputs, then the inputs that mirror the group's boundary.
            var states = new IItemValue[inputCount];
            for (int i = 0; i < inputCount; i++)
            {
                states[i] = inputNode!.Items[i].CreateItemValue();
                states[i].PropagateFrom(ItemValues[outputCount + i]);
            }

            ShaderLoopStep[]? steps = GetSteps(context, out string? error);
            node.Errors.ReportOwn(error);
            try
            {
                var ordered = new List<IShaderGraphValue>();
                if (steps is { Length: > 0 })
                {
                    int inputSlot = snapshot.FindSlotIndex(inputNode);
                    int outputSlot = snapshot.FindSlotIndex(outputNode);
                    var inputResource = snapshot.GetResource(inputSlot) as GroupInput.Resource;
                    inputResource?.OuterInputValues = states;
                    var stepContext = new GraphCompositionContext(context.Time)
                    {
                        DisableResourceShare = context.DisableResourceShare,
                        Conditions = context.Conditions
                    };
                    foreach (ShaderLoopStep step in steps)
                    {
                        stepContext.Time = StepTime(node, context, step);
                        stepContext.LoopStep = step;
                        snapshot.Evaluate(stepContext);
                        for (int i = 0; i < Math.Min(inputCount, outputCount); i++)
                        {
                            // A state nothing writes keeps its value.
                            if (snapshot.HasInputConnection(outputSlot, i)
                                && snapshot.GetItemValue(outputSlot, i) is { } next)
                                states[i].PropagateFrom(next);
                        }

                        foreach (IItemValue state in states)
                        {
                            if (state.GetBoxed() is IShaderGraphValue value)
                                ordered.Add(value);
                        }
                    }

                    for (int i = Math.Min(inputCount, outputCount); i < outputCount; i++)
                    {
                        if (snapshot.GetItemValue(outputSlot, i) is { } last)
                            ItemValues[i].PropagateFrom(last);
                    }
                }

                // Recording state by state, in iteration order, keeps the depth of the call
                // stack to one iteration; a long replay would otherwise overflow it.
                for (int i = 0; i < Math.Min(inputCount, outputCount); i++)
                {
                    ItemValues[i].PropagateFrom(states[i].GetBoxed() is IShaderGraphValue value && ordered.Count > 0
                        ? (IItemValue)new ItemValue<object?> { Value = value.After(ordered) }
                        : states[i]);
                }
            }
            finally
            {
                foreach (IItemValue state in states)
                    state.Dispose();
            }
        }

        private protected virtual MediaTime StepTime(ShaderLoopNode node, GraphCompositionContext context, ShaderLoopStep step)
            => context.Time;
    }
}

/// <summary>Records its contents the given number of times.</summary>
public partial class ShaderRepeatNode : ShaderLoopNode
{
    public ShaderRepeatNode()
    {
        Count = AddInput<int>("Count", ShaderPortDisplays.Get("Count"));
        Count.Property?.SetValue(1);
        Error = AddTextMonitor("Error", NodePortDisplays.Error);
    }

    public InputPort<int> Count { get; }

    private protected override int FixedPortCount => 2;

    public partial class Resource
    {
        private protected override ShaderLoopStep[]? GetSteps(GraphCompositionContext context, out string? error)
        {
            int count = Math.Max(Count, 0);
            if (IsTooMany(count))
            {
                error = TooManySteps(count);
                return null;
            }

            error = null;
            var steps = new ShaderLoopStep[count];
            double time = (context.Time - RequireOriginal().Start).ToSeconds();
            for (int i = 0; i < count; i++)
                steps[i] = new(i, count, time, 0);
            return steps;
        }
    }
}

/// <summary>Rebuilds a simulation from its start for the requested time, in fixed steps, so a seek gives the same picture as playing.</summary>
public partial class ShaderReplayNode : ShaderLoopNode
{
    public ShaderReplayNode()
    {
        Time = AddInput<object?>("Time", NodePortDisplays.Time);
        StepsPerSecond = AddInput<double>("StepsPerSecond", ShaderPortDisplays.Get("StepsPerSecond"));
        StepsPerSecond.Property?.SetValue(60d);
        ResetTime = AddInput<double>("ResetTime", ShaderPortDisplays.Get("ResetTime"));
        Error = AddTextMonitor("Error", NodePortDisplays.Error);
    }

    /// <summary>The time to reach, in seconds. It takes a connection only: left alone, it is the time being rendered.</summary>
    public InputPort<object?> Time { get; }

    public InputPort<double> StepsPerSecond { get; }

    public InputPort<double> ResetTime { get; }

    private protected override int FixedPortCount => 4;

    public partial class Resource
    {
        // Past this a TimeSpan runs out of range, well before any loop could finish.
        private const double MaximumSeconds = 100_000_000;

        private protected override ShaderLoopStep[]? GetSteps(GraphCompositionContext context, out string? error)
        {
            double target;
            try
            {
                target = Time is IConvertible connected
                    ? connected.ToDouble(CultureInfo.InvariantCulture)
                    : (context.Time - RequireOriginal().Start).ToSeconds();
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                error = NodeGraphStrings.ShaderError_InvalidReplayTime;
                return null;
            }

            if (!(StepsPerSecond > 0) || !double.IsFinite(StepsPerSecond) || !double.IsFinite(target) || !double.IsFinite(ResetTime)
                || Math.Abs(target) > MaximumSeconds || Math.Abs(ResetTime) > MaximumSeconds || 1 / StepsPerSecond > MaximumSeconds)
            {
                error = NodeGraphStrings.ShaderError_InvalidReplayTime;
                return null;
            }

            TimeSpan time = TimeSpan.FromSeconds(target);
            TimeSpan step = TimeSpan.FromSeconds(1 / StepsPerSecond);
            TimeSpan reset = TimeSpan.FromSeconds(ResetTime);
            if (step.Ticks <= 0)
            {
                error = NodeGraphStrings.ShaderError_InvalidReplayTime;
                return null;
            }

            long ticks = time > reset ? (time - reset).Ticks : 0;
            long count = ticks / step.Ticks + (ticks % step.Ticks == 0 ? 0 : 1);
            if (IsTooMany(count))
            {
                error = TooManySteps(count);
                return null;
            }

            error = null;
            var steps = new ShaderLoopStep[count];
            foreach (ShaderTimeStep s in ShaderGraphBuilder.ReplaySteps(time, step, reset))
                steps[s.Index] = new(s.Index, steps.Length, s.Time.TotalSeconds, s.Delta.TotalSeconds);
            return steps;
        }

        // Inner animations read the time the step stands for, not the frame being rendered.
        private protected override MediaTime StepTime(ShaderLoopNode node, GraphCompositionContext context, ShaderLoopStep step)
            => node.Start + MediaTime.FromSeconds(step.Time);
    }
}

/// <summary>Where a loop's state comes in. A new state also gets its way out, and keeps its value until that is connected.</summary>
public partial class ShaderLoopInput : GroupInput
{
    public ShaderLoopInput()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    public override bool AddNodePort(INodePort port, [NotNullWhen(true)] out Connection? connection)
    {
        if (!base.AddNodePort(port, out connection)) return false;
        if (this.FindHierarchicalParent<GraphGroup>()?.Output.Value is ShaderLoopOutput output
            && Items[^1] is IOutputPort state)
            output.AddPartner(state);
        return true;
    }

    /// <summary>Adds the state that <paramref name="next"/> writes.</summary>
    internal void AddPartner(IInputPort next)
    {
        Type type = typeof(GroupInputPort<>).MakeGenericType(next.AssociatedType!);
        var state = (IOutputPort)Activator.CreateInstance(type)!;
        state.Name.Value = next.Name.Value;
        ((NodeMember)state).Display.Value = next.Display.Value;
        Items.Add(state);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove && !HistoryReplay.IsActive
            && this.FindHierarchicalParent<GraphGroup>() is { Output.Value: ShaderLoopOutput output } group)
            ShaderLoopBoundary.RemovePartner(group, output, Items.Count, e.OldStartingIndex);
    }
}

/// <summary>Where a loop's state goes out, to the next iteration and finally to the loop's outputs.</summary>
public partial class ShaderLoopOutput : GroupOutput
{
    public ShaderLoopOutput()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    public override bool AddNodePort(INodePort port, [NotNullWhen(true)] out Connection? connection)
    {
        if (!base.AddNodePort(port, out connection)) return false;
        if (this.FindHierarchicalParent<GraphGroup>()?.Input.Value is ShaderLoopInput input
            && Items[^1] is IInputPort next)
            input.AddPartner(next);
        return true;
    }

    /// <summary>Adds the way out of <paramref name="state"/>.</summary>
    internal void AddPartner(IOutputPort state)
    {
        Type type = typeof(GroupOutputPort<>).MakeGenericType(state.AssociatedType!);
        var next = (IInputPort)Activator.CreateInstance(type)!;
        next.Name.Value = state.Name.Value;
        ((NodeMember)next).Display.Value = state.Display.Value;
        Items.Add(next);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove && !HistoryReplay.IsActive
            && this.FindHierarchicalParent<GraphGroup>() is { Input.Value: ShaderLoopInput input } group)
            ShaderLoopBoundary.RemovePartner(group, input, Items.Count, e.OldStartingIndex);
    }
}

internal static class ShaderLoopBoundary
{
    // The partner holds one more port than the node that lost one, unless it is the partner following.
    public static void RemovePartner(GraphGroup group, GraphNode partner, int remaining, int index)
    {
        if (partner.Items.Count != remaining + 1 || index >= partner.Items.Count) return;
        INodeMember port = partner.Items[index];
        group.DisconnectPort(port);
        partner.Items.Remove(port);
    }
}

/// <summary>The iteration being evaluated: its number and, in a replay, the time it stands for.</summary>
public partial class ShaderLoopInfoNode : GraphNode
{
    public ShaderLoopInfoNode()
    {
        Index = AddOutput<int>("Index", ShaderPortDisplays.Get("Index"));
        Count = AddOutput<int>("Count", ShaderPortDisplays.Get("Count"));
        Time = AddOutput<float>("Time", NodePortDisplays.Time);
        Delta = AddOutput<float>("Delta", ShaderPortDisplays.Get("Delta"));
    }

    public OutputPort<int> Index { get; }

    public OutputPort<int> Count { get; }

    public OutputPort<float> Time { get; }

    public OutputPort<float> Delta { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            ShaderLoopStep step = context.LoopStep ?? default;
            Index = step.Index;
            Count = step.Count;
            Time = (float)step.Time;
            Delta = (float)step.Delta;
        }
    }
}
