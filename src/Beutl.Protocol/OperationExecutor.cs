namespace Beutl.Protocol;

public class OperationExecutor
{
    public async ValueTask Execute(OperationBase operation)
    {
        operation.Execute(new OperationContext(BeutlApplication.Current));
    }
}
