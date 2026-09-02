using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Rendering;

/// <summary>What a recorded fragment answers a hit test with, as a rule rather than a bound delegate.</summary>
/// <remarks>
/// A delegate built while recording closes over the fragments that request held. Replay recreates a fragment
/// over the inputs of the request it is replayed into, so such a delegate would answer for a graph that has
/// ended. A rule names its inputs instead of capturing them, which lets replay rebase the hit test the same
/// way it rebases everything else - and lets <see cref="RenderFragmentReference.RecordingFingerprint"/> speak
/// for the hit test, because a rule has an identity a digest can read.
/// </remarks>
internal readonly struct RenderFragmentHitTest
{
    private readonly Rect _region;
    private readonly RenderHitTestContract _contract;
    private readonly IReadOnlyList<RenderResourceBinding>? _resources;

    private RenderFragmentHitTest(
        RenderFragmentHitTestKind kind,
        Rect region,
        RenderHitTestContract contract,
        IReadOnlyList<RenderResourceBinding>? resources)
    {
        Kind = kind;
        _region = region;
        _contract = contract;
        _resources = resources;
    }

    public RenderFragmentHitTestKind Kind { get; }

    public static RenderFragmentHitTest None => default;

    public static RenderFragmentHitTest Bounds { get; } =
        new(RenderFragmentHitTestKind.Bounds, default, default, null);

    public static RenderFragmentHitTest Inputs { get; } =
        new(RenderFragmentHitTestKind.Inputs, default, default, null);

    public static RenderFragmentHitTest Region(Rect region)
        => new(RenderFragmentHitTestKind.Region, region, default, null);

    public static RenderFragmentHitTest RegionAndInputs(Rect region)
        => new(RenderFragmentHitTestKind.RegionAndInputs, region, default, null);

    public static RenderFragmentHitTest FromContract(
        RenderHitTestContract contract,
        IReadOnlyList<RenderResourceBinding>? resources)
        => new(RenderFragmentHitTestKind.Contract, default, contract, resources);

    public bool Evaluate(Rect bounds, ImmutableArray<RenderFragmentReference> inputs, Point point)
        => Kind switch
        {
            RenderFragmentHitTestKind.Bounds => bounds.Contains(point),
            RenderFragmentHitTestKind.Region => _region.Contains(point),
            RenderFragmentHitTestKind.Inputs => AnyInput(inputs, point),
            RenderFragmentHitTestKind.RegionAndInputs
                => _region.Contains(point) && AnyInput(inputs, point),
            RenderFragmentHitTestKind.Contract
                => _contract.Evaluate(bounds, CreateInputViews(inputs), _resources ?? [], point),
            _ => false,
        };

    /// <summary>A digest of which rule this is, ignoring the state an author-declared contract reads.</summary>
    /// <remarks>
    /// A contract's structural identity is which callback answers, not what it answers over: a contract built
    /// from a resource or from per-recording state keeps one identity while that state moves. The state
    /// belongs to the node that recorded it and is answered for by
    /// <see cref="RenderNode.HasChanges"/>; a consumer that only forwards the hit test reads the live one
    /// through <see cref="RenderFragmentReference.Inputs"/> either way.
    /// </remarks>
    public ulong IdentityDigest
    {
        get
        {
            unchecked
            {
                ulong hash = Combine(14695981039346656037UL, (byte)Kind);
                switch (Kind)
                {
                    case RenderFragmentHitTestKind.Region:
                    case RenderFragmentHitTestKind.RegionAndInputs:
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.X));
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.Y));
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.Width));
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.Height));
                        break;
                    case RenderFragmentHitTestKind.Contract:
                        hash = Combine(hash, (uint)ContractIdentityHash());
                        break;
                }

                return hash;
            }
        }
    }

    /// <summary>The values <see cref="IdentityDigest"/> reads, so a digest match can be settled exactly.</summary>
    public (RenderFragmentHitTestKind Kind, Rect Region, object? ContractIdentity) RuleIdentity
        => (Kind,
            Kind is RenderFragmentHitTestKind.Region or RenderFragmentHitTestKind.RegionAndInputs
                ? _region
                : default,
            Kind == RenderFragmentHitTestKind.Contract ? _contract.StructuralIdentity : null);

    /// <summary>Compares two structural identities the way <see cref="ContractIdentityHash"/> digests one.</summary>
    public static bool SameStructuralIdentity(object? left, object? right)
        => left is ValueType ? left.Equals(right) : ReferenceEquals(left, right);

    private int ContractIdentityHash()
    {
        object identity = _contract.StructuralIdentity;
        // A boxed contract kind or bounds identity answers for its value; a callback answers only for which
        // object it is, because two closures over equal state are not the same declaration.
        return identity is ValueType
            ? identity.GetHashCode()
            : RuntimeHelpers.GetHashCode(identity);
    }

    private static ulong Combine(ulong hash, ulong value)
    {
        unchecked
        {
            return (hash ^ value) * 1099511628211UL;
        }
    }

    private static bool AnyInput(ImmutableArray<RenderFragmentReference> inputs, Point point)
    {
        foreach (RenderFragmentReference input in inputs)
        {
            if (input.HitTest(point))
                return true;
        }

        return false;
    }

    private static RenderHitTestInput[] CreateInputViews(ImmutableArray<RenderFragmentReference> inputs)
    {
        if (inputs.Length == 0)
            return [];

        var views = new RenderHitTestInput[inputs.Length];
        for (int index = 0; index < inputs.Length; index++)
            views[index] = new RenderHitTestInput(inputs[index].Bounds, inputs[index].HitTest);
        return views;
    }
}
