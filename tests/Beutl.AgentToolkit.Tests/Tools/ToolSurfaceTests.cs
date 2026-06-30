using System.Reflection;
using Beutl.AgentToolkit.Tools;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ToolSurfaceTests
{
    private static readonly Type[] s_sharedPublicToolTypes =
    [
        typeof(QueryTools),
        typeof(SessionTools),
        typeof(EditTools),
        typeof(RenderTools)
    ];

    [Test]
    public void Shared_public_tool_surface_exposes_session_and_declarative_tools()
    {
        string[] names = ToolNames(s_sharedPublicToolTypes);

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EquivalentTo(new[]
            {
                "get_started",
                "list_creative_directions",
                "record_creative_direction",
                "get_schema",
                "list_effects",
                "list_effect_recipes",
                "get_effect_recipe",
                "list_compositions",
                "get_composition",
                "render_composition_patch",
                "list_examples",
                "get_examples",
                "open_project",
                "create_project",
                "add_scene",
                "save_project",
                "read_operation_status",
                "read_document_summary",
                "measure_object_bounds",
                "read_document",
                "apply_edit",
                "plan_composition",
                "apply_composition",
                "render_still",
                "evaluate_motion_variation",
                "evaluate_edit_quality",
                "preview_quality_risks",
                "suggest_quality_fixes",
                "final_preflight",
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
