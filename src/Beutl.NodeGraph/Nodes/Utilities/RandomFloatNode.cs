using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class RandomSingleNode : GraphNode
{
    public RandomSingleNode()
    {
        Value = AddOutput<float>("Value");
        Maximum = AddInput<float>("Maximum");
        Minimum = AddInput<float>("Minimum");
    }

    public OutputPort<float> Value { get; }

    public InputPort<float> Maximum { get; }

    public InputPort<float> Minimum { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            float value = Random.Shared.NextSingle();
            Value = value * Maximum + (1 - value) * Minimum;
        }
    }
}

public partial class RandomDoubleNode : GraphNode
{
    public RandomDoubleNode()
    {
        Value = AddOutput<double>("Value");
        Maximum = AddInput<double>("Maximum");
        Minimum = AddInput<double>("Minimum");
    }

    public OutputPort<double> Value { get; }

    public InputPort<double> Maximum { get; }

    public InputPort<double> Minimum { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            double value = Random.Shared.NextDouble();
            Value = value * Maximum + (1 - value) * Minimum;
        }
    }
}

public partial class RandomInt32Node : GraphNode
{
    public RandomInt32Node()
    {
        Value = AddOutput<int>("Value");
        Maximum = AddInput<int>("Maximum");
        Minimum = AddInput<int>("Minimum");
    }

    public OutputPort<int> Value { get; }

    public InputPort<int> Maximum { get; }

    public InputPort<int> Minimum { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = Maximum <= Minimum ? Random.Shared.Next(Maximum, Minimum) : Random.Shared.Next(Minimum, Maximum);
        }
    }
}

public partial class RandomInt64Node : GraphNode
{
    public RandomInt64Node()
    {
        Value = AddOutput<long>("Value");
        Maximum = AddInput<long>("Maximum");
        Minimum = AddInput<long>("Minimum");
    }

    public OutputPort<long> Value { get; }

    public InputPort<long> Maximum { get; }

    public InputPort<long> Minimum { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = Maximum <= Minimum ? Random.Shared.NextInt64(Maximum, Minimum) : Random.Shared.NextInt64(Minimum, Maximum);
        }
    }
}
