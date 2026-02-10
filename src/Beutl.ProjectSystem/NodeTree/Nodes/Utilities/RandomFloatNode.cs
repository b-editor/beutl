namespace Beutl.NodeTree.Nodes.Utilities;

public class RandomSingleNode : Node
{
    private readonly OutputSocket<float> _valueSocket;
    private readonly InputSocket<float> _maximumSocket;
    private readonly InputSocket<float> _minimumSocket;

    public RandomSingleNode()
    {
        _valueSocket = AddOutput<float>("Value");
        _maximumSocket = AddInput<float>("Maximum").AcceptNumber();
        _minimumSocket = AddInput<float>("Minimum").AcceptNumber();
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
        _valueSocket = AddOutput<double>("Value");
        _maximumSocket = AddInput<double>("Maximum").AcceptNumber();
        _minimumSocket = AddInput<double>("Minimum").AcceptNumber();
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
        _valueSocket = AddOutput<int>("Value");
        _maximumSocket = AddInput<int>("Maximum").AcceptNumber();
        _minimumSocket = AddInput<int>("Minimum").AcceptNumber();
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
        _valueSocket = AddOutput<long>("Value");
        _maximumSocket = AddInput<long>("Maximum").AcceptNumber();
        _minimumSocket = AddInput<long>("Minimum").AcceptNumber();
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
