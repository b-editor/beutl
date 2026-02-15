using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class RandomSingleNode : Node
{
    public RandomSingleNode()
    {
        Value = AddOutput<float>("Value");
        Maximum = AddInput<float>("Maximum");
        Minimum = AddInput<float>("Minimum");
    }

    public OutputSocket<float> Value { get; }

    public InputSocket<float> Maximum { get; }

    public InputSocket<float> Minimum { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            float value = Random.Shared.NextSingle();
            Value = value * Maximum + (1 - value) * Minimum;
        }
    }
}

public partial class RandomDoubleNode : Node
{
    public RandomDoubleNode()
    {
        Value = AddOutput<double>("Value");
        Maximum = AddInput<double>("Maximum");
        Minimum = AddInput<double>("Minimum");
    }

    public OutputSocket<double> Value { get; }

    public InputSocket<double> Maximum { get; }

    public InputSocket<double> Minimum { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            double value = Random.Shared.NextDouble();
            Value = value * Maximum + (1 - value) * Minimum;
        }
    }
}

public partial class RandomInt32Node : Node
{
    public RandomInt32Node()
    {
        Value = AddOutput<int>("Value");
        Maximum = AddInput<int>("Maximum");
        Minimum = AddInput<int>("Minimum");
    }

    public OutputSocket<int> Value { get; }

    public InputSocket<int> Maximum { get; }

    public InputSocket<int> Minimum { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = Maximum <= Minimum ? Random.Shared.Next(Maximum, Minimum) : Random.Shared.Next(Minimum, Maximum);
        }
    }
}

public partial class RandomInt64Node : Node
{
    public RandomInt64Node()
    {
        Value = AddOutput<long>("Value");
        Maximum = AddInput<long>("Maximum");
        Minimum = AddInput<long>("Minimum");
    }

    public OutputSocket<long> Value { get; }

    public InputSocket<long> Maximum { get; }

    public InputSocket<long> Minimum { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = Maximum <= Minimum ? Random.Shared.NextInt64(Maximum, Minimum) : Random.Shared.NextInt64(Minimum, Maximum);
        }
    }
}
