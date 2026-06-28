using System.Reflection;
using Beutl.AgentToolkit.Tools;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ToolSurfaceTests
{
    private static readonly Type[] s_sharedPublicToolTypes =
    [
        typeof(QueryTools),
        typeof(EditTools),
        typeof(RenderTools)
    ];

    [Test]
    public void Shared_public_tool_surface_is_declarative_only()
    {
        string[] names = ToolNames(s_sharedPublicToolTypes);

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EquivalentTo(new[]
            {
                "get_started",
                "get_schema",
                "get_examples",
                "read_document_summary",
                "read_document",
                "plan_edit",
                "apply_edit",
                "render_still",
                "export_video"
            }));
            Assert.That(names, Does.Not.Contain("add_keyframe"));
            Assert.That(names, Does.Not.Contain("update_keyframe"));
            Assert.That(names, Does.Not.Contain("remove_keyframe"));
            Assert.That(names, Does.Not.Contain("set_property"));
            Assert.That(names, Does.Not.Contain("undo"));
            Assert.That(names, Does.Not.Contain("redo"));
        });
    }

    [Test]
    public void Internal_element_wrappers_are_not_in_shared_public_surface()
    {
        string[] publicNames = ToolNames(s_sharedPublicToolTypes);
        string[] internalElementNames = ToolNames([typeof(ElementTools)]);

        Assert.Multiple(() =>
        {
            Assert.That(internalElementNames, Does.Contain("add_element"));
            Assert.That(publicNames.Intersect(internalElementNames), Is.Empty);
        });
    }

    private static string[] ToolNames(params Type[] toolTypes)
    {
        return toolTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
