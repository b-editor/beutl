using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Beutl.AgentToolkit.Live;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
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
        typeof(HistoryTools),
        typeof(RenderTools)
    ];

    private static readonly Dictionary<short, OpCode> s_opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    [Test]
    public void Shared_public_tool_surface_exposes_session_and_declarative_tools()
    {
        string[] names = ToolNames(s_sharedPublicToolTypes);

        Assert.That(names, Is.EquivalentTo(new[]
        {
            "get_started",
            "get_schema",
            "list_fonts",
            "list_effects",
            "list_effect_recipes",
            "get_effect_recipe",
            "validate_shader",
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
            "undo",
            "redo",
            "read_history",
            "read_document_summary",
            "measure_object_bounds",
            "read_document",
            "apply_edit",
            "duplicate_object",
            "plan_composition",
            "apply_composition",
            "render_still",
            "render_storyboard",
            "measure_frame_differences",
            "analyze_audio_rhythm",
            "export_video",
            "read_render_job",
            "cancel_render_job"
        }));
    }

    // A running editor binds a session only from a call's sceneId, so a tool left out of the routed set cannot run there.
    [Test]
    public void Every_tool_that_requires_a_session_is_routed_to_a_scene()
    {
        MethodInfo requireSession = typeof(AgentSessionManager).GetMethod(nameof(AgentSessionManager.RequireSession))!;
        string[] sessionTools = typeof(AgentSessionManager).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null
                             && Reaches(method, requireSession, []))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!)
            .ToArray();

        Assert.Multiple(() =>
        {
            // Through a lambda, an async helper and a snapshot helper.
            Assert.That(sessionTools, Is.SupersetOf(new[] { "apply_edit", "undo", "render_still" }));
            Assert.That(sessionTools.Where(name => !AgentHostSceneRouting.AcceptsScene(name)), Is.Empty);
        });
    }

    // Whether method calls target directly or through the lambdas, state machines and same-assembly methods it uses.
    private static bool Reaches(MethodBase method, MethodBase target, HashSet<MethodBase> visited)
    {
        if (!visited.Add(method))
        {
            return false;
        }

        if (method.GetCustomAttribute<StateMachineAttribute>() is { } stateMachine
            && Reaches(
                stateMachine.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!,
                target,
                visited))
        {
            return true;
        }

        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        Type[]? typeArguments = method.DeclaringType?.GetGenericArguments();
        Type[]? methodArguments = method is MethodInfo { IsGenericMethod: true } ? method.GetGenericArguments() : null;
        for (int offset = 0; offset < il.Length;)
        {
            OpCode opCode = s_opCodes[il[offset] == 0xFE ? unchecked((short)(0xFE00 | il[offset + 1])) : il[offset]];
            offset += opCode.Size;
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                MethodBase? callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset), typeArguments, methodArguments);
                if (callee == target || (callee?.Module == method.Module && Reaches(callee, target, visited)))
                {
                    return true;
                }
            }

            offset += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4,
            };
        }

        return false;
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
