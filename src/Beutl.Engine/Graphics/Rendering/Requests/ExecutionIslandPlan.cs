using System.Collections.Immutable;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class ExecutionIslandPlan
{
    private readonly Dictionary<RenderFragmentId, ExecutionIslandMembership> _membershipByFragment;

    public ExecutionIslandPlan(
        ImmutableArray<ExecutionIsland> islands,
        ImmutableArray<ExecutionIslandBoundary> boundaries)
    {
        Islands = islands;
        Boundaries = boundaries;
        _membershipByFragment = [];
        for (int index = 0; index < islands.Length; index++)
        {
            ExecutionIsland island = islands[index];
            // A plan holds few islands and this constructor runs on every plan rebind, so the pairwise scan
            // is cheaper than the set it replaced - the same trade the fragment check below already makes.
            for (int earlier = 0; earlier < index; earlier++)
            {
                if (islands[earlier].Id.Equals(island.Id))
                    throw new ArgumentException("Execution-island IDs must be unique.", nameof(islands));
            }

            ValidateIsland(island, nameof(islands));
            for (int fragment = 0; fragment < island.Fragments.Length; fragment++)
            {
                RenderFragmentId fragmentId = island.Fragments[fragment];
                bool terminal = fragment == island.Fragments.Length - 1;
                if (!_membershipByFragment.TryAdd(
                        fragmentId,
                        new ExecutionIslandMembership(island, island.ShaderRun, terminal)))
                {
                    throw new ArgumentException(
                        "A fragment cannot belong to more than one execution island.",
                        nameof(islands));
                }
            }
        }
    }

    public ImmutableArray<ExecutionIsland> Islands { get; }

    public ImmutableArray<ExecutionIslandBoundary> Boundaries { get; }

    public IEnumerable<CompiledShaderRun> ShaderRuns
        => Islands
            .Where(static island => island.ShaderRun is not null)
            .Select(static island => island.ShaderRun!);

    public bool TryGetMembership(
        RenderFragmentReference fragment,
        out ExecutionIslandMembership membership)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Id is not { } id)
            throw new InvalidOperationException("An execution-plan fragment is not committed.");
        return _membershipByFragment.TryGetValue(id, out membership);
    }

    public ExecutionIslandExecutionLedger CreateExecutionLedger(
        RecordedRenderGraph graph,
        ImmutableArray<RenderFragmentReference> roots,
        RenderCacheResolution cacheResolution)
        => new(this, graph, roots, cacheResolution);

    // A cached structural plan rebinds through this constructor on every hit, so both scans stay
    // allocation-free rather than moving behind a Debug gate: a plugin-authored island is validated in the
    // build a plugin author ships against.
    private static bool HasDuplicateFragment(ImmutableArray<RenderFragmentId> fragments)
    {
        const int PairwiseScanLimit = 32;
        if (fragments.Length > PairwiseScanLimit)
        {
            var seen = new HashSet<RenderFragmentId>(fragments.Length);
            foreach (RenderFragmentId fragment in fragments)
            {
                if (!seen.Add(fragment))
                    return true;
            }

            return false;
        }

        for (int index = 0; index < fragments.Length; index++)
        {
            for (int other = index + 1; other < fragments.Length; other++)
            {
                if (fragments[index] == fragments[other])
                    return true;
            }
        }

        return false;
    }

    private static bool MatchesStageOrder(
        ImmutableArray<RenderFragmentId> fragments,
        ImmutableArray<CompiledShaderStage> stages)
    {
        if (fragments.Length != stages.Length)
            return false;

        for (int index = 0; index < fragments.Length; index++)
        {
            if (fragments[index] != stages[index].FragmentId)
                return false;
        }

        return true;
    }

    private static void ValidateIsland(ExecutionIsland island, string parameterName)
    {
        if (HasDuplicateFragment(island.Fragments))
            throw new ArgumentException("An execution island cannot contain a fragment more than once.", parameterName);

        if (island.ShaderRun is not { } run)
        {
            if (island.Fragments.Length != 1)
            {
                throw new ArgumentException(
                    "A non-Shader execution island must identify exactly one semantic fragment.",
                    parameterName);
            }
            return;
        }

        if (!MatchesStageOrder(island.Fragments, run.Stages))
        {
            throw new ArgumentException(
                "A Shader-run island must contain exactly its compiled stages in execution order.",
                parameterName);
        }
        if (run.Output.Id != island.Fragments[^1])
            throw new ArgumentException("A Shader run must publish its final stage.", parameterName);

        RenderFragmentReference current = run.Output;
        for (int index = run.Stages.Length - 1; index >= 0; index--)
        {
            CompiledShaderStage stage = run.Stages[index];
            if (!ReferenceEquals(current, stage.Fragment)
                || current.Id != stage.FragmentId
                || current.Kind != stage.Kind
                || current.Inputs.Length != 1)
            {
                throw new ArgumentException(
                    "A Shader run must describe one direct single-input semantic chain.",
                    parameterName);
            }
            current = current.Inputs[0];
        }
        if (!ReferenceEquals(current, run.Input))
            throw new ArgumentException("A Shader run has a mismatched declared input.", parameterName);
    }
}
