using System.ComponentModel;

using BeUtl.Framework.Service;
using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Operations;

internal class TestOperation : LayerOperation
{
    public static readonly CoreProperty<bool> BooleanProperty;
    public static readonly CoreProperty<float> NumberProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<PixelPoint> PixelPointProperty;
    public static readonly CoreProperty<PixelRect> PixelRectProperty;
    public static readonly CoreProperty<PixelSize> PixelSizeProperty;
    public static readonly CoreProperty<Point> PointProperty;
    public static readonly CoreProperty<Rect> RectProperty;
    public static readonly CoreProperty<Size> SizeProperty;
    public static readonly CoreProperty<Thickness> ThicknessProperty;
    public static readonly CoreProperty<Asis> AsisProperty;
    public static readonly CoreProperty<string> StringProperty;
    public static readonly CoreProperty<FileInfo?> FileInfoProperty;

    //public static readonly PropertyDefine<Vector2> Vector2Property;
    //public static readonly PropertyDefine<Vector3> Vector3Property;
    //public static readonly PropertyDefine<Vector4> Vector4Property;
    private readonly INotificationService _service;

    static TestOperation()
    {
        static OperationPropertyMetadata<T> Metadata<T>(string serializeName)
        {
            return new OperationPropertyMetadata<T>()
            {
                IsAnimatable = true,
                SerializeName = serializeName,
                PropertyFlags = PropertyFlags.Designable
            };
        }

        BooleanProperty = ConfigureProperty<bool, TestOperation>("Boolean")
            .DefaultValue(false)
            .OverrideMetadata(Metadata<bool>("boolean"))
            .Register();

        NumberProperty = ConfigureProperty<float, TestOperation>("Number")
            .DefaultValue(0)
            .OverrideMetadata(Metadata<float>("number"))
            .Register();

        ColorProperty = ConfigureProperty<Color, TestOperation>("Color")
            .DefaultValue(Colors.White)
            .OverrideMetadata(Metadata<Color>("color"))
            .Register();

        PixelPointProperty = ConfigureProperty<PixelPoint, TestOperation>("PixelPoint")
            .DefaultValue(PixelPoint.Origin)
            .OverrideMetadata(Metadata<PixelPoint>("pixelPoint"))
            .Register();

        PixelRectProperty = ConfigureProperty<PixelRect, TestOperation>("PixelRect")
            .DefaultValue(PixelRect.Empty)
            .OverrideMetadata(Metadata<PixelRect>("pixelRect"))
            .Register();

        PixelSizeProperty = ConfigureProperty<PixelSize, TestOperation>("PixelSize")
            .DefaultValue(PixelSize.Empty)
            .OverrideMetadata(Metadata<PixelSize>("pixelSize"))
            .Register();

        PointProperty = ConfigureProperty<Point, TestOperation>("Point")
            .DefaultValue(new Point())
            .OverrideMetadata(Metadata<Point>("point"))
            .Register();

        RectProperty = ConfigureProperty<Rect, TestOperation>("Rect")
            .DefaultValue(Rect.Empty)
            .OverrideMetadata(Metadata<Rect>("rect"))
            .Register();

        SizeProperty = ConfigureProperty<Size, TestOperation>("Size")
            .DefaultValue(new Size(100, 100))
            .OverrideMetadata(Metadata<Size>("size"))
            .Register();

        ThicknessProperty = ConfigureProperty<Thickness, TestOperation>("Thickness")
            .DefaultValue(new Thickness())
            .OverrideMetadata(Metadata<Thickness>("thickness"))
            .Register();

        AsisProperty = ConfigureProperty<Asis, TestOperation>("Asis")
            .DefaultValue(Asis.X)
            .OverrideMetadata(Metadata<Asis>("asis"))
            .Register();

        StringProperty = ConfigureProperty<string, TestOperation>("String")
            .DefaultValue("")
            .OverrideMetadata(Metadata<string>("string"))
            .Register();

        FileInfoProperty = ConfigureProperty<FileInfo?, TestOperation>("FileInfo")
            .DefaultValue(null)
            .OverrideMetadata(Metadata<FileInfo?>("file"))
            .Register();
    }

    public TestOperation()
    {
        _service = ServiceLocator.Current.GetRequiredService<INotificationService>();

        Properties.FirstOrDefault(i => i.Property == BooleanProperty)?.GetObservable().Subscribe(_ =>
        {
            _service.Show(
                new Notification("Change Boolean", "Booleanが変更された", (NotificationType)Random.Shared.Next(0, 4)));
        });
    }

    public enum Asis
    {
        [Description("S.Common.Width")]
        X,
        [Description("S.Common.Height")]
        Y
    }
}
