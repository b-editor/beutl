namespace Beutl.NodeTree.Nodes.Utilities;

public class RandomSingleNode : Node
{
    private readonly OutputSocket<float> _valueSocket;
    private readonly InputSocket<float> _maximumSocket;
    private readonly InputSocket<float> _minimumSocket;

    public RandomSingleNode()
    {
        _valueSocket = AsOutput<float>("Value");
        _maximumSocket = AsInput<float>("Maximum").AcceptNumber();
        _minimumSocket = AsInput<float>("Minimum").AcceptNumber();
    }

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
    private readonly OutputSocket<double> _valueSocket;
    private readonly InputSocket<double> _maximumSocket;
    private readonly InputSocket<double> _minimumSocket;

    public RandomDoubleNode()
    {
        _valueSocket = AsOutput<double>("Value");
        _maximumSocket = AsInput<double>("Maximum").AcceptNumber();
        _minimumSocket = AsInput<double>("Minimum").AcceptNumber();
    }

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
    private readonly OutputSocket<int> _valueSocket;
    private readonly InputSocket<int> _maximumSocket;
    private readonly InputSocket<int> _minimumSocket;

    public RandomInt32Node()
    {
        _valueSocket = AsOutput<int>("Value");
        _maximumSocket = AsInput<int>("Maximum").AcceptNumber();
        _minimumSocket = AsInput<int>("Minimum").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        int max = _maximumSocket.Value;
        int min = _minimumSocket.Value;
        if (max <= min)
        {
            _valueSocket.Value = Random.Shared.Next(max, min);
        }
        else
        {
            _valueSocket.Value = Random.Shared.Next(min, max);
        }
    }
}

public class RandomInt64Node : Node
{
    private readonly OutputSocket<long> _valueSocket;
    private readonly InputSocket<long> _maximumSocket;
    private readonly InputSocket<long> _minimumSocket;

    public RandomInt64Node()
    {
        _valueSocket = AsOutput<long>("Value");
        _maximumSocket = AsInput<long>("Maximum").AcceptNumber();
        _minimumSocket = AsInput<long>("Minimum").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        long max = _maximumSocket.Value;
        long min = _minimumSocket.Value;
        if (max <= min)
        {
            _valueSocket.Value = Random.Shared.NextInt64(max, min);
            return;
        }
        _valueSocket.Value = Random.Shared.NextInt64(min, max);
    }
}
