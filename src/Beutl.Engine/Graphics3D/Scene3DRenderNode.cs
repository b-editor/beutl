using System;
using System.Collections.Generic;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Media;

namespace Beutl.Graphics3D;

/// <summary>
/// Render node for 3D scene rendering.
/// </summary>
internal sealed class Scene3DRenderNode : RenderNode
{
    private Scene3D.Resource? _resource;
    private List<Object3D>? _objects;
    private List<Light3D>? _lights;

    public Scene3DRenderNode(
        Scene3D.Resource resource,
        IEnumerable<Object3D> objects,
        IEnumerable<Light3D> lights)
    {
        Update(resource, objects, lights);
    }

    public Rect Bounds { get; private set; }

    public bool Update(
        Scene3D.Resource resource,
        IEnumerable<Object3D> objects,
        IEnumerable<Light3D> lights)
    {
        bool changed = false;

        if (_resource != resource)
        {
            _resource = resource;
            changed = true;
        }

        _objects = new List<Object3D>(objects);
        _lights = new List<Light3D>(lights);

        Bounds = new Rect(0, 0, resource.RenderWidth, resource.RenderHeight);
        HasChanges = changed;

        return changed;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (_resource == null)
            return [];

        var graphicsContext = GraphicsContextFactory.SharedContext;
        if (graphicsContext == null || !graphicsContext.Supports3DRendering)
            return [];

        // Camera is already a Resource from the source generator
        var cameraResource = _resource.Camera;
        if (cameraResource == null)
            return [];

        int width = (int)_resource.RenderWidth;
        int height = (int)_resource.RenderHeight;

        if (width <= 0 || height <= 0)
            return [];

        // Get or create renderer
        var renderer = _resource.Renderer ??= graphicsContext.Create3DRenderer();

        // Initialize or resize if needed
        if (renderer.Width != width || renderer.Height != height)
        {
            if (renderer.Width == 0 || renderer.Height == 0)
            {
                renderer.Initialize(width, height);
            }

            renderer.Resize(width, height);
        }

        // Prepare object resources
        var objectResources = new List<Object3D.Resource>();
        if (_objects != null)
        {
            foreach (var obj in _objects)
            {
                if (!obj.IsEnabled)
                    continue;

                // Create resource for the Object3D
                var objResource = (Object3D.Resource)obj.ToResource(new RenderContext(TimeSpan.Zero));
                objectResources.Add(objResource);
            }
        }

        // Prepare light resources
        var lightResources = new List<Light3D.Resource>();
        if (_lights != null)
        {
            foreach (var light in _lights)
            {
                if (!light.IsEnabled)
                    continue;

                // Create resource for the Light3D
                var lightResource = (Light3D.Resource)light.ToResource(new RenderContext(TimeSpan.Zero));
                lightResources.Add(lightResource);
            }
        }

        // Render
        renderer.Render(
            cameraResource,
            objectResources,
            lightResources,
            _resource.BackgroundColor,
            _resource.AmbientColor,
            _resource.AmbientIntensity);

        // Get the rendered surface
        var surface = renderer.CreateSkiaSurface();
        if (surface == null)
            return [];

        // Create the render operation that will draw the 3D scene
        var operation = RenderNodeOperation.CreateFromSurface(
            Bounds,
            new Point(0, 0),
            surface);

        return [operation];
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _resource = null;
        _objects?.Clear();
        _objects = null;
        _lights?.Clear();
        _lights = null;
    }
}
