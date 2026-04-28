using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.ProjectSystem;

public class SceneMarker : Hierarchical
{
    public static readonly CoreProperty<TimeSpan> TimeProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<string> NoteProperty;

    static SceneMarker()
    {
        TimeProperty = ConfigureProperty<TimeSpan, SceneMarker>(nameof(Time))
            .Accessor(o => o.Time, (o, v) => o.Time = v)
            .DefaultValue(TimeSpan.Zero)
            .Register();

        ColorProperty = ConfigureProperty<Color, SceneMarker>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Yellow)
            .Register();

        NoteProperty = ConfigureProperty<string, SceneMarker>(nameof(Note))
            .Accessor(o => o.Note, (o, v) => o.Note = v)
            .DefaultValue(string.Empty)
            .Register();
    }

    public SceneMarker()
    {
        Name = string.Empty;
        Note = string.Empty;
        Color = Colors.Yellow;
    }

    public SceneMarker(TimeSpan time, string? name = null, Color? color = null, string? note = null)
    {
        Time = time;
        Name = name ?? string.Empty;
        Color = color ?? Colors.Yellow;
        Note = note ?? string.Empty;
    }

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public TimeSpan Time
    {
        get;
        set
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;

            SetAndRaise(TimeProperty, ref field, value);
        }
    }

    public Color Color
    {
        get;
        set => SetAndRaise(ColorProperty, ref field, value);
    } = Colors.Yellow;

    public string Note
    {
        get;
        set => SetAndRaise(NoteProperty, ref field, value ?? string.Empty);
    } = string.Empty;
}
