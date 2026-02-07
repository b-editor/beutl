using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D;

/// <summary>
/// A Drawable that renders a 3D scene.
/// </summary>
[Display(Name = nameof(Strings.Scene3D), ResourceType = typeof(Strings))]
public partial class Scene3D : Drawable
{
    public Scene3D()
    {
        ScanProperties<Scene3D>();
    }

    /// <summary>
    /// Gets the camera for this scene.
    /// </summary>
    [Display(Name = nameof(Strings.Camera), ResourceType = typeof(Strings))]
    public IProperty<Camera3D?> Camera { get; } = Property.Create<Camera3D?>();

    /// <summary>
    /// Gets the 3D objects in this scene.
    /// </summary>
    [Display(Name = nameof(Strings.Objects), ResourceType = typeof(Strings))]
    public IListProperty<Object3D> Objects { get; } = Property.CreateList<Object3D>();

    /// <summary>
    /// Gets the lights in this scene.
    /// </summary>
    [Display(Name = nameof(Strings.Lights), ResourceType = typeof(Strings))]
    public IListProperty<Light3D> Lights { get; } = Property.CreateList<Light3D>();

    /// <summary>
    /// Gets the ambient color of the scene.
    /// </summary>
    [Display(Name = nameof(Strings.AmbientColor), ResourceType = typeof(Strings))]
    public IProperty<Color> AmbientColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the ambient light intensity.
    /// </summary>
    [Display(Name = nameof(Strings.AmbientIntensity), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> AmbientIntensity { get; } = Property.CreateAnimatable(0.1f);

    /// <summary>
    /// Gets the width of the 3D render target.
    /// </summary>
    [Display(Name = nameof(Strings.RenderWidth), ResourceType = typeof(Strings))]
    [Range(1f, 8192f)]
    public IProperty<float> RenderWidth { get; } = Property.CreateAnimatable(1920f);

    /// <summary>
    /// Gets the height of the 3D render target.
    /// </summary>
    [Display(Name = nameof(Strings.RenderHeight), ResourceType = typeof(Strings))]
    [Range(1f, 8192f)]
    public IProperty<float> RenderHeight { get; } = Property.CreateAnimatable(1080f);

    /// <summary>
    /// Gets the background color of the 3D scene.
    /// </summary>
    [Display(Name = nameof(Strings.BackgroundColor), ResourceType = typeof(Strings))]
    public IProperty<Color> BackgroundColor { get; } = Property.CreateAnimatable(Colors.Black);

    /// <summary>
    /// Gets the target object ID for gizmo visualization.
    /// </summary>
    public IProperty<Guid?> GizmoTarget { get; } = Property.Create<Guid?>();

    /// <summary>
    /// Gets the gizmo visualization mode.
    /// </summary>
    public IProperty<GizmoMode> GizmoMode { get; } = Property.Create(Gizmo.GizmoMode.None);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var scene3DResource = (Resource)resource;
        return new Size(scene3DResource.RenderWidth, scene3DResource.RenderHeight);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var scene3DResource = (Resource)resource;

        if (scene3DResource.Camera == null)
            return;

        // Use DrawNode to add our custom render node
        context.DrawNode<Scene3DRenderNode, Resource>(
            scene3DResource,
            static res => new Scene3DRenderNode(res),
            static (node, res) => node.Update(res));
    }

    public partial class Resource
    {
        internal IRenderer3D? Renderer { get; set; }

        public TimeSpan Time { get; set; } = TimeSpan.Zero;

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Renderer?.Dispose();
                Renderer = null;
            }
        }

        partial void PostUpdate(Scene3D _, RenderContext context)
        {
            if (Time != context.Time)
            {
                Time = context.Time;
                Version++;
            }
        }
    }
}
