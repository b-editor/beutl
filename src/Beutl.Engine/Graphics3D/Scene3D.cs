using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Collections.Pooled;
using Beutl.Editor;
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
public partial class Scene3D : Drawable, IFlowOperator
{
    public Scene3D()
    {
        ScanProperties<Scene3D>();
        HideProperty(GizmoMode);
        HideProperty(GizmoTarget);
        Camera.CurrentValue = new PerspectiveCamera();
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
    [SuppressResourceClassGeneration]
    public IListProperty<Object3D> Objects { get; } = Property.CreateList<Object3D>();

    /// <summary>
    /// Gets the lights in this scene.
    /// </summary>
    [Display(Name = nameof(Strings.Lights), ResourceType = typeof(Strings))]
    [SuppressResourceClassGeneration]
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
        private readonly PooledList<int> _lightsVersion = [];
        private readonly PooledList<int> _objectsVersion = [];

        internal IRenderer3D? Renderer { get; set; }

        public TimeSpan Time { get; set; } = TimeSpan.Zero;

        public List<Light3D.Resource> Lights { get; set; } = [];

        public List<Object3D.Resource> Objects { get; set; } = [];

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Renderer?.Dispose();
                Renderer = null;
                for (int i = _lightsVersion.Count; i < Lights.Count; i++)
                {
                    Lights[i].Dispose();
                }

                Lights.Clear();
                _lightsVersion.Dispose();

                for (int i = _objectsVersion.Count; i < Objects.Count; i++)
                {
                    Objects[i].Dispose();
                }

                Objects.Clear();
                _objectsVersion.Dispose();
            }
        }

        partial void PostUpdate(Scene3D obj, RenderContext context)
        {
            bool changed = false;
            if (Time != context.Time)
            {
                Time = context.Time;
                changed = true;
            }

            // Consume lights and objects from flow
            using var consumedLights = new PooledList<Light3D.Resource>();
            using var consumedObjects = new PooledList<Object3D.Resource>();
            if (context is ICompositionRenderContext ctx)
            {
                for (int i = ctx.Flow.Count - 1; i >= 0; i--)
                {
                    switch (ctx.Flow[i])
                    {
                        case Light3D.Resource light:
                            ctx.Flow.RemoveAt(i);
                            consumedLights.Insert(0, light);
                            break;
                        case Object3D.Resource obj3d:
                            ctx.Flow.RemoveAt(i);
                            consumedObjects.Insert(0, obj3d);
                            break;
                    }
                }
            }

            ResourceReconciler.ReconcileListFromFlow(
                context: context,
                property: obj.Lights,
                consumed: consumedLights,
                field: Lights,
                versions: _lightsVersion,
                changed: ref changed);
            ResourceReconciler.ReconcileListFromFlow(
                context: context,
                property: obj.Objects,
                consumed: consumedObjects,
                field: Objects,
                versions: _objectsVersion,
                changed: ref changed);

            if (changed)
                Version++;
        }
    }
}
