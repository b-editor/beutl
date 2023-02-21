namespace Beutl.NodeTree;

internal interface IInputSocketForSetter : IInputSocket
{
    new int LocalId { get; set; }

    void SetProperty(object property);
}
