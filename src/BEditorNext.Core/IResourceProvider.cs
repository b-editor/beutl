using System.Diagnostics.CodeAnalysis;

namespace BEditorNext;

public interface IResourceProvider
{
    IObservable<T?> GetResourceObservable<T>(ResourceReference<T> reference);

    bool TryFindResource<T>(ResourceReference<T> reference, [NotNullWhen(true)] out T? value);
}
