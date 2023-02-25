namespace Beutl.NodeTree;

public sealed class Connection
{
    public Connection(IInputSocket input, IOutputSocket output)
    {
        Input = input;
        Output = output;
    }

    public IInputSocket Input { get; }

    public IOutputSocket Output { get; }
}
