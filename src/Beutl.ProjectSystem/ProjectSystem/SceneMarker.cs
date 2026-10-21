using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.ProjectSystem;

public class SceneMarker : Hierarchical
{
    public SceneMarker()
    {
        ScanProperties<SceneMarker>();
        TimeProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Time)));
        ColorProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Color)));
        NoteProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Note)));
    }

    public SceneMarker(TimeSpan time, string? name = null, Color? color = null, string? note = null)
        : this()
    {
        Time = time;
        Name = name ?? string.Empty;
        Color = color ?? Colors.Yellow;
        Note = note ?? string.Empty;
    }

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public IProperty<TimeSpan> TimeProperty { get; } = new ValueOnlyProperty<TimeSpan>(TimeSpan.Zero)
    {
        Coerce = value => value < TimeSpan.Zero ? TimeSpan.Zero : value
    };

    public IProperty<Color> ColorProperty { get; } = new ValueOnlyProperty<Color>(Colors.Yellow);

    public IProperty<string> NoteProperty { get; } = new ValueOnlyProperty<string>(string.Empty)
    {
        Coerce = value => value ?? string.Empty
    };

    public TimeSpan Time
    {
        get => TimeProperty.Value;
        set => TimeProperty.Value = value;
    }

    public Color Color
    {
        get => ColorProperty.Value;
        set => ColorProperty.Value = value;
    }

    public string Note
    {
        get => NoteProperty.Value;
        set => NoteProperty.Value = value;
    }
}
