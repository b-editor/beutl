using System.Text.Json;
using Beutl.Api.Clients;

namespace Beutl.Api.Services;

/// <summary>
/// Builds the wire request once and enforces the exact serialized body limit
/// before either the Refit or event-stream transport can send it.
/// </summary>
internal static class AiCaptionTranslationRequestTransport
{
    public static AiCaptionTranslationRequestPayload CreatePayload(
        AiCaptionTranslationRequest request)
    {
        var dto = new AiCaptionTranslationRequestDto
        {
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            Segments = request.Segments.Select(segment => new AiCaptionTranslationSegmentDto
            {
                Id = segment.Id,
                Text = segment.Text,
                Context = segment.Context is { } context
                    ? new AiCaptionTranslationSegmentContextDto
                    {
                        GroupId = context.GroupId,
                        PartIndex = context.PartIndex,
                        Start = context.Start.TotalSeconds,
                        End = context.End.TotalSeconds,
                    }
                    : null,
            }).ToArray(),
            Style = request.Style is { } style
                ? new AiCaptionTranslationStyleDto
                {
                    Glossary = style.Glossary,
                    MaxCharactersPerLine = style.MaxCharactersPerLine,
                    MaxLines = style.MaxLines,
                }
                : null,
            Model = request.Model?.Value,
        };

        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(
            dto,
            AiStreamJson.CanonicalOptions);
        if (serialized.Length > request.Limits.MaxRequestBytes)
        {
            throw new ArgumentException(
                $"The serialized translation request cannot exceed {request.Limits.MaxRequestBytes} bytes.",
                nameof(request));
        }

        return new AiCaptionTranslationRequestPayload(serialized);
    }
}

internal readonly record struct AiCaptionTranslationRequestPayload(
    byte[] Json);
