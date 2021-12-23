using System.ComponentModel;

using BEditorNext.Framework.Service;
using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

namespace BEditorNext.Operations;

internal class TestOperation : RenderOperation
{
    public static readonly PropertyDefine<bool> BooleanProperty;
    public static readonly PropertyDefine<float> NumberProperty;
    public static readonly PropertyDefine<Color> ColorProperty;
    public static readonly PropertyDefine<PixelPoint> PixelPointProperty;
    public static readonly PropertyDefine<PixelRect> PixelRectProperty;
    public static readonly PropertyDefine<PixelSize> PixelSizeProperty;
    public static readonly PropertyDefine<Point> PointProperty;
    public static readonly PropertyDefine<Rect> RectProperty;
    public static readonly PropertyDefine<Size> SizeProperty;
    public static readonly PropertyDefine<Thickness> ThicknessProperty;
    public static readonly PropertyDefine<Asis> AsisProperty;
    public static readonly PropertyDefine<string> StringProperty;
    public static readonly PropertyDefine<FileInfo?> FileInfoProperty;

    //public static readonly PropertyDefine<Vector2> Vector2Property;
    //public static readonly PropertyDefine<Vector3> Vector3Property;
    //public static readonly PropertyDefine<Vector4> Vector4Property;
    private readonly INotificationService _service;

    static TestOperation()
    {
        BooleanProperty = RegisterProperty<bool, TestOperation>("Boolean")
            .DefaultValue(false)
            .Animatable()
            .JsonName("boolean")
            .EnableEditor();

        NumberProperty = RegisterProperty<float, TestOperation>("Number")
            .DefaultValue(0)
            .Animatable()
            .JsonName("number")
            .EnableEditor();

        ColorProperty = RegisterProperty<Color, TestOperation>("Color")
            .DefaultValue(Colors.White)
            .Animatable()
            .JsonName("color")
            .EnableEditor();

        PixelPointProperty = RegisterProperty<PixelPoint, TestOperation>("PixelPoint")
            .DefaultValue(PixelPoint.Origin)
            .Animatable()
            .JsonName("pixelPoint")
            .EnableEditor();

        PixelRectProperty = RegisterProperty<PixelRect, TestOperation>("PixelRect")
            .DefaultValue(PixelRect.Empty)
            .Animatable()
            .JsonName("pixelRect")
            .EnableEditor();

        PixelSizeProperty = RegisterProperty<PixelSize, TestOperation>("PixelSize")
            .DefaultValue(PixelSize.Empty)
            .Animatable()
            .JsonName("pixelSize")
            .EnableEditor();

        PointProperty = RegisterProperty<Point, TestOperation>("Point")
            .DefaultValue(new Point())
            .Animatable()
            .JsonName("point")
            .EnableEditor();

        RectProperty = RegisterProperty<Rect, TestOperation>("Rect")
            .DefaultValue(Rect.Empty)
            .Animatable()
            .JsonName("rect")
            .EnableEditor();

        SizeProperty = RegisterProperty<Size, TestOperation>("Size")
            .DefaultValue(new Size(100, 100))
            .Animatable()
            .JsonName("size")
            .EnableEditor();

        ThicknessProperty = RegisterProperty<Thickness, TestOperation>("Thickness")
            .DefaultValue(new Thickness())
            .Animatable()
            .JsonName("thickness")
            .EnableEditor();

        AsisProperty = RegisterProperty<Asis, TestOperation>("Asis")
            .DefaultValue(Asis.X)
            .JsonName("asis")
            .EnableEditor();

        StringProperty = RegisterProperty<string, TestOperation>("String")
            .DefaultValue("")
            .JsonName("string")
            .EnableEditor();

        FileInfoProperty = RegisterProperty<FileInfo?, TestOperation>("FileInfo")
            .DefaultValue(null)
            .JsonName("file")
            .EnableEditor();
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
