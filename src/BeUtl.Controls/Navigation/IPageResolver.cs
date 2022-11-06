namespace Beutl.Controls.Navigation;

public interface IPageResolver
{
    int GetOrder(Type pagetype);
    
    int GetDepth(Type pagetype);

    Type GetPageType(Type contextType);
}
