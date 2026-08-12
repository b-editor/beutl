using System.Collections.ObjectModel;

namespace Beutl.Editor.Services.Captions;

public readonly struct CaptionDiagnosticKind : IEquatable<CaptionDiagnosticKind>
{
    private readonly string? _value;

    public CaptionDiagnosticKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.Trim();
    }

    public string Value => _value ?? string.Empty;

    public bool Equals(CaptionDiagnosticKind other)
        => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is CaptionDiagnosticKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(CaptionDiagnosticKind left, CaptionDiagnosticKind right)
        => left.Equals(right);

    public static bool operator !=(CaptionDiagnosticKind left, CaptionDiagnosticKind right)
        => !left.Equals(right);
}

public static class CaptionDiagnosticKinds
{
    public static CaptionDiagnosticKind InvalidUtf8 { get; } = new("caption.invalid-utf8");

    public static CaptionDiagnosticKind InvalidHeader { get; } = new("caption.invalid-header");

    public static CaptionDiagnosticKind InvalidStructure { get; } = new("caption.invalid-structure");

    public static CaptionDiagnosticKind InvalidTiming { get; } = new("caption.invalid-timing");
}

public sealed record CaptionDiagnostic(
    CaptionDiagnosticKind Kind,
    int? LineNumber,
    string Message);

public sealed class CaptionImportResult
{
    private CaptionImportResult(
        CaptionDocument? document,
        ReadOnlyCollection<CaptionDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Whether the decoder produced a usable document. A successful import can still contain
    /// diagnostics for malformed cues that were skipped.
    /// </summary>
    public bool IsSuccess => Document is not null;

    public CaptionDocument? Document { get; }

    public IReadOnlyList<CaptionDiagnostic> Diagnostics { get; }

    public static CaptionImportResult Imported(
        CaptionDocument document,
        IEnumerable<CaptionDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        CaptionDiagnostic[] diagnosticList = ValidateDiagnostics(diagnostics ?? []);
        return new CaptionImportResult(document, Array.AsReadOnly(diagnosticList));
    }

    public static CaptionImportResult Failure(IEnumerable<CaptionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        CaptionDiagnostic[] diagnosticList = ValidateDiagnostics(diagnostics);
        if (diagnosticList.Length == 0)
            throw new ArgumentException("A failed import must contain at least one diagnostic.", nameof(diagnostics));

        return new CaptionImportResult(null, Array.AsReadOnly(diagnosticList));
    }

    public static CaptionImportResult Failure(CaptionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return Failure([diagnostic]);
    }

    private static CaptionDiagnostic[] ValidateDiagnostics(
        IEnumerable<CaptionDiagnostic> diagnostics)
    {
        CaptionDiagnostic[] result = diagnostics.ToArray();
        if (result.Any(diagnostic => diagnostic is null))
            throw new ArgumentException("Diagnostics cannot contain null.", nameof(diagnostics));
        return result;
    }
}

public sealed class CaptionExportException : FormatException
{
    public CaptionExportException(int? cueIndex, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        CueIndex = cueIndex;
    }

    public int? CueIndex { get; }
}
