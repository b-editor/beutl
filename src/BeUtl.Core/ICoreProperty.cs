using System.Diagnostics.CodeAnalysis;

namespace BeUtl;

public interface ICoreProperty
{
    string Name { get; }

    Type PropertyType { get; }

    Type OwnerType { get; }

    int Id { get; }

    IObservable<CorePropertyChangedEventArgs> Changed { get; }

    TMetadata GetMetadata<TMetadata>(Type type)
        where TMetadata : ICorePropertyMetadata;

    bool TryGetMetadata<TMetadata>(Type type, [NotNullWhen(true)] out TMetadata? result)
        where TMetadata : ICorePropertyMetadata;
}
