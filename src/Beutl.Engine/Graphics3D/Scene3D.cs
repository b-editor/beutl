using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.Graphics3D;

/// <summary>
/// A Drawable that renders a 3D scene.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Scene3D), ResourceType = typeof(GraphicsStrings))]
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
    [Display(Name = nameof(GraphicsStrings.Scene3D_Camera), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Camera3D?> Camera { get; } = Property.Create<Camera3D?>();

    /// <summary>
    /// Gets the 3D objects in this scene.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Scene3D_Objects), ResourceType = typeof(GraphicsStrings))]
    [SuppressResourceClassGeneration]
    public IListProperty<Object3D> Objects { get; } = Property.CreateList<Object3D>();

    /// <summary>
    /// Gets the lights in this scene.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Scene3D_Lights), ResourceType = typeof(GraphicsStrings))]
    [SuppressResourceClassGeneration]
    public IListProperty<Light3D> Lights { get; } = Property.CreateList<Light3D>();

    /// <summary>
    /// Gets the ambient color of the scene.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Scene3D_AmbientColor), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> AmbientColor { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the ambient light intensity.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Scene3D_AmbientIntensity), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f), NumberStep(0.1, 0.01)]
    public IProperty<float> AmbientIntensity { get; } = Property.CreateAnimatable(0.1f);

    /// <summary>
    /// Gets the width of the 3D render target.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Scene3D_RenderWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(1f, 8192f)]
    public IProperty<float> RenderWidth { get; } = Property.CreateAnimatable(1920f);

    /// <summary>
    /// Gets the height of the 3D render target.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Scene3D_RenderHeight), ResourceType = typeof(GraphicsStrings))]
    [Range(1f, 8192f)]
    public IProperty<float> RenderHeight { get; } = Property.CreateAnimatable(1080f);

    /// <summary>
    /// Gets the background color of the 3D scene.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Scene3D_BackgroundColor), ResourceType = typeof(GraphicsStrings))]
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
        private static readonly ReadOnlyCollection<Light3D.Resource> s_emptyLights
            = Array.AsReadOnly(Array.Empty<Light3D.Resource>());
        private static readonly ReadOnlyCollection<Object3D.Resource> s_emptyObjects
            = Array.AsReadOnly(Array.Empty<Object3D.Resource>());

        private readonly List<Light3D.Resource> _lights = [];
        private readonly PooledList<int> _lightsVersion = [];
        private readonly List<Object3D.Resource> _objects = [];
        private readonly PooledList<int> _objectsVersion = [];
        private bool _disableResourceShare;
        private ReadOnlyCollection<Light3D.Resource> _lightsSnapshot = s_emptyLights;
        private ReadOnlyCollection<Object3D.Resource> _objectsSnapshot = s_emptyObjects;
        private ProxyPreset _preferredProxyPreset = ProxyPreset.Quarter;
        private bool _preferProxy;
        private Renderer3D? _renderer;
        private TimeSpan _time;

        internal Renderer3D? Renderer
        {
            get => ReadGeneratedResourceState(ref _renderer);
            set => WriteGeneratedResourceState(ref _renderer, value);
        }

        internal RenderOperationLease BeginRenderOperation() => new(this);

        public TimeSpan Time
        {
            get => ReadGeneratedResourceState(ref _time);
            set => WriteGeneratedResourceState(ref _time, value);
        }

        public bool DisableResourceShare
        {
            get => ReadGeneratedResourceState(ref _disableResourceShare);
            set => WriteGeneratedResourceState(ref _disableResourceShare, value);
        }

        public bool PreferProxy
        {
            get => ReadGeneratedResourceState(ref _preferProxy);
            set => WriteGeneratedResourceState(ref _preferProxy, value);
        }

        public ProxyPreset PreferredProxyPreset
        {
            get => ReadGeneratedResourceState(ref _preferredProxyPreset);
            set => WriteGeneratedResourceState(ref _preferredProxyPreset, value);
        }

        public IReadOnlyList<Light3D.Resource> Lights => ReadGeneratedResourceState(ref _lightsSnapshot);

        public IReadOnlyList<Object3D.Resource> Objects => ReadGeneratedResourceState(ref _objectsSnapshot);

        internal ref struct RenderOperationLease
        {
            private GeneratedResourceOperationLease _operation;

            internal RenderOperationLease(Resource owner)
            {
                _operation = owner.BeginExclusiveResourceOperation();
            }

            public void Dispose()
            {
                _operation.Dispose();
            }
        }

        partial void PrepareResourceDispose(
            bool disposing,
            EngineObject.Resource.GeneratedResourceCleanupContext context)
        {
            if (!disposing)
                return;

            int ownedLightsStart = Math.Min(_lightsVersion.Count, _lights.Count);
            for (int i = ownedLightsStart; i < _lights.Count; i++)
            {
                context.Reserve(_lights[i]);
            }

            int ownedObjectsStart = Math.Min(_objectsVersion.Count, _objects.Count);
            for (int i = ownedObjectsStart; i < _objects.Count; i++)
            {
                context.Reserve(_objects[i]);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Volatile.Write(ref _lightsSnapshot, s_emptyLights);
                Volatile.Write(ref _objectsSnapshot, s_emptyObjects);
                Exception? cleanupFailure = null;
                Renderer3D? renderer = Renderer;
                Renderer = null;
                Graphics3DDisposal.Capture(renderer, ref cleanupFailure);
                _lights.Clear();
                Graphics3DDisposal.Capture(_lightsVersion, ref cleanupFailure);

                _objects.Clear();
                Graphics3DDisposal.Capture(_objectsVersion, ref cleanupFailure);
                Graphics3DDisposal.ThrowIfFailed(cleanupFailure);
            }
        }

        partial void PostUpdate(Scene3D obj, CompositionContext context)
        {
            bool changed = false;
            if (_time != context.Time)
            {
                _time = context.Time;
                changed = true;
            }

            if (_disableResourceShare != context.DisableResourceShare)
            {
                _disableResourceShare = context.DisableResourceShare;
                changed = true;
            }

            if (_preferProxy != context.PreferProxy)
            {
                _preferProxy = context.PreferProxy;
                changed = true;
            }

            if (_preferredProxyPreset != context.PreferredProxyPreset)
            {
                _preferredProxyPreset = context.PreferredProxyPreset;
                changed = true;
            }

            // Consume lights and objects from flow
            EngineObject.Resource[]? flowRollbackSnapshot = context.Flow?.ToArray();
            using var consumedLights = new PooledList<Light3D.Resource>();
            using var consumedObjects = new PooledList<Object3D.Resource>();
            if (context.Flow != null)
            {
                for (int i = context.Flow.Count - 1; i >= 0; i--)
                {
                    switch (context.Flow[i])
                    {
                        case Light3D.Resource light:
                            context.Flow.RemoveAt(i);
                            consumedLights.Insert(0, light);
                            break;
                        case Object3D.Resource obj3d:
                            context.Flow.RemoveAt(i);
                            consumedObjects.Insert(0, obj3d);
                            break;
                    }
                }
            }

            try
            {
                ResourceReconciler.ReconcileListsFromFlow(
                    context: context,
                    firstProperty: obj.Lights,
                    firstConsumed: consumedLights,
                    firstField: _lights,
                    firstVersions: _lightsVersion,
                    secondProperty: obj.Objects,
                    secondConsumed: consumedObjects,
                    secondField: _objects,
                    secondVersions: _objectsVersion,
                    flowRollbackSnapshot: flowRollbackSnapshot,
                    changed: ref changed);
            }
            finally
            {
                PublishLightsSnapshotIfChanged();
                PublishObjectsSnapshotIfChanged();
                if (changed)
                    Version++;
            }
        }

        private void PublishLightsSnapshotIfChanged()
        {
            ReadOnlyCollection<Light3D.Resource> snapshot = Volatile.Read(ref _lightsSnapshot);
            if (SnapshotMatches(snapshot, _lights))
                return;

            Volatile.Write(
                ref _lightsSnapshot,
                _lights.Count == 0 ? s_emptyLights : Array.AsReadOnly(_lights.ToArray()));
        }

        private void PublishObjectsSnapshotIfChanged()
        {
            ReadOnlyCollection<Object3D.Resource> snapshot = Volatile.Read(ref _objectsSnapshot);
            if (SnapshotMatches(snapshot, _objects))
                return;

            Volatile.Write(
                ref _objectsSnapshot,
                _objects.Count == 0 ? s_emptyObjects : Array.AsReadOnly(_objects.ToArray()));
        }

        private static bool SnapshotMatches<T>(IReadOnlyList<T> snapshot, List<T> items)
            where T : class
        {
            if (snapshot.Count != items.Count)
                return false;

            for (int i = 0; i < items.Count; i++)
            {
                if (!ReferenceEquals(snapshot[i], items[i]))
                    return false;
            }

            return true;
        }
    }
}
