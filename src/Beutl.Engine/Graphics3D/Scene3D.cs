using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D;

/// <summary>
/// A Drawable that renders a 3D scene.
/// </summary>
public partial class Scene3D : Drawable
{
    private readonly CoreList<Object3D> _objects = [];
    private readonly CoreList<Light3D> _lights = [];

    public Scene3D()
    {
        ScanProperties<Scene3D>();
    }

    /// <summary>
    /// Gets the camera for this scene.
    /// </summary>
    public IProperty<Camera3D?> Camera { get; } = Property.Create<Camera3D?>();

    /// <summary>
    /// Gets the 3D objects in this scene.
    /// </summary>
    public CoreList<Object3D> Objects => _objects;

    /// <summary>
    /// Gets the lights in this scene.
    /// </summary>
    public CoreList<Light3D> Lights => _lights;

    /// <summary>
    /// Gets the ambient color of the scene.
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> AmbientColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the ambient light intensity.
    /// </summary>
    [Range(0f, 1f)]
    public IProperty<float> AmbientIntensity { get; } = Property.CreateAnimatable(0.1f);

    /// <summary>
    /// Gets the width of the 3D render target.
    /// </summary>
    [Range(1f, 8192f)]
    public IProperty<float> RenderWidth { get; } = Property.CreateAnimatable(1920f);

    /// <summary>
    /// Gets the height of the 3D render target.
    /// </summary>
    [Range(1f, 8192f)]
    public IProperty<float> RenderHeight { get; } = Property.CreateAnimatable(1080f);

    /// <summary>
    /// Gets the background color of the 3D scene.
    /// </summary>
    public IProperty<Color> BackgroundColor { get; } = Property.CreateAnimatable(Colors.Black);

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
        context.DrawNode<Scene3DRenderNode, (Resource, CoreList<Object3D>, CoreList<Light3D>)>(
            (scene3DResource, _objects, _lights),
            static parameters => new Scene3DRenderNode(parameters.Item1, parameters.Item2, parameters.Item3),
            static (node, parameters) => node.Update(parameters.Item1, parameters.Item2, parameters.Item3));
    }

    public partial class Resource
    {
        internal Vulkan3DRenderer? Renderer { get; set; }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Renderer?.Dispose();
                Renderer = null;
            }
        }
    }
}
