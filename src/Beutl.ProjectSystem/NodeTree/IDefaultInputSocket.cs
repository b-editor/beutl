namespace Beutl.NodeTree;

internal interface IDefaultInputSocket : IInputSocket
{
    new int LocalId { get; set; }

    void SetPropertyAdapter(object property);
}
