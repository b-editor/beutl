using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SkiaSharp;

namespace Beutl.Graphics3D;

public class Scene3D : Drawable
{
    public static readonly CoreProperty<int> WidthProperty;
    public static readonly CoreProperty<int> HeightProperty;
    public static readonly CoreProperty<Camera?> CameraProperty;
    public static readonly CoreProperty<Drawables3D> ChildrenProperty;
    private int _width = -1;
    private int _height = -1;
    private Camera? _camera;
    private readonly Drawables3D _children = [];
    private SceneNode? _root;

    static Scene3D()
    {
        WidthProperty = ConfigureProperty<int, Scene3D>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .DefaultValue(-1)
            .Register();

        HeightProperty = ConfigureProperty<int, Scene3D>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .DefaultValue(-1)
            .Register();

        CameraProperty = ConfigureProperty<Camera?, Scene3D>(nameof(Camera))
            .Accessor(o => o.Camera, (o, v) => o.Camera = v)
            .Register();

        ChildrenProperty = ConfigureProperty<Drawables3D, Scene3D>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();

        AffectsRender<Scene3D>(
            WidthProperty, HeightProperty, CameraProperty);
    }

    public Scene3D()
    {
        Camera = new PerspectiveCamera { AspectRatio = 1 };
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
        _children.Attached += HierarchicalChildren.Add;
        _children.Detached += item => HierarchicalChildren.Remove(item);
    }

    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(-1, int.MaxValue)]
    public int Width
    {
        get => _width;
        set => SetAndRaise(WidthProperty, ref _width, value);
    }

    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
    [Range(-1, int.MaxValue)]
    public int Height
    {
        get => _height;
        set => SetAndRaise(HeightProperty, ref _height, value);
    }

    public Camera? Camera
    {
        get => _camera;
        set => SetAndRaise(CameraProperty, ref _camera, value);
    }

    [NotAutoSerialized]
    public Drawables3D Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<Drawables3D>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        Camera?.ApplyAnimations(clock);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (Width > 0 && Height > 0)
        {
            return new Size(Width, Height);
        }

        return availableSize;
    }

    protected override void OnDraw(GraphicsContext2D canvas)
    {
        if (_root?.IsDisposed == true)
        {
            _root = null;
        }

        var size = canvas.Size;
        if (Width > 0 && Height > 0)
        {
            size = new PixelSize(Width, Height);
        }

        switch (Camera)
        {
            case PerspectiveCamera persp:
                persp.AspectRatio = size.Width / (float)size.Height;
                break;
            case OrthographicCamera ortho:
                ortho.Width = size.Width;
                ortho.Height = size.Height;
                break;

            default:
                Camera = new PerspectiveCamera { AspectRatio = size.Width / (float)size.Height };
                break;
        }

        _root ??= new SceneNode(new Rect(size.ToSize(1)), this);
        canvas.DrawNode(_root);
    }

    private class SceneNode(Rect bounds, Scene3D scene) : RenderNode, IEquatable<SceneNode?>
    {
        private Rect Bounds => bounds;

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            context.IsRenderCacheEnabled = false;
            return [RenderNodeOperation.CreateLambda(bounds, Render, HitTest)];
        }

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
        }

        private bool HitTest(Point point)
        {
            return bounds.ContainsExclusive(point);
        }

        private unsafe void Render(ImmediateCanvas canvas)
        {
        }

        public bool Equals(SceneNode? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return bounds.Equals(other.Bounds);
        }
    }
}
