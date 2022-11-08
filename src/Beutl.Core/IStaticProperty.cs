namespace Beutl;

public interface IStaticProperty : ICoreProperty
{
    bool CanRead { get; }

    bool CanWrite { get; }
}
