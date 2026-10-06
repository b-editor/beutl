using System.Diagnostics;
using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliRunner
{
    internal const int MaxRetainedStandardErrorLength = 64 * 1024;

    internal const int MaxProgressRecordLength = 4 * 1024;

    private const string OmittedStandardErrorRecord =
        "[stderr record omitted because it exceeded the retention limit]";

    private const string OmittedProgressRecord =
        "[progress record omitted because it exceeded the retention limit]";

    internal static async Task<string> ReadStandardErrorAsync(
        TextReader reader,
        IProgress<string>? progress)
    {
        var retainedRecords = new Queue<string>();
        int retainedLength = 0;
        var errorRecord = new StringBuilder();
        var progressRecord = new StringBuilder();
        bool errorRecordOmitted = false;
        bool progressRecordOmitted = false;
        bool pendingCarriageReturn = false;
        var buffer = new char[256];

        void CompleteErrorRecord(string delimiter)
        {
            string retainedContent = errorRecordOmitted
                ? OmittedStandardErrorRecord
                : GitDiagnosticSanitizer.RedactCredentials(errorRecord.ToString());
            if (retainedContent.Length + delimiter.Length
                > MaxRetainedStandardErrorLength)
            {
                retainedContent = OmittedStandardErrorRecord;
                retainedRecords.Clear();
                retainedLength = 0;
            }

            RetainCompleteStandardErrorRecord(
                retainedRecords,
                ref retainedLength,
                retainedContent + delimiter);
            errorRecord.Clear();
            errorRecordOmitted = false;
        }

        void CompleteProgressRecord()
        {
            if (progress is not null
                && !progressRecordOmitted
                && progressRecord.Length > 0)
            {
                progress.Report(GitDiagnosticSanitizer.RedactCredentials(
                    progressRecord.ToString()));
            }

            progressRecord.Clear();
            progressRecordOmitted = false;
        }

        void AppendRecordCharacter(char value)
        {
            if (!errorRecordOmitted)
            {
                if (errorRecord.Length == MaxRetainedStandardErrorLength)
                {
                    // Never retain a suffix cut out of a larger raw record: the removed prefix may
                    // contain the URL scheme that the sanitizer needs in order to recognize
                    // credentials in the retained suffix.
                    errorRecord.Clear();
                    errorRecordOmitted = true;
                    retainedRecords.Clear();
                    retainedLength = 0;
                }
                else
                {
                    errorRecord.Append(value);
                }
            }

            if (progress is not null && !progressRecordOmitted)
            {
                if (progressRecord.Length == MaxProgressRecordLength)
                {
                    progressRecord.Clear();
                    progressRecordOmitted = true;
                    progress.Report(OmittedProgressRecord);
                }
                else
                {
                    progressRecord.Append(value);
                }
            }
        }

        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < count; i++)
            {
                char value = buffer[i];
                if (pendingCarriageReturn)
                {
                    if (value == '\n')
                    {
                        CompleteErrorRecord("\r\n");
                        pendingCarriageReturn = false;
                        continue;
                    }

                    CompleteErrorRecord("\r");
                    pendingCarriageReturn = false;
                }

                if (value == '\r')
                {
                    CompleteProgressRecord();
                    pendingCarriageReturn = true;
                }
                else if (value == '\n')
                {
                    CompleteErrorRecord("\n");
                    CompleteProgressRecord();
                }
                else
                {
                    AppendRecordCharacter(value);
                }
            }
        }

        if (pendingCarriageReturn)
        {
            CompleteErrorRecord("\r");
        }

        if (errorRecordOmitted || errorRecord.Length > 0)
        {
            CompleteErrorRecord(string.Empty);
            CompleteProgressRecord();
        }

        return string.Concat(retainedRecords);
    }

    private static void RetainCompleteStandardErrorRecord(
        Queue<string> records,
        ref int retainedLength,
        string record)
    {
        Debug.Assert(record.Length <= MaxRetainedStandardErrorLength);

        while (retainedLength + record.Length > MaxRetainedStandardErrorLength
               && records.TryDequeue(out string? removed))
        {
            retainedLength -= removed.Length;
        }

        records.Enqueue(record);
        retainedLength += record.Length;
    }

    internal static async Task<(string Output, bool Truncated)> ReadStandardOutputAsync(
        Stream stream,
        int? maxBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (maxBytes is null)
        {
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            return (await reader.ReadToEndAsync().ConfigureAwait(false), false);
        }

        (byte[] captured, int capturedCount, bool truncated) =
            await CaptureBoundedAsync(stream, maxBytes.Value).ConfigureAwait(false);
        int completeByteCount = GetCompleteUtf8PrefixLength(
            captured.AsSpan(0, capturedCount));
        return (
            Encoding.UTF8.GetString(captured, 0, completeByteCount),
            truncated);
    }

    private static async Task<(string Output, byte[]? OutputBytes, bool Truncated)>
        CaptureStandardOutputAsync(
            Stream stream,
            int? maxBytes,
            bool captureBytes)
    {
        if (!captureBytes)
        {
            (string output, bool truncated) = await ReadStandardOutputAsync(stream, maxBytes)
                .ConfigureAwait(false);
            return (output, null, truncated);
        }

        (byte[] outputBytes, bool outputTruncated) =
            await ReadStandardOutputBytesAsync(stream, maxBytes).ConfigureAwait(false);
        return (Encoding.UTF8.GetString(outputBytes), outputBytes, outputTruncated);
    }

    internal static async Task<(byte[] Output, bool Truncated)> ReadStandardOutputBytesAsync(
        Stream stream,
        int? maxBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (maxBytes is null)
        {
            using var output = new MemoryStream();
            await stream.CopyToAsync(output).ConfigureAwait(false);
            return (output.ToArray(), false);
        }

        (byte[] captured, int capturedCount, bool truncated) =
            await CaptureBoundedAsync(stream, maxBytes.Value).ConfigureAwait(false);
        if (capturedCount != captured.Length)
        {
            Array.Resize(ref captured, capturedCount);
        }

        return (captured, truncated);
    }

    // Reads the stream to its end and keeps its first limit bytes.
    private static async Task<(byte[] Captured, int Count, bool Truncated)> CaptureBoundedAsync(
        Stream stream,
        int limit)
    {
        var captured = new byte[limit];
        var buffer = new byte[8192];
        int capturedCount = 0;
        bool truncated = false;
        int count;
        while ((count = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            int copyCount = Math.Min(count, limit - capturedCount);
            if (copyCount > 0)
            {
                buffer.AsSpan(0, copyCount).CopyTo(captured.AsSpan(capturedCount));
                capturedCount += copyCount;
            }

            truncated |= copyCount < count;
        }

        return (captured, capturedCount, truncated);
    }

    private static int GetCompleteUtf8PrefixLength(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        int sequenceStart = bytes.Length - 1;
        while (sequenceStart > 0 && (bytes[sequenceStart] & 0xC0) == 0x80)
        {
            sequenceStart--;
        }

        int sequenceLength = bytes[sequenceStart] switch
        {
            < 0x80 => 1,
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 1,
        };
        return bytes.Length - sequenceStart < sequenceLength
            ? sequenceStart
            : bytes.Length;
    }
}
