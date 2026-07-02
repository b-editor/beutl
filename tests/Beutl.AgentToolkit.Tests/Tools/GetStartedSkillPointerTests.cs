using Beutl.AgentToolkit.Installation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class GetStartedSkillPointerTests
{
    [Test]
    public void Get_started_recommends_the_three_composition_planning_skills()
    {
        var queryTools = new QueryTools(new AgentSessionManager());

        GettingStartedResponse response = queryTools.GetStarted().Value!;

        string[] names = response.RecommendedSkills.Select(s => s.Name).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EquivalentTo(new[]
            {
                "beutl-agent-timeline-from-shotlist",
                "beutl-agent-look-effect-chain",
                "beutl-agent-source-grounding"
            }));
            Assert.That(
                response.RecommendedSkills.All(s =>
                    !string.IsNullOrWhiteSpace(s.WhenToUse)
                    && !string.IsNullOrWhiteSpace(s.HowToLoad)),
                Is.True,
                "every recommended skill needs a non-empty WhenToUse and HowToLoad");
        });
    }

    [Test]
    public void Get_started_recommended_calls_lead_with_a_skill_pointer()
    {
        var queryTools = new QueryTools(new AgentSessionManager());

        GettingStartedResponse response = queryTools.GetStarted().Value!;

        Assert.That(
            response.RecommendedCalls.Any(c =>
                c.Contains("recommendedSkills", StringComparison.OrdinalIgnoreCase)
                && c.Contains("beutl-agent-timeline-from-shotlist", StringComparison.Ordinal)),
            Is.True,
            "RecommendedCalls should carry a lead pointer to the skills for clients that ignore the structured field");
    }

    [Test]
    public void Get_started_skill_names_match_the_bundled_skill_set()
    {
        var queryTools = new QueryTools(new AgentSessionManager());
        string[] recommended = queryTools.GetStarted().Value!
            .RecommendedSkills.Select(s => s.Name).ToArray();

        string[] bundled = BundledAgentToolkitAssets.Load()
            .Where(a => a.Kind == AgentToolkitAssetKind.Skill)
            .Select(a => a.RelativePath.Split('/')[0])
            .ToArray();

        Assert.That(
            recommended,
            Is.EquivalentTo(bundled),
            "get_started's recommendedSkills must stay in sync with the bundled skill set; update GetStarted() when a skill is added, removed, or renamed in BundledAgentToolkitAssets");
    }
}
