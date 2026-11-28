using Avalonia.Headless.NUnit;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ProjectSessionRegistryTests
{
    [AvaloniaTest]
    public void Add_loads_the_project_into_the_application_without_activating_it()
    {
        var app = new BeutlApplication();
        var registry = new ProjectSessionRegistry(app);
        var project = new Project();
        FontManager fonts = TestShell.FontManager.Fork();

        ProjectSession session = registry.Add(project, fonts);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Sessions, Is.EqualTo(new[] { session }));
            Assert.That(session.Project, Is.SameAs(project));
            Assert.That(session.ProjectFonts, Is.SameAs(fonts));
            Assert.That(app.Projects, Is.EqualTo(new[] { project }));
            Assert.That(project.HierarchicalParent, Is.SameAs(app));
            Assert.That(registry.ActiveSession.Value, Is.Null);
            Assert.That(registry.ActiveProject.Value, Is.Null);
            Assert.That(app.ActiveProject.Value, Is.Null);
        });
    }

    [AvaloniaTest]
    public void Two_sessions_are_held_and_the_active_one_switches()
    {
        var app = new BeutlApplication();
        var registry = new ProjectSessionRegistry(app);
        ProjectSession first = registry.Add(new Project(), TestShell.FontManager.Fork());
        ProjectSession second = registry.Add(new Project(), TestShell.FontManager.Fork());
        var activeProjects = new List<Project?>();
        using IDisposable subscription = registry.ActiveProject.Subscribe(activeProjects.Add);

        registry.Activate(first);
        registry.Activate(second);

        Assert.Multiple(() =>
        {
            Assert.That(registry.ActiveSession.Value, Is.SameAs(second));
            Assert.That(registry.ActiveProject.Value, Is.SameAs(second.Project));
            Assert.That(app.ActiveProject.Value, Is.SameAs(second.Project));
            Assert.That(activeProjects, Is.EqualTo(new[] { null, first.Project, second.Project }));
            Assert.That(registry.Sessions, Is.EqualTo(new[] { first, second }));
            Assert.That(first.Project.HierarchicalParent, Is.SameAs(app));
            Assert.That(second.Project.HierarchicalParent, Is.SameAs(app));
        });
    }

    [AvaloniaTest]
    public void The_application_project_changes_before_the_active_session_is_published()
    {
        var app = new BeutlApplication();
        var registry = new ProjectSessionRegistry(app);
        ProjectSession session = registry.Add(new Project(), TestShell.FontManager.Fork());
        Project? seen = null;
        using IDisposable subscription = registry.ActiveSession.Subscribe(active =>
        {
            if (active is not null)
                seen = app.ActiveProject.Value;
        });

        registry.Activate(session);

        Assert.That(seen, Is.SameAs(session.Project));
    }

    [AvaloniaTest]
    public void Remove_deactivates_and_unloads_the_session()
    {
        var app = new BeutlApplication();
        var registry = new ProjectSessionRegistry(app);
        ProjectSession active = registry.Add(new Project(), TestShell.FontManager.Fork());
        ProjectSession other = registry.Add(new Project(), TestShell.FontManager.Fork());
        registry.Activate(active);

        Assert.That(registry.Remove(active), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(registry.ActiveSession.Value, Is.Null);
            Assert.That(registry.ActiveProject.Value, Is.Null);
            Assert.That(app.ActiveProject.Value, Is.Null);
            Assert.That(registry.Sessions, Is.EqualTo(new[] { other }));
            Assert.That(app.Projects, Is.EqualTo(new[] { other.Project }));
            Assert.That(active.Project.HierarchicalParent, Is.Null);
            Assert.That(registry.Remove(active), Is.False);
        });
    }

    [AvaloniaTest]
    public void Removing_an_inactive_session_keeps_the_active_one()
    {
        var registry = new ProjectSessionRegistry(new BeutlApplication());
        ProjectSession active = registry.Add(new Project(), TestShell.FontManager.Fork());
        ProjectSession other = registry.Add(new Project(), TestShell.FontManager.Fork());
        registry.Activate(active);

        registry.Remove(other);

        Assert.That(registry.ActiveSession.Value, Is.SameAs(active));
    }

    [AvaloniaTest]
    public void A_project_is_loaded_once()
    {
        var registry = new ProjectSessionRegistry(new BeutlApplication());
        var project = new Project();
        registry.Add(project, TestShell.FontManager.Fork());

        Assert.Throws<InvalidOperationException>(() => registry.Add(project, TestShell.FontManager.Fork()));
        Assert.That(registry.Sessions, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    public void A_project_file_is_loaded_once()
    {
        var registry = new ProjectSessionRegistry(new BeutlApplication());
        string file = Path.Combine(Path.GetTempPath(), $"session-{Guid.NewGuid():N}", "project.bep");
        registry.Add(new Project { Uri = new Uri(file) }, TestShell.FontManager.Fork());
        var duplicate = new Project { Uri = new Uri(file) };

        Assert.Throws<InvalidOperationException>(() => registry.Add(duplicate, TestShell.FontManager.Fork()));
        Assert.Multiple(() =>
        {
            Assert.That(registry.Sessions, Has.Count.EqualTo(1));
            Assert.That(duplicate.HierarchicalParent, Is.Null);
            Assert.That(registry.Find(file), Is.SameAs(registry.Sessions[0]));
            Assert.That(registry.Sessions[0].FilePath, Is.EqualTo(file));
        });
    }

    [AvaloniaTest]
    public void A_project_that_has_a_parent_is_not_loaded()
    {
        var project = new Project();
        _ = new BeutlApplication { Projects = { project } };
        var app = new BeutlApplication();
        var registry = new ProjectSessionRegistry(app);

        Assert.Throws<InvalidOperationException>(() => registry.Add(project, TestShell.FontManager.Fork()));
        Assert.Multiple(() =>
        {
            Assert.That(registry.Sessions, Is.Empty);
            Assert.That(app.Projects, Is.Empty);
        });
    }

    [AvaloniaTest]
    public void Only_a_loaded_session_can_be_activated()
    {
        var registry = new ProjectSessionRegistry(new BeutlApplication());
        ProjectSession unloaded = new ProjectSessionRegistry(new BeutlApplication())
            .Add(new Project(), TestShell.FontManager.Fork());

        Assert.Throws<InvalidOperationException>(() => registry.Activate(unloaded));
        Assert.That(registry.ActiveSession.Value, Is.Null);
    }

    [AvaloniaTest]
    public void Find_looks_a_session_up_by_its_project()
    {
        var registry = new ProjectSessionRegistry(new BeutlApplication());
        ProjectSession session = registry.Add(new Project(), TestShell.FontManager.Fork());

        Assert.Multiple(() =>
        {
            Assert.That(registry.Find(session.Project), Is.SameAs(session));
            Assert.That(registry.Find(new Project()), Is.Null);
            Assert.That(session.FilePath, Is.Null);
        });
    }

    // ProjectService resolves fonts per loaded project and follows the registry's active session.
    [AvaloniaTest]
    public void ProjectService_follows_the_active_session_and_resolves_fonts_per_project()
    {
        var registry = new ProjectSessionRegistry(new BeutlApplication());
        var service = new ProjectService(
            new Beutl.Configuration.ViewConfig(), TestShell.FontManager, TestShell.Notifications, registry);
        ProjectSession first = registry.Add(new Project(), TestShell.FontManager.Fork());
        ProjectSession second = registry.Add(new Project(), TestShell.FontManager.Fork());

        Assert.Multiple(() =>
        {
            Assert.That(service.IsOpened.Value, Is.False);
            Assert.That(service.ProjectFonts, Is.Null);
            Assert.That(service.GetFonts(first.Project), Is.SameAs(first.ProjectFonts));
            Assert.That(service.GetFonts(second.Project), Is.SameAs(second.ProjectFonts));
            Assert.That(service.GetFonts(new Project()), Is.SameAs(TestShell.FontManager));
            Assert.That(service.GetFonts(null), Is.SameAs(TestShell.FontManager));
        });

        registry.Activate(second);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentProject.Value, Is.SameAs(second.Project));
            Assert.That(service.IsOpened.Value, Is.True);
            Assert.That(service.ProjectFonts, Is.SameAs(second.ProjectFonts));
        });

        registry.Activate(first);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentProject.Value, Is.SameAs(first.Project));
            Assert.That(service.ProjectFonts, Is.SameAs(first.ProjectFonts));
        });

        registry.Remove(first);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentProject.Value, Is.Null);
            Assert.That(service.IsOpened.Value, Is.False);
            Assert.That(service.GetFonts(first.Project), Is.SameAs(TestShell.FontManager));
        });
    }

    // With one session, opening and closing a project through the shell loads and unloads exactly that session.
    [AvaloniaTest]
    public async Task Opening_and_closing_a_project_loads_and_unloads_one_session()
    {
        await TestReset.ResetShellAsync();
        ProjectSessionRegistry registry = TestShell.Project.Sessions;
        Assert.That(registry.Sessions, Is.Empty);

        Project? project = await TestShell.Project.CreateProject(
            640, 480, new Rational(30), 44100, "session", TestShell.NewWorkspace("session"));
        HeadlessTestHelpers.Settle();

        Assert.That(project, Is.Not.Null);
        ProjectSession session = registry.Sessions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(session.Project, Is.SameAs(project));
            Assert.That(registry.ActiveSession.Value, Is.SameAs(session));
            Assert.That(BeutlApplication.Current.Projects, Is.EqualTo(new[] { project }));
            Assert.That(BeutlApplication.Current.ActiveProject.Value, Is.SameAs(project));
            Assert.That(TestShell.Project.ProjectFonts, Is.SameAs(session.ProjectFonts));
            Assert.That(session.FilePath, Is.EqualTo(project!.Uri!.LocalPath));
        });

        await TestShell.Project.CloseProjectAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Sessions, Is.Empty);
            Assert.That(registry.ActiveSession.Value, Is.Null);
            Assert.That(BeutlApplication.Current.Projects, Is.Empty);
            Assert.That(BeutlApplication.Current.ActiveProject.Value, Is.Null);
            Assert.That(project!.HierarchicalParent, Is.Null);
        });
    }
}
