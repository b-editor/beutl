using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

/// <summary>What a recorded fragment answers a hit test with, as a rule rather than a bound delegate.</summary>
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
