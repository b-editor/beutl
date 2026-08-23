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
    // 送った順のまま持つ。task ごとに 1 つとは限らず、同じ task の依頼が 2 つ
    // 未回収で残ることがある——そのとき「どれか 1 つ」では、戻ってきた画面が
    // 名乗るモデルが呼び出しごとに変わる。
    private readonly List<(string Key, string?[] Request)> _held = [];

    public void Remember(AiRequestName name, string?[] request)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;
        Forget(name);
        _held.Add((name.Key, request));
    }

    public void Forget(AiRequestName name)
    {
        if (!string.IsNullOrEmpty(name.Key))
            _held.RemoveAll(held => string.Equals(held.Key, name.Key, StringComparison.Ordinal));
    }

    /// <summary>Every request being held, oldest first.</summary>
    public IEnumerable<string?[]> All() => _held.Select(held => held.Request);

    /// <summary>Whether any request being held matches <paramref name="predicate"/>.</summary>
    public bool Any(Func<string?[], bool> predicate)
        => _held.Exists(held => predicate(held.Request));

    /// <summary>
    /// The most recently sent request being held that matches
    /// <paramref name="predicate"/>. Reading what it was sent with is how a list
    /// fetched later lands on the model that request named, rather than on
    /// whichever the account can afford today — and the newest is the one the
    /// screen was last showing, so coming back lands where it was left.
    /// </summary>
    public bool TryFind(Func<string?[], bool> predicate, out string?[] request)
    {
        for (int index = _held.Count - 1; index >= 0; index--)
        {
            if (predicate(_held[index].Request))
            {
                request = _held[index].Request;
                return true;
            }
        }

        request = [];
        return false;
    }
}
