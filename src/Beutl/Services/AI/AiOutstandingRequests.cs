using Beutl.Api.Services;

namespace Beutl.Services.AI;

/// <summary>
/// What each request still waiting to be collected was sent as, so that asking
/// for one again asks for the same thing.
/// </summary>
/// <remarks>
/// <para>
/// The model is part of what names a request, and the picker moves on its own —
/// an operator withdraws one, the account can no longer pay for another, the
/// user chooses a different one for the next request. A request asked for again
/// on any other model is a different request, and buying it again is what the
/// name was there to prevent.
/// </para>
/// <para>
/// One slot is not enough. A picture charged for and lost, followed by a second
/// one the user asked for after changing the prompt and the model, leaves two
/// names outstanding: coming back to the first has to find the model the first
/// was sent with, not the model the second moved the picker to.
/// </para>
/// </remarks>
internal sealed class AiOutstandingRequests
{
    // 名前ごとに、その名前を作った依頼の中身（モデル欄は空）と、名乗ったモデル。
    private readonly Dictionary<string, (string?[] Request, AiModelId? Model)> _byName =
        new(StringComparer.Ordinal);

    /// <summary>How many names are being held. Changes are worth reporting.</summary>
    public int Count => _byName.Count;

    public void Remember(AiRequestName name, string?[] request, AiModelId? model)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;
        _byName[name.Key] = (request, model);
    }

    public void Forget(AiRequestName name)
    {
        if (!string.IsNullOrEmpty(name.Key))
            _byName.Remove(name.Key);
    }

    public void Clear() => _byName.Clear();

    /// <summary>
    /// The model <paramref name="request"/> was sent with, when it is one of
    /// the requests being held.
    /// </summary>
    public bool TryGetModel(string?[] request, out AiModelId? model)
    {
        foreach ((string?[] held, AiModelId? sentWith) in _byName.Values)
        {
            if (held.AsSpan().SequenceEqual(request))
            {
                model = sentWith;
                return true;
            }
        }

        model = null;
        return false;
    }

    /// <summary>Whether any held request matches <paramref name="predicate"/>.</summary>
    public bool Any(Func<string?[], bool> predicate)
    {
        foreach ((string?[] held, AiModelId? _) in _byName.Values)
        {
            if (predicate(held))
                return true;
        }

        return false;
    }
}
