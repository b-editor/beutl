﻿namespace Beutl.NodeTree.Nodes.Utilities;

public class RandomSingleNode : Node
{
    private static readonly CoreProperty<float> MaximumProperty
        = ConfigureProperty<float, RandomSingleNode>(o => o.Maximum)
            .DefaultValue(1)
            .Register();
    private static readonly CoreProperty<float> MinimumProperty
        = ConfigureProperty<float, RandomSingleNode>(o => o.Minimum)
            .DefaultValue(0)
            .Register();
    private readonly OutputSocket<float> _valueSocket;
    private readonly InputSocket<float> _maximumSocket;
    private readonly InputSocket<float> _minimumSocket;

    public RandomSingleNode()
    {
        _valueSocket = AsOutput<float>("Value");
        _maximumSocket = AsInput(MaximumProperty).AcceptNumber();
        _minimumSocket = AsInput(MinimumProperty).AcceptNumber();
    }

    private float Maximum { get; set; } = 1;

    private float Minimum { get; set; }

    public override void Evaluate(NodeEvaluationContext context)
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
            .Register();
    private static readonly CoreProperty<double> MinimumProperty
        = ConfigureProperty<double, RandomDoubleNode>(o => o.Minimum)
            .DefaultValue(0)
            .Register();
    private readonly OutputSocket<double> _valueSocket;
    private readonly InputSocket<double> _maximumSocket;
    private readonly InputSocket<double> _minimumSocket;

    public RandomDoubleNode()
    {
        _valueSocket = AsOutput<double>("Value");
        _maximumSocket = AsInput(MaximumProperty).AcceptNumber();
        _minimumSocket = AsInput(MinimumProperty).AcceptNumber();
    }

    private double Maximum { get; set; } = 1;

    private double Minimum { get; set; }

    public override void Evaluate(NodeEvaluationContext context)
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
            .Register();
    private static readonly CoreProperty<int> MinimumProperty
        = ConfigureProperty<int, RandomInt32Node>(o => o.Minimum)
            .DefaultValue(0)
            .Register();
    private readonly OutputSocket<int> _valueSocket;
    private readonly InputSocket<int> _maximumSocket;
    private readonly InputSocket<int> _minimumSocket;

    public RandomInt32Node()
    {
        _valueSocket = AsOutput<int>("Value");
        _maximumSocket = AsInput(MaximumProperty).AcceptNumber();
        _minimumSocket = AsInput(MinimumProperty).AcceptNumber();
    }

    private int Maximum { get; set; } = 100;

    private int Minimum { get; set; }

    public override void Evaluate(NodeEvaluationContext context)
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
            .Register();
    private static readonly CoreProperty<long> MinimumProperty
        = ConfigureProperty<long, RandomInt64Node>(o => o.Minimum)
            .DefaultValue(0)
            .Register();
    private readonly OutputSocket<long> _valueSocket;
    private readonly InputSocket<long> _maximumSocket;
    private readonly InputSocket<long> _minimumSocket;

    public RandomInt64Node()
    {
        _valueSocket = AsOutput<long>("Value");
        _maximumSocket = AsInput(MaximumProperty).AcceptNumber();
        _minimumSocket = AsInput(MinimumProperty).AcceptNumber();
    }

    private long Maximum { get; set; } = 100;

    private long Minimum { get; set; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        long max = _maximumSocket.Value;
        long min = _minimumSocket.Value;
        _valueSocket.Value = Random.Shared.NextInt64(min, max);
    }
}
