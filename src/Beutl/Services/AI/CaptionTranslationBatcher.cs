using System.Globalization;
using Beutl.Api.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Language;

namespace Beutl.Services.AI;

internal static class CaptionTranslationBatcher
{
    internal sealed record TranslationPiece(
        int CueIndex,
        int PartIndex,
        int ContextPartIndex,
        string Id,
        string GroupId,
        TimeSpan Start,
        TimeSpan End,
        string Text);

    internal sealed record TranslationBatch(IReadOnlyList<TranslationPiece> Pieces);

    public static List<TranslationBatch> CreateBatches(
        CaptionDocument document,
        string? sourceLanguage,
        string targetLanguage,
        AiModelId? model,
        AiCaptionTranslationLimits limits)
    {
        var pieces = new List<TranslationPiece>();
        for (int cueIndex = 0; cueIndex < document.Count; cueIndex++)
        {
            CaptionCue cue = document[cueIndex];
            if (string.IsNullOrWhiteSpace(cue.Text))
                continue;

            int[] textElementBoundaries = CreateTranslationTextElementBoundaries(cue.Text);
            int offset = 0;
            int partIndex = 0;
            while (offset < cue.Text.Length)
            {
                int maximumLength = Math.Min(
                    limits.MaxCharacters,
                    cue.Text.Length - offset);
                int length = LargestTranslationPieceLength(
                    cue,
                    cueIndex,
                    partIndex,
                    offset,
                    maximumLength,
                    textElementBoundaries,
                    sourceLanguage,
                    targetLanguage,
                    model,
                    limits);
                if (length <= 0)
                    throw new SubtitleInputException(Strings.AiFileTooLarge);

                pieces.Add(CreateTranslationPiece(
                    cue,
                    cueIndex,
                    partIndex,
                    cue.Text.Substring(offset, length),
                    limits));
                offset += length;
                partIndex++;
            }
        }

        var batches = new List<TranslationBatch>();
        int start = 0;
        while (start < pieces.Count)
        {
            int candidateCount = 0;
            int characters = 0;
            while (start + candidateCount < pieces.Count
                   && candidateCount < limits.MaxSegments
                   && characters + pieces[start + candidateCount].Text.Length
                   <= limits.MaxCharacters)
            {
                characters += pieces[start + candidateCount].Text.Length;
                candidateCount++;
            }

            int count = LargestTranslationBatchPrefix(
                pieces,
                start,
                candidateCount,
                sourceLanguage,
                targetLanguage,
                model,
                limits);
            if (count <= 0)
                throw new SubtitleInputException(Strings.AiFileTooLarge);

            batches.Add(new TranslationBatch(pieces.GetRange(start, count).ToArray()));
            start += count;
        }
        return batches;
    }

    private static int LargestTranslationPieceLength(
        CaptionCue cue,
        int cueIndex,
        int partIndex,
        int offset,
        int maximumLength,
        int[] textElementBoundaries,
        string? sourceLanguage,
        string targetLanguage,
        AiModelId? model,
        AiCaptionTranslationLimits limits)
    {
        int low = 1;
        int high = maximumLength;
        int best = 0;
        while (low <= high)
        {
            int midpoint = low + ((high - low) / 2);
            int length = KeepTranslationBoundaryTogether(
                cue.Text,
                textElementBoundaries,
                offset,
                midpoint);
            if (length <= 0)
            {
                low = midpoint + 1;
                continue;
            }

            TranslationPiece piece = CreateTranslationPiece(
                cue,
                cueIndex,
                partIndex,
                cue.Text.Substring(offset, length),
                limits);
            if (TranslationBatchFits(
                    [piece],
                    sourceLanguage,
                    targetLanguage,
                    model,
                    limits))
            {
                best = Math.Max(best, length);
                low = midpoint + 1;
            }
            else
            {
                high = midpoint - 1;
            }
        }
        return best;
    }

    private static int LargestTranslationBatchPrefix(
        List<TranslationPiece> pieces,
        int start,
        int maximumCount,
        string? sourceLanguage,
        string targetLanguage,
        AiModelId? model,
        AiCaptionTranslationLimits limits)
    {
        int low = 1;
        int high = maximumCount;
        int best = 0;
        while (low <= high)
        {
            int count = low + ((high - low) / 2);
            if (TranslationBatchFits(
                    pieces.GetRange(start, count),
                    sourceLanguage,
                    targetLanguage,
                    model,
                    limits))
            {
                best = count;
                low = count + 1;
            }
            else
            {
                high = count - 1;
            }
        }
        return best;
    }

    private static bool TranslationBatchFits(
        IReadOnlyList<TranslationPiece> pieces,
        string? sourceLanguage,
        string targetLanguage,
        AiModelId? model,
        AiCaptionTranslationLimits limits)
    {
        try
        {
            _ = new AiCaptionTranslationRequest(
                pieces.Select(piece => new AiCaptionTranslationSegment
                {
                    Id = piece.Id,
                    Text = piece.Text,
                    Context = new AiCaptionTranslationSegmentContext(
                        piece.GroupId,
                        piece.ContextPartIndex,
                        piece.Start,
                        piece.End),
                }).ToArray(),
                targetLanguage,
                sourceLanguage,
                model: model,
                limits: limits);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static TranslationPiece CreateTranslationPiece(
        CaptionCue cue,
        int cueIndex,
        int partIndex,
        string text,
        AiCaptionTranslationLimits limits)
    {
        int contextGroup = partIndex / limits.MaxSegments;
        return new TranslationPiece(
            cueIndex,
            partIndex,
            partIndex % limits.MaxSegments,
            $"c{cueIndex}-p{partIndex}",
            contextGroup == 0 ? $"c{cueIndex}" : $"c{cueIndex}-g{contextGroup}",
            cue.Start,
            cue.End,
            text);
    }

    internal static int[] CreateTranslationTextElementBoundaries(string text)
        => StringInfo.ParseCombiningCharacters(text);

    internal static int KeepTranslationBoundaryTogether(
        string text,
        int[] textElementBoundaries,
        int offset,
        int length)
    {
        int end = offset + length;
        if (end >= text.Length || length <= 0)
            return length;

        int boundaryIndex = Array.BinarySearch(textElementBoundaries, end);
        if (boundaryIndex < 0)
            boundaryIndex = ~boundaryIndex - 1;
        if (boundaryIndex < 0 || textElementBoundaries[boundaryIndex] <= offset)
            return 0;
        int textElementBoundary = textElementBoundaries[boundaryIndex];
        int offsetBoundaryIndex = Array.BinarySearch(textElementBoundaries, offset);
        if (offsetBoundaryIndex < 0)
            throw new ArgumentException("The offset must be a text-element boundary.", nameof(offset));

        for (int candidateIndex = boundaryIndex;
             candidateIndex > offsetBoundaryIndex;
             candidateIndex--)
        {
            int boundary = textElementBoundaries[candidateIndex];
            int previousTextElement = textElementBoundaries[candidateIndex - 1];
            if (!char.IsWhiteSpace(text[previousTextElement])
                || (boundary < text.Length && char.IsWhiteSpace(text[boundary])))
            {
                continue;
            }

            int separatorIndex = candidateIndex - 1;
            while (separatorIndex > offsetBoundaryIndex
                   && char.IsWhiteSpace(text[textElementBoundaries[separatorIndex - 1]]))
            {
                separatorIndex--;
            }
            int separatorStart = textElementBoundaries[separatorIndex];
            if (separatorStart > offset)
            {
                // Keep the complete separator with the preceding word. A
                // leading separator is part of the next word for this purpose.
                return boundary - offset;
            }
        }

        int trailingTextElement = textElementBoundaries[boundaryIndex - 1];
        if (char.IsWhiteSpace(text[trailingTextElement])
            && char.IsWhiteSpace(text[textElementBoundary]))
        {
            int separatorIndex = boundaryIndex - 1;
            while (separatorIndex > offsetBoundaryIndex
                   && char.IsWhiteSpace(text[textElementBoundaries[separatorIndex - 1]]))
            {
                separatorIndex--;
            }
            int separatorStart = textElementBoundaries[separatorIndex];
            if (separatorStart > offset)
                return separatorStart - offset;
        }

        // A single word longer than the provider limit still has to make
        // progress, but it may only be split between complete text elements.
        return textElementBoundary - offset;
    }

}
