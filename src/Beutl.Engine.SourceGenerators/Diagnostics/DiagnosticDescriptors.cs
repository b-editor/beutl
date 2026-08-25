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
        title: "Render metadata callback reads mutable static state",
        messageFormat:
            "'{0}.{1}' keys its compiled plan by which callback this is, not by what the callback reads, "
            + "so the callback has to answer the same way every time it runs; this one reads the mutable "
            + "static {2} '{3}'. Carry the value through the state-passing overload or a bound render "
            + "resource, or make '{3}' immutable. This check only sees what the callback body names "
            + "itself, so it staying silent is not proof that the callback is state-free.",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Proving a callback state-free in general is not possible, and this rule does not try. It reads "
            + "only the static fields and properties the callback body names directly. A static method the "
            + "body calls, a member reached through a static readonly instance, and mutation of the object "
            + "a static readonly field holds are all invisible to it. Treat silence as the absence of the "
            + "shape authors usually write, not as a purity proof.");
}
