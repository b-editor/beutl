using Beutl.Editor.Models;

namespace Beutl.Editor.Services;

public interface IElementAdder
{
    ValueTask<ElementAddResult> AddAsync(
        IReadOnlyList<ElementDescription> descriptions,
        CancellationToken cancellationToken);
}
