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
}
