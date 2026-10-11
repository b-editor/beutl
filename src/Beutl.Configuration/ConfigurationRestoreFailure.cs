namespace Beutl.Configuration;

/// <summary>A setting in settings.json that could not be read and kept its default.</summary>
/// <param name="Setting">The section key, followed by the property name when one setting failed.</param>
public sealed record ConfigurationRestoreFailure(string Setting, Exception Exception);
