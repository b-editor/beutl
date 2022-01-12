using System.ComponentModel;

using BEditorNext.Framework.Service;
using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

namespace BEditorNext.Operations;

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
    public static readonly CoreProperty<RelativePoint> RelativePointProperty;
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
        BooleanProperty = ConfigureProperty<bool, TestOperation>("Boolean")
            .DefaultValue(false)
            .Animatable()
            .JsonName("boolean")
            .EnableEditor()
            .Register();

        NumberProperty = ConfigureProperty<float, TestOperation>("Number")
            .DefaultValue(0)
            .Animatable()
            .JsonName("number")
            .EnableEditor()
            .Register();

        ColorProperty = ConfigureProperty<Color, TestOperation>("Color")
            .DefaultValue(Colors.White)
            .Animatable()
            .JsonName("color")
            .EnableEditor()
            .Register();

        PixelPointProperty = ConfigureProperty<PixelPoint, TestOperation>("PixelPoint")
            .DefaultValue(PixelPoint.Origin)
            .Animatable()
            .JsonName("pixelPoint")
            .EnableEditor()
            .Register();

        PixelRectProperty = ConfigureProperty<PixelRect, TestOperation>("PixelRect")
            .DefaultValue(PixelRect.Empty)
            .Animatable()
            .JsonName("pixelRect")
            .EnableEditor()
            .Register();

        PixelSizeProperty = ConfigureProperty<PixelSize, TestOperation>("PixelSize")
            .DefaultValue(PixelSize.Empty)
            .Animatable()
            .JsonName("pixelSize")
            .EnableEditor()
            .Register();

        PointProperty = ConfigureProperty<Point, TestOperation>("Point")
            .DefaultValue(new Point())
            .Animatable()
            .JsonName("point")
            .EnableEditor()
            .Register();

        RectProperty = ConfigureProperty<Rect, TestOperation>("Rect")
            .DefaultValue(Rect.Empty)
            .Animatable()
            .JsonName("rect")
            .EnableEditor()
            .Register();

        SizeProperty = ConfigureProperty<Size, TestOperation>("Size")
            .DefaultValue(new Size(100, 100))
            .Animatable()
            .JsonName("size")
            .EnableEditor()
            .Register();

        RelativePointProperty = ConfigureProperty<RelativePoint, TestOperation>("RelativePoint")
            .DefaultValue(new RelativePoint(100, 100, RelativeUnit.Absolute))
            .JsonName("relpt")
            .EnableEditor()
            .Register();

        ThicknessProperty = ConfigureProperty<Thickness, TestOperation>("Thickness")
            .DefaultValue(new Thickness())
            .Animatable()
            .JsonName("thickness")
            .EnableEditor()
            .Register();

        AsisProperty = ConfigureProperty<Asis, TestOperation>("Asis")
            .DefaultValue(Asis.X)
            .JsonName("asis")
            .EnableEditor()
            .Register();

        StringProperty = ConfigureProperty<string, TestOperation>("String")
            .DefaultValue("")
            .JsonName("string")
            .EnableEditor()
            .Register();

        FileInfoProperty = ConfigureProperty<FileInfo?, TestOperation>("FileInfo")
            .DefaultValue(null)
            .JsonName("file")
            .EnableEditor()
            .Register();
    }

    public TestOperation()
    {
        _service = ServiceLocator.Current.GetRequiredService<INotificationService>();

        Setters.FirstOrDefault(i => i.Property == BooleanProperty)?.GetObservable().Subscribe(_ =>
        {
            _service.Show(
                new Notification("Change Boolean", "Booleanが変更された", (NotificationType)Random.Shared.Next(0, 4)));
        });
    }

    public override void Render(in OperationRenderArgs args)
    {
    }

    public enum Asis
    {
        [Description("WidthString")]
        X,
        [Description("HeightString")]
        Y
    }
}
