using Beutl.Editor.Models;

namespace Beutl.Editor.Services;

public interface IElementAdder
{
    IElementSourceHandlerRegistry SourceHandlers { get; }

    ValueTask<ElementAddResult> AddAsync(
        IReadOnlyList<ElementDescription> descriptions,
        CancellationToken cancellationToken);
}
