using Beutl.Agents;
using Beutl.AgentToolkit.Installation;
using Beutl.Editor.Components.AgentTab;

namespace Beutl.AgentHost;

internal sealed partial class BeutlAgentWorkspaceHost
{
    private static readonly Lazy<BundledSkill[]> s_bundledSkills = new(LoadBundledSkills);
    private static readonly Lazy<AgentComposerSuggestion[]> s_bundledSkillSuggestions =
        new(() => s_bundledSkills.Value.Select(skill => skill.Suggestion).ToArray());

    public IReadOnlyList<AgentComposerSuggestion> BundledSkills => s_bundledSkillSuggestions.Value;

    internal static IReadOnlyList<AgentSkillInstruction>? ResolveBundledSkills(string text)
    {
        var names = AgentComposerToken.InvokedNames(text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
            return null;
        var skills = s_bundledSkills.Value.Where(skill => names.Contains(skill.Suggestion.Name))
            .Select(skill => new AgentSkillInstruction(skill.Suggestion.Name, skill.Instructions)).ToArray();
        return skills.Length == 0 ? null : skills;
    }

    private static BundledSkill[] LoadBundledSkills()
        => BundledAgentToolkitAssets.Load()
            .Where(asset => asset.Kind == AgentToolkitAssetKind.Skill && asset.RelativePath.EndsWith("/SKILL.md", StringComparison.Ordinal))
            .Select(asset =>
            {
                string name = asset.RelativePath[..asset.RelativePath.IndexOf('/')];
                var lines = asset.Content.ReplaceLineEndings("\n").Split('\n');
                int end = Array.IndexOf(lines, "---", 1);
                const string prefix = "description:";
                string? description = end > 0 ? lines[1..end]
                    .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim() : null;
                string instructions = end > 0 ? string.Join('\n', lines[(end + 1)..]).Trim() : asset.Content;
                return new BundledSkill(new(name, description, AgentComposerSuggestionKind.Skill, Source: "Beutl"), instructions);
            }).ToArray();

    private sealed record BundledSkill(AgentComposerSuggestion Suggestion, string Instructions);
}
