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

    public SceneMarker(MediaTime time, string? name = null, Color? color = null, string? note = null)
        : this()
    {
        Time = time;
        Name = name ?? string.Empty;
        Color = color ?? Colors.Yellow;
        Note = note ?? string.Empty;
    }

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public IProperty<MediaTime> TimeProperty { get; } = new ValueOnlyProperty<MediaTime>(MediaTime.Zero)
    {
        Coerce = static (_, value) => value < MediaTime.Zero ? MediaTime.Zero : value
    };

    public IProperty<Color> ColorProperty { get; } = new ValueOnlyProperty<Color>(Colors.Yellow);

    public IProperty<string> NoteProperty { get; } = new ValueOnlyProperty<string>(string.Empty)
    {
        Coerce = static (_, value) => value ?? string.Empty
    };

    public MediaTime Time
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
