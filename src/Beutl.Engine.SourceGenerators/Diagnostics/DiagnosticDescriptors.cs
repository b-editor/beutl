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

    public static readonly DiagnosticDescriptor ResourcePropertyMissingInitializer = new(
        id: "BESG003",
        title: "Stable resource property declaration required",
        messageFormat: "Property '{0}.{1}' must expose one stable declaration-time IProperty so detached Resource defaults can be generated; use a declaration initializer or a readonly computed backing field, do not replace it in a constructor, add a valid [ResourceDefaultValuesProvider] factory, or suppress Resource generation and implement Resource/ToResource manually",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ResourcePrimaryConstructorNotSupported = new(
        id: "BESG004",
        title: "Primary constructor is incompatible with generated Resource defaults",
        messageFormat: "Type '{0}' cannot use a primary constructor with initializer-only detached Resource defaults; add a valid [ResourceDefaultValuesProvider] factory, move to an ordinary constructor, or suppress Resource generation and implement Resource/ToResource manually",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ResourceDefaultValuesProviderInvalid = new(
        id: "BESG005",
        title: "Resource defaults provider signature is invalid",
        messageFormat: "Type '{0}' must declare exactly one [ResourceDefaultValuesProvider] method that is static, parameterless, non-generic, and returns '{0}'",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ResourceDefaultValuesProviderRequiredOnDerivedType = new(
        id: "BESG006",
        title: "Derived resource defaults provider required",
        messageFormat: "Type '{0}' derives from a type with an explicit resource defaults provider and must declare its own [ResourceDefaultValuesProvider] factory so inherited detached defaults are not evaluated through the initializer-only path",
        category: "Beutl.Engine.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
