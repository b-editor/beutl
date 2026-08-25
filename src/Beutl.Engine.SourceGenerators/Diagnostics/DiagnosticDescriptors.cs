using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Diagnostics;

public static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MissingPartial = new(
        id: "BESG001",
        title: "Partial declaration required",
        messageFormat: "Type '{0}' must be declared partial to generate Resource nested classes",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FallbackMissingPartial = new(
        id: "BESG002",
        title: "Partial declaration required for IFallback",
        messageFormat: "Type '{0}' must be declared partial to generate IFallback implementation",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CapturingMetadataCallback = new(
        id: "BESG003",
        title: "Render metadata callback is not a stable, state-free delegate",
        messageFormat:
            "'{0}.{1}' keys its compiled plan by which callback this is, not by what the callback closed "
            + "over, so {2}. Declare the callback static and carry changing values through the "
            + "state-passing overload or a bound render resource.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StaticStateMetadataCallback = new(
        id: "BESG004",
        title: "Render metadata callback reads static state that is not proven constant",
        messageFormat:
            "'{0}.{1}' keys its compiled plan by which callback this is, not by what the callback reads, "
            + "so the callback has to answer the same way every time it runs; this one reads the static "
            + "{2} '{3}', and {4}. Carry the value through the state-passing overload or a bound render "
            + "resource, or make '{3}' yield the same value on every read. This check only sees what the "
            + "callback body names itself, so it staying silent is not proof that the callback is "
            + "state-free.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Proving a callback state-free in general is not possible, and this rule does not try. It reads "
            + "only the static fields and properties the callback body names directly. A static field is "
            + "accepted when it is const or readonly. A static property is accepted only when its getter is "
            + "proven to yield the same value on every read: an expression-bodied getter, a getter whose "
            + "body is a single return, or the initialiser of a get-only auto-property, whose expression is "
            + "a compile-time constant, an enum member, default, or a static readonly field of an immutable "
            + "type. Every other getter is reported, including one whose source is not available here, "
            + "because a metadata callback reading external mutable static state is the hazard this rule "
            + "exists for; having no setter is not on its own evidence that a getter answers the same way "
            + "twice. A static method the body calls, a member reached through a static readonly instance, "
            + "and mutation of the object a static readonly field holds are all still invisible to it. Treat "
            + "silence as the absence of the shape authors usually write, not as a purity proof.");

    public static readonly DiagnosticDescriptor UnmarkedRenderNodeMutation = new(
        id: "BESG005",
        title: "Render node changes state its Process reads without marking the node changed",
        messageFormat:
            "'{0}.{1}' assigns '{2}', which '{0}.Process' reads, and no assignment to HasChanges is reachable "
            + "from it. A render graph recorded for a node that reports no changes may be reused instead of "
            + "re-recorded, so this change would never reach a frame. Set HasChanges = true where the value "
            + "changes. This rule reads only a direct assignment written inside '{0}', so it staying silent "
            + "is not proof that every mutation is marked.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Deciding in general whether a render node's recorded output can go stale is not possible, and "
            + "this rule does not try. It reports one shape: a member of the node's own type assigns an "
            + "instance field or auto-property that the node's Process reads, and neither that member nor a "
            + "method of the same type it calls assigns HasChanges. Everything else stays invisible - an "
            + "assignment made through a helper on another type, through a virtual call, or by mutating a "
            + "collection in place; an assignment guarded by a branch that skips the HasChanges the rule "
            + "found elsewhere in the same member; and any assignment written inside Process itself or a "
            + "method Process calls, which are excluded so that ordinary memoization is not reported. The "
            + "runtime cross-check in Beutl.Engine, not this rule, is what covers those. Treat silence as "
            + "the absence of the shape authors usually write, not as a proof that the node is safe to skip.");
}
