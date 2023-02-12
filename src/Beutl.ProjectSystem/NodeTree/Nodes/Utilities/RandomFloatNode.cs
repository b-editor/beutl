namespace Beutl.NodeTree.Nodes.Utilities;

public class RandomSingleNode : Node
{
    private static readonly CoreProperty<float> MaximumProperty
        = ConfigureProperty<float, RandomSingleNode>(o => o.Maximum)
            .DefaultValue(1)
            .SerializeName("maximum")
            .Register();
    private static readonly CoreProperty<float> MinimumProperty
        = ConfigureProperty<float, RandomSingleNode>(o => o.Minimum)
            .DefaultValue(0)
            .SerializeName("minimum")
            .Register();
    private readonly OutputSocket<float> _valueSocket;
    private readonly InputSocket<float> _maximumSocket;
    private readonly InputSocket<float> _minimumSocket;

    public RandomSingleNode()
    {
        _valueSocket = AsOutput<float>("Output", "Value");
        _maximumSocket = AsInput(MaximumProperty);
        _minimumSocket = AsInput(MinimumProperty);
    }

    private float Maximum { get; set; } = 1;

    private float Minimum { get; set; }

    public override void Evaluate(EvaluationContext context)
    {
        float value = Random.Shared.NextSingle();
        float max = _maximumSocket.Value;
        float min = _minimumSocket.Value;

        _valueSocket.Value = value * min + (1 - value) * max;
    }
}

public class RandomDoubleNode : Node
{
    private static readonly CoreProperty<double> MaximumProperty
        = ConfigureProperty<double, RandomDoubleNode>(o => o.Maximum)
            .DefaultValue(1)
            .SerializeName("maximum")
            .Register();
    private static readonly CoreProperty<double> MinimumProperty
        = ConfigureProperty<double, RandomDoubleNode>(o => o.Minimum)
            .DefaultValue(0)
            .SerializeName("minimum")
            .Register();
    private OutputSocket<double> _valueSocket;
    private InputSocket<double> _maximumSocket;
    private InputSocket<double> _minimumSocket;

    public RandomDoubleNode()
    {
        _valueSocket = AsOutput<double>("Output", "Value");
        _maximumSocket = AsInput(MaximumProperty);
        _minimumSocket = AsInput(MinimumProperty);
    }

    private float Maximum { get; set; } = 1;

    private float Minimum { get; set; }

    public override void Evaluate(EvaluationContext context)
    {
        double value = Random.Shared.NextDouble();
        double max = _maximumSocket.Value;
        double min = _minimumSocket.Value;

        _valueSocket.Value = value * min + (1 - value) * max;
    }
}

public class RandomInt32Node : Node
{
    private static readonly CoreProperty<int> MaximumProperty
        = ConfigureProperty<int, RandomInt32Node>(o => o.Maximum)
            .DefaultValue(100)
            .SerializeName("maximum")
            .Register();
    private static readonly CoreProperty<int> MinimumProperty
        = ConfigureProperty<int, RandomInt32Node>(o => o.Minimum)
            .DefaultValue(0)
            .SerializeName("minimum")
            .Register();
    private OutputSocket<int> _valueSocket;
    private InputSocket<int> _maximumSocket;
    private InputSocket<int> _minimumSocket;

    public RandomInt32Node()
    {
        _valueSocket = AsOutput<int>("Output", "Value");
        _maximumSocket = AsInput(MaximumProperty);
        _minimumSocket = AsInput(MinimumProperty);
    }

    private int Maximum { get; set; } = 1;

    private int Minimum { get; set; }

    public override void Evaluate(EvaluationContext context)
    {
        int max = _maximumSocket.Value;
        int min = _minimumSocket.Value;
        _valueSocket.Value = Random.Shared.Next(min, max);
    }
}

public class RandomInt64Node : Node
{
    private static readonly CoreProperty<long> MaximumProperty
        = ConfigureProperty<long, RandomInt64Node>(o => o.Maximum)
            .DefaultValue(100)
            .SerializeName("maximum")
            .Register();
    private static readonly CoreProperty<long> MinimumProperty
        = ConfigureProperty<long, RandomInt64Node>(o => o.Minimum)
            .DefaultValue(0)
            .SerializeName("minimum")
            .Register();
    private OutputSocket<long> _valueSocket;
    private InputSocket<long> _maximumSocket;
    private InputSocket<long> _minimumSocket;

    public RandomInt64Node()
    {
        _valueSocket = AsOutput<long>("Output", "Value");
        _maximumSocket = AsInput(MaximumProperty);
        _minimumSocket = AsInput(MinimumProperty);
    }

    private long Maximum { get; set; } = 1;

    private long Minimum { get; set; }

    public override void Evaluate(EvaluationContext context)
    {
        long max = _maximumSocket.Value;
        long min = _minimumSocket.Value;
        _valueSocket.Value = Random.Shared.NextInt64(min, max);
    }
}
