using Beutl.Api.Services;

namespace Beutl.Services.AI;

/// <summary>
/// What each request still waiting to be collected was made of.
/// </summary>
/// <remarks>
/// The image editor's five tasks are five operations with five model lists and
/// five prices, so a name outstanding on one says nothing about a request built
/// on another. Holding every outstanding request, rather than only the last
/// one, is what lets the screen answer that for the task on show while another
/// task's request is still uncollected.
/// </remarks>
internal sealed class AiOutstandingRequests
{
    private readonly Dictionary<string, string?[]> _byName = new(StringComparer.Ordinal);

    public void Remember(AiRequestName name, string?[] request)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;
        _byName[name.Key] = request;
    }

    public void Forget(AiRequestName name)
    {
        if (!string.IsNullOrEmpty(name.Key))
            _byName.Remove(name.Key);
    }

    /// <summary>Whether any request being held matches <paramref name="predicate"/>.</summary>
    public bool Any(Func<string?[], bool> predicate)
    {
        foreach (string?[] held in _byName.Values)
        {
            if (predicate(held))
                return true;
        }

        return false;
    }
}
