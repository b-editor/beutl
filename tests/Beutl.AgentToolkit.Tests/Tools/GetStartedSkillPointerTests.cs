using Beutl.AgentToolkit.Installation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class GetStartedSkillPointerTests
{
    [Test]
    public void Get_started_recommends_the_bundled_agent_skills()
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
                "beutl-agent-asset-sourcing",
                "beutl-agent-source-grounding",
                "beutl-agent-visual-review"
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
    public void Get_started_without_video_type_includes_video_types_and_classification_step()
    {
        var queryTools = new QueryTools(new AgentSessionManager());

        GettingStartedResponse response = queryTools.GetStarted().Value!;

        Assert.Multiple(() =>
        {
            Assert.That(response.VideoTypes, Is.Not.Null);
            Assert.That(response.VideoTypes!, Has.Count.EqualTo(5));
            Assert.That(response.SelectedVideoType, Is.Null);
            Assert.That(response.RecommendedCalls[0], Does.Contain("Classify the brief"));
            Assert.That(response.RecommendedCalls, Has.Some.Contains("derive_palette"));
            Assert.That(response.VideoTypes!.Select(item => item.Name), Is.EquivalentTo(new[]
            {
                "motion-graphics",
                "footage-cut",
                "slideshow",
                "lyric-captions",
                "logo-intro"
            }));
        });
    }

    [Test]
    public void Get_started_with_slideshow_uses_selected_type_workflow()
    {
        var queryTools = new QueryTools(new AgentSessionManager());

        GettingStartedResponse response = queryTools.GetStarted("SlIdEsHoW").Value!;

        Assert.Multiple(() =>
        {
            Assert.That(response.SelectedVideoType, Is.Not.Null);
            Assert.That(response.SelectedVideoType!.Name, Is.EqualTo("slideshow"));
            Assert.That(response.VideoTypes, Is.Null);
            Assert.That(response.RecommendedCalls, Has.Some.Contains("per-photo duration grid"));
            Assert.That(response.RecommendedCalls, Has.Some.Contains("Ken Burns"));
            Assert.That(response.RecommendedCalls, Has.Some.Contains("videoType:\"slideshow\""));
            Assert.That(response.RecommendedCalls, Has.Some.Contains("beutl-agent-asset-sourcing"));
            Assert.That(response.RecommendedCalls, Has.None.Contains("BPM"));
            Assert.That(response.RecommendedCalls, Has.None.Contains("beat grid"));
        });
    }

    [TestCase("footage-cut")]
    [TestCase("slideshow")]
    [TestCase("lyric-captions")]
    public void Get_started_with_media_dependent_video_type_mentions_asset_sourcing(string videoType)
    {
        var queryTools = new QueryTools(new AgentSessionManager());

        GettingStartedResponse response = queryTools.GetStarted(videoType).Value!;

        Assert.That(response.RecommendedCalls, Has.Some.Contains("beutl-agent-asset-sourcing"));
    }

    [Test]
    public void Get_started_with_unknown_video_type_returns_validation_error()
    {
        var queryTools = new QueryTools(new AgentSessionManager());

        var result = queryTools.GetStarted("tutorial");

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error!.Code, Is.EqualTo("validation_rejected"));
            Assert.That(result.Error.Message, Does.Contain("motion-graphics"));
            Assert.That(result.Error.Message, Does.Contain("logo-intro"));
        });
    }

    [Test]
    public void Get_started_recommends_subdivided_storyboard_review_after_motion_authoring()
    {
        var queryTools = new QueryTools(new AgentSessionManager());

        GettingStartedResponse response = queryTools.GetStarted().Value!;

        Assert.That(
            response.RecommendedCalls.Any(call =>
                call.Contains("subdivisionLevel:1", StringComparison.Ordinal)
                && call.Contains("subdivisionLevel:2", StringComparison.Ordinal)
                && call.Contains("bridge animations", StringComparison.Ordinal)),
            Is.True);
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
