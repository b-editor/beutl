using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Beutl.Editor.Services.Captions;

namespace Beutl.ViewModels.Dialogs;

public sealed class EditableCaptionCueViewModel : INotifyPropertyChanged
{
    private int _number;
    private string _startText;
    private string _endText;
    private string _text;
    private int _caretIndex;
    private string _speaker;
    private string _language;
    private readonly CaptionMetadata _metadata;

    public EditableCaptionCueViewModel(int number, CaptionCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);

        _number = number;
        _startText = FormatTime(cue.Start);
        _endText = FormatTime(cue.End);
        _text = cue.Text;
        _caretIndex = cue.Text.Length;
        _speaker = cue.Speaker ?? string.Empty;
        _language = cue.Language ?? string.Empty;
        _metadata = cue.Metadata;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number
    {
        get => _number;
        internal set => SetField(ref _number, value);
    }

    public string StartText
    {
        get => _startText;
        set => SetField(ref _startText, value);
    }

    public string EndText
    {
        get => _endText;
        set => SetField(ref _endText, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            string normalized = value ?? string.Empty;
            if (!SetField(ref _text, normalized))
                return;

            if (_caretIndex > normalized.Length)
            {
                CaretIndex = normalized.Length;
            }
        }
    }

    public int CaretIndex
    {
        get => _caretIndex;
        set => SetField(ref _caretIndex, Math.Clamp(value, 0, Text.Length));
    }

    public string Speaker
    {
        get => _speaker;
        set => SetField(ref _speaker, value ?? string.Empty);
    }

    public string Language
    {
        get => _language;
        set => SetField(ref _language, value ?? string.Empty);
    }

    public bool TryCreateCue(out CaptionCue? cue)
    {
        cue = null;
        if (!TryParseTime(StartText, out TimeSpan start)
            || !TryParseTime(EndText, out TimeSpan end)
            || start < TimeSpan.Zero
            || end <= start)
        {
            return false;
        }

        cue = new CaptionCue(
            start,
            end,
            Text,
            NormalizeOptional(Speaker),
            NormalizeOptional(Language),
            _metadata);
        return true;
    }

    internal static string FormatTime(TimeSpan value)
    {
        long totalHours = (long)value.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");
    }

    internal static bool TryParseTime(string? value, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string input = value.Trim();
        string[] formats =
        [
            @"h\:mm\:ss\.fff",
            @"hh\:mm\:ss\.fff",
            @"h\:mm\:ss",
            @"hh\:mm\:ss",
            @"m\:ss\.fff",
            @"mm\:ss\.fff",
            @"m\:ss",
            @"mm\:ss",
        ];

        return TryParseTotalHours(input, out result)
            || TimeSpan.TryParseExact(input, formats, CultureInfo.InvariantCulture, out result)
            || TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseTotalHours(string input, out TimeSpan result)
    {
        result = default;
        int firstColon = input.IndexOf(':');
        int secondColon = firstColon < 0 ? -1 : input.IndexOf(':', firstColon + 1);
        if (firstColon <= 0 || secondColon <= firstColon + 1)
            return false;
        if (input.IndexOf(':', secondColon + 1) >= 0)
            return false;

        if (!long.TryParse(input.AsSpan(0, firstColon), NumberStyles.None, CultureInfo.InvariantCulture, out long hours)
            || !int.TryParse(
                input.AsSpan(firstColon + 1, secondColon - firstColon - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minutes)
            || !decimal.TryParse(
                input.AsSpan(secondColon + 1),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal seconds)
            || minutes is < 0 or > 59
            || seconds is < 0 or >= 60)
        {
            return false;
        }

        try
        {
            decimal ticks = checked(
                hours * (decimal)TimeSpan.TicksPerHour
                + minutes * (decimal)TimeSpan.TicksPerMinute
                + seconds * TimeSpan.TicksPerSecond);
            if (ticks > TimeSpan.MaxValue.Ticks)
                return false;

            result = TimeSpan.FromTicks(decimal.ToInt64(decimal.Round(ticks)));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string? NormalizeOptional(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record CaptionLanguageOption(string? Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
