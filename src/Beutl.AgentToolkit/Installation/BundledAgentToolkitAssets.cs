using System.Reflection;

namespace Beutl.AgentToolkit.Installation;

public static class BundledAgentToolkitAssets
{
    private static readonly AssetDescriptor[] s_descriptors =
    [
        new(
            AgentToolkitAssetKind.Skill,
            "beutl-agent-timeline-from-shotlist/SKILL.md",
            "Beutl.AgentToolkit.Installation.Assets.skills.beutl-agent-timeline-from-shotlist.SKILL.md"),
        new(
            AgentToolkitAssetKind.Skill,
            "beutl-agent-look-effect-chain/SKILL.md",
            "Beutl.AgentToolkit.Installation.Assets.skills.beutl-agent-look-effect-chain.SKILL.md"),
        new(
            AgentToolkitAssetKind.Skill,
            "beutl-agent-source-grounding/SKILL.md",
            "Beutl.AgentToolkit.Installation.Assets.skills.beutl-agent-source-grounding.SKILL.md"),
        new(
            AgentToolkitAssetKind.Subagent,
            "beutl-agent-timeline-builder.md",
            "Beutl.AgentToolkit.Installation.Assets.agents.beutl-agent-timeline-builder.md"),
        new(
            AgentToolkitAssetKind.Subagent,
            "beutl-agent-look-applier.md",
            "Beutl.AgentToolkit.Installation.Assets.agents.beutl-agent-look-applier.md"),
    ];

    public static IReadOnlyList<AgentToolkitAsset> Load()
    {
        Assembly assembly = typeof(BundledAgentToolkitAssets).Assembly;
        var assets = new List<AgentToolkitAsset>(s_descriptors.Length);

        foreach (AssetDescriptor descriptor in s_descriptors)
        {
            using Stream? stream = assembly.GetManifestResourceStream(descriptor.ResourceName);
            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"Bundled agent toolkit asset was not found: {descriptor.ResourceName}");
            }

            using var reader = new StreamReader(stream);
            assets.Add(new AgentToolkitAsset(
                descriptor.Kind,
                descriptor.RelativePath,
                reader.ReadToEnd()));
        }

        return assets;
    }

    private sealed record AssetDescriptor(
        AgentToolkitAssetKind Kind,
        string RelativePath,
        string ResourceName);
}
