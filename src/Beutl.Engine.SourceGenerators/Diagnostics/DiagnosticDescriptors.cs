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
            + "so the callback has to answer the same way every time it runs; this one reaches the static "
            + "{2} '{3}', and {4}. Carry the value through the state-passing overload or a bound render "
            + "resource, or make '{3}' answer the same way every time. This check reads the callback's own "
            + "body - a lambda's, or the one a method group names - and follows the static methods that "
            + "body names, and the methods those name, to a bounded depth. What a called method whose body "
            + "has no source here reads, what an instance member computes, and what a user-defined "
            + "operator, conversion or constructor does are all still invisible, so it staying silent is "
            + "not proof that the callback is state-free.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Proving a callback state-free in general is not possible, and this rule does not try. It reads "
            + "only the static fields and properties the callback body names directly. A static field is "
            + "accepted when it is const, or readonly and of a type whose instances carry no writable "
            + "state: an enum, a primitive, string, decimal, DateTime, a RenderResourceSlot<T>, or a "
            + "readonly struct or sealed class whose every instance field, inherited ones included, is "
            + "itself readonly and of such a type, walked to a bounded depth. That walk is only run where "
            + "the field list is the whole type - a type declared in this compilation, read from its "
            + "declaration, or a struct, whose fields a compilation imports whatever their accessibility. A "
            + "class from another assembly is imported down to its public and protected members, so its "
            + "field list is a floor and not the type; a static readonly field of one is reported however "
            + "immutable the visible members look, and the fix is to declare the type where this rule can "
            + "read it or to carry the value through the state-passing overload. readonly on its own fixes "
            + "the reference and not the object behind it, so a static readonly field of a type carrying a "
            + "writable field - its own or one it reaches - is reported, and so is one of an unsealed "
            + "class, which a subclass can add state to, or of a delegate, whose target this rule cannot "
            + "read. A static property is accepted only when its getter is proven to yield the same value "
            + "on every read: an expression-bodied getter, a getter whose body is a single return, or the "
            + "initialiser of a get-only auto-property, whose expression is a compile-time constant, an "
            + "enum member, default, or a static readonly field that same test accepts. Every other getter "
            + "is reported, including one whose source is not available here, because a metadata callback "
            + "reading external mutable static state is the hazard this rule exists for; having no setter "
            + "is not on its own evidence that a getter answers the same way twice. All of that is read out "
            + "of the callback's own body, and a method group names a body as surely as a lambda writes "
            + "one, so both are read: the method a group names is followed to its declaration and walked. "
            + "A method group whose body has no source in this compilation is reported, because that body "
            + "is the whole of the callback and silence would say the rule looked when it looked at "
            + "nothing - the position the static field rule already takes for a type whose state was never "
            + "imported. From inside a body the walk follows the static methods that body names, and the "
            + "methods those name, to a bounded depth; a chain longer than the bound is reported rather "
            + "than accepted, so the bound can cost a diagnostic and never hide one. A called method whose "
            + "body has no source here is where the walk stops without reporting: the rule did inspect the "
            + "callback, so the callee is a bound on an inspected callback rather than an uninspected one, "
            + "and reporting it would reject every callback that names Math.Clamp. What is still invisible "
            + "to it: what such a method reads, whatever an instance member computes - one reached through "
            + "an accepted field, or one the body calls on a value - and a user-defined operator, "
            + "conversion or constructor, which a body invokes without naming. Treat silence as the "
            + "absence of the shape authors usually write, not as a purity proof.");

    public static readonly DiagnosticDescriptor UnmarkedRenderNodeMutation = new(
        id: "BESG005",
        title: "Render node changes state its Process reads without marking the node changed",
        messageFormat:
            "'{0}.{1}' assigns '{2}', which '{0}.Process' reads, and no MarkChanged() call on this node is "
            + "reachable from it. A render graph recorded for a node that reports no changes may be reused "
            + "instead of re-recorded, so this change would never reach a frame. Call MarkChanged() where "
            + "the value changes. This rule reads only a direct assignment written inside '{0}', so it "
            + "staying silent is not proof that every mutation is marked.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Deciding in general whether a render node's recorded output can go stale is not possible, and "
            + "this rule does not try. It reports one shape: a member of the node's own type assigns an "
            + "instance field or auto-property that the node's Process reads, and neither that member nor a "
            + "method of the same type it calls marks that node with MarkChanged(). Everything else stays "
            + "invisible - an assignment made through a helper on another type, through a virtual call, or "
            + "by mutating a collection in place; an assignment guarded by a branch that skips the "
            + "MarkChanged() call the rule found elsewhere in the same member; and any assignment written "
            + "inside Process itself or a method Process calls, which are excluded so that ordinary "
            + "memoization is not reported. The "
            + "runtime cross-check in Beutl.Engine, not this rule, is what covers those. Treat silence as "
            + "the absence of the shape authors usually write, not as a proof that the node is safe to skip.");
}
