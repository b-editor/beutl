using System.Collections.Immutable;

namespace Beutl.Api.Services;

public static class AiRequestLimits
{
    private static readonly ImmutableHashSet<string> s_iso6391LanguageCodes =
        ("aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs "
        + "ca ce ch co cr cs cu cv cy da de dv dz ee el en eo es et eu fa ff "
        + "fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id "
        + "ie ig ii ik io is it iu ja jv ka kg ki kj kk kl km kn ko kr ks ku "
        + "kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml mn mr ms mt my "
        + "na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps pt qu "
        + "rm rn ro ru rw sa sc sd se sg si sk sl sm sn so sq sr ss st su sv "
        + "sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi vo "
        + "wa wo xh yi yo za zh zu")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToImmutableHashSet(StringComparer.Ordinal);

    public const int MaxPromptLength = 4_000;

    public const int MaxTranslationSegments = 200;

    public const int MaxTranslationCharacters = 20_000;

    public const int MaxTranslationRequestBytes = 128 * 1024;

    public const long MaxFrameUploadBytes = 5L * 1024 * 1024;

    public const long MaxImageUploadBytes = 20L * 1024 * 1024;

    // What the transcription endpoint takes in one upload. Speech is sent as
    // 16 kHz mono 16-bit PCM, so this is a little under fourteen minutes of it:
    // audio longer than that has to be split before it is sent, and a caller
    // that sends it anyway is refused after the whole upload has gone out.
    public const long MaxTranscriptionUploadBytes = 25L * 1024 * 1024;

    // What the server is priced for. Each model publishes its own count and the
    // smaller of the two is what may be sent; this is also what a server that
    // publishes nothing is read as.
    public const int MaxImageReferences = 4;

    // What all the reference pictures of one request may come to together, for
    // a server that publishes no figure of its own. The per-picture limit taken
    // four times over is more than the server can hold: every picture is kept
    // raw, again as base64 and again through JSON, so the fallback is what one
    // picture was already allowed to be.
    public const long MaxImageReferencesTotalBytes = MaxImageUploadBytes;

    // The provider accepts a signed 32-bit seed. Bounding it here keeps the
    // same number intact through every JSON encoder on the way.
    public const int MinSeed = 0;

    public const int MaxSeed = int.MaxValue;

    // The span the server considers at all. Which whole seconds within it a
    // given model takes is published per model: Veo 3.1 takes 4, 6 or 8 and
    // Seedance 2.5 anything from 4 to 30, so a fixed three would offer lengths
    // one model refuses and hide most of another's.
    public const int MinVideoDurationSeconds = 1;

    public const int MaxVideoDurationSeconds = 60;

    internal static int ValidateVideoDurationSeconds(
        int durationSeconds,
        string parameterName)
    {
        if (!IsValidVideoDurationSeconds(durationSeconds))
            throw new ArgumentOutOfRangeException(parameterName);
        return durationSeconds;
    }

    internal static bool IsValidVideoDurationSeconds(int durationSeconds)
        => durationSeconds is >= MinVideoDurationSeconds and <= MaxVideoDurationSeconds;

    internal static string ValidatePrompt(string prompt, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, parameterName);
        string normalized = prompt.Trim();
        if (normalized.Length > MaxPromptLength)
        {
            throw new ArgumentException(
                $"The final prompt cannot exceed {MaxPromptLength} characters.",
                parameterName);
        }

        return normalized;
    }

    internal static bool IsSafeTranslationIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            return false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool alphaNumeric = c is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9';
            if (i == 0 ? !alphaNumeric : !alphaNumeric && c is not ('_' or '-'))
                return false;
        }

        return true;
    }

    internal static bool IsIso6391LanguageCode(string value)
        => s_iso6391LanguageCodes.Contains(value);

    internal static string? ValidateOptionalPrompt(string? prompt, string parameterName)
        => string.IsNullOrWhiteSpace(prompt) ? null : ValidatePrompt(prompt, parameterName);

    internal static int? ValidateOptionalSeed(int? seed, string parameterName)
    {
        if (seed is null) return null;
        if (seed.Value is < MinSeed or > MaxSeed)
            throw new ArgumentOutOfRangeException(parameterName);
        return seed;
    }

    // The server holds an idempotency key to printable ASCII and refuses the
    // request outright when it does not match, so a key that could never be
    // accepted is caught here rather than after the whole upload has gone out.
    internal static string? ValidateOptionalIdempotencyKey(string? key, string parameterName)
    {
        if (key is null)
            return null;
        if (key.Length is 0 or > 255)
            throw new ArgumentException("The idempotency key length is invalid.", parameterName);
        foreach (char character in key)
        {
            if (character is < '\u0021' or > '\u007e')
            {
                throw new ArgumentException(
                    "An idempotency key may only contain printable ASCII.",
                    parameterName);
            }
        }

        return key;
    }

    // A default-constructed AiModelId carries no id, and sending an empty
    // string would be refused as an unknown model. It means "no choice made",
    // so it is normalized to null and the server picks its own default.
    internal static AiModelId? ValidateOptionalModel(AiModelId? model, string parameterName)
    {
        _ = parameterName;
        return model is { Value.Length: > 0 } ? model : null;
    }
}
