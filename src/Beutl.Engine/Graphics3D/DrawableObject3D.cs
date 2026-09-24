using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Textures;
using Beutl.Language;

namespace Beutl.Graphics3D;

/// <summary>
/// Places 2D drawables in a 3D scene as a flat card.
/// </summary>
/// <remarks>
/// It takes the drawables that come before it in the flow, or its own <see cref="Children"/>, lays them out
/// as a 2D scene the size of the 3D scene would, and shows the part they cover on a card at z = 0. With the
/// default camera the card looks exactly like the 2D drawables; <see cref="Object3D.Position"/>,
/// <see cref="Object3D.Rotation"/> and <see cref="Object3D.Scale"/> then move it about the content's center.
/// </remarks>
[Display(Name = nameof(GraphicsStrings.DrawableObject3D), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableObject3D : Object3D, IFlowOperator
{
    public DrawableObject3D()
    {
        ScanProperties<DrawableObject3D>();
        Material.CurrentValue = new UnlitMaterial();
        CastShadows.CurrentValue = false;
    }

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.Children), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    public partial class Resource
    {
        // The card is a unit square on the XY plane facing the default camera; ContentMatrix sizes it.
        private static readonly Matrix4x4 s_planeToCard = Matrix4x4.CreateRotationX(MathF.PI / 2);

        private readonly PooledList<int> _childrenVersion = [];
        private readonly PlaneMesh _mesh = new() { Width = { CurrentValue = 1 }, Height = { CurrentValue = 1 } };
        private PlaneMesh.Resource? _meshResource;
        private readonly DrawableContentTexture _content = new();

        public List<Drawable.Resource> Children { get; set; } = [];

        internal DrawableContentTexture Content => _content;

        internal override Matrix4x4 ContentMatrix
        {
            get
            {
                Rect bounds = _content.ContentBounds;
                return s_planeToCard * Matrix4x4.CreateScale(bounds.Width, bounds.Height, 1);
            }
        }

        internal override Vector3 ContentOffset
        {
            get
            {
                Rect bounds = _content.ContentBounds;
                Size canvas = _content.CanvasSize;
                return new Vector3(
                    bounds.X + (bounds.Width / 2) - (canvas.Width / 2),
                    bounds.Y + (bounds.Height / 2) - (canvas.Height / 2),
                    0);
            }
        }

        public override Mesh.Resource? GetMesh() => _content.HasContent ? _meshResource : null;

        /// <summary>Lays the drawables out on a 2D canvas of <paramref name="canvasSize"/>.</summary>
        internal void UpdateLayout(Size canvasSize, float density)
        {
            _content.Drawables = Children;
            _content.UpdateLayout(canvasSize, density);
        }

        partial void PreUpdate(DrawableObject3D obj, CompositionContext context)
        {
            if (ResourceReconciler.ReconcileChildrenFromFlow(context, obj.Children, Children, _childrenVersion))
                Version++;
        }

        partial void PostUpdate(DrawableObject3D obj, CompositionContext context)
        {
            _meshResource ??= _mesh.ToResource(context);
            _content.Drawables = Children;
            if (Material is UnlitMaterial.Resource unlit)
                unlit.ContentMap = _content;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                ResourceReconciler.ReleaseReconciledChildren(Children, _childrenVersion);
                _meshResource?.Dispose();
                _meshResource = null;
                _content.Dispose();
            }
        }
    }
}
