using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.ProjectSystem;

public partial class SceneMarker : Hierarchical
{
    public SceneMarker()
    {
        ScanProperties<SceneMarker>();
    }

    public SceneMarker(MediaTime time, string? name = null, Color? color = null, string? note = null)
        : this()
    {
        Time.Value = time;
        Name.Value = name ?? string.Empty;
        Color.Value = color ?? Colors.Yellow;
        Note.Value = note ?? string.Empty;
    }

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public IProperty<MediaTime> Time { get; } = new ValueOnlyProperty<MediaTime>(MediaTime.Zero)
    {
        Coerce = static (_, value) => value < MediaTime.Zero ? MediaTime.Zero : value
    };

    public IProperty<Color> Color { get; } = Property.CreateValue(Colors.Yellow);

    public IProperty<string> Note { get; } = new ValueOnlyProperty<string>(string.Empty)
    {
        Coerce = static (_, value) => value ?? string.Empty
    };
}
