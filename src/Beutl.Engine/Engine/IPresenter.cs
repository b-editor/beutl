namespace Beutl.Engine;

public interface IPresenter
{
    Type TargetType { get; }
}

public interface IPresenter<T> : IPresenter
    where T : CoreObject
{
    IProperty<T?> Target { get; }

    Type IPresenter.TargetType => typeof(T);
}
