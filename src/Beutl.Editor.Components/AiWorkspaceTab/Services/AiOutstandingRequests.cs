using Beutl.Api.Services;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

/// <summary>
/// What each request still waiting to be collected was made of.
/// </summary>
/// <remarks>
/// Every outstanding request is held, not only the last: the image editor's tasks are separate
/// operations, so a name outstanding on one says nothing about a request built on another.
/// </remarks>
internal sealed class AiOutstandingRequests
{
    // Keep requests in dispatch order. A task can have more than one uncollected request; choosing
    // an arbitrary one in that case would make the model shown after reopening vary by call.
    private readonly List<(string? AccountId, string Key, string?[] Request)> _held = [];

    public void Remember(AiRequestName name, string?[] request)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;
        Forget(name);
        _held.Add((name.AccountId, name.Key, request));
    }

    public void Forget(AiRequestName name)
    {
        if (!string.IsNullOrEmpty(name.Key))
            _held.RemoveAll(held => (held.AccountId, held.Key) == name.AccountAndKey);
    }

    /// <summary>The account's held requests, oldest first.</summary>
    public IEnumerable<string?[]> All(string? accountId)
        => _held.Where(held => held.AccountId == accountId).Select(held => held.Request);

    /// <summary>Whether any request held by the account matches <paramref name="predicate"/>.</summary>
    public bool Any(string? accountId, Func<string?[], bool> predicate)
        => _held.Exists(held => held.AccountId == accountId && predicate(held.Request));

    /// <summary>
    /// The newest held request that matches <paramref name="predicate"/>, so a list fetched later lands on
    /// the model that request named, and coming back lands where the screen was left.
    /// </summary>
    public bool TryFind(string? accountId, Func<string?[], bool> predicate, out string?[] request)
    {
        for (int index = _held.Count - 1; index >= 0; index--)
        {
            var held = _held[index];
            if (held.AccountId == accountId && predicate(held.Request))
            {
                request = held.Request;
                return true;
            }
        }

        request = [];
        return false;
    }
}
