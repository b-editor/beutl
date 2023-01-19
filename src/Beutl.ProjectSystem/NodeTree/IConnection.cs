namespace Beutl.NodeTree;

public interface IConnection
{
    IInputSocket Input { get; }

    IOutputSocket Output { get; }
}

public sealed class Connection : IConnection
{
    public Connection(IInputSocket input, IOutputSocket output)
    {
        Input = input;
        Output = output;
    }

    public IInputSocket Input { get; }
    
    public IOutputSocket Output { get; }
}
