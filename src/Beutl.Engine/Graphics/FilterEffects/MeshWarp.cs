using System.ComponentModel.DataAnnotations;
using System.Globalization;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.JsonConverters;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.MeshWarp), ResourceType = typeof(GraphicsStrings))]
public sealed partial class MeshWarp : FilterEffect, IEditingHandleProvider
{
    private static readonly Color s_cornerColor = Color.FromRgb(255, 179, 77);
    private const int MaxCachedInputBounds = 512;
    private readonly Lock _inputBoundsLock = new();
    private readonly Dictionary<MediaTime, Rect> _renderedInputBounds = [];
    private readonly Queue<MediaTime> _renderedInputBoundsOrder = [];

    public MeshWarp()
    {
        ScanProperties<MeshWarp>();
        Rows.ValueChanged += OnGridDimensionsChanged;
        Columns.ValueChanged += OnGridDimensionsChanged;
    }

    [Display(Name = nameof(GraphicsStrings.MeshWarp_Rows), ResourceType = typeof(GraphicsStrings))]
    [Range(MeshWarpGrid.MinCells, MeshWarpGrid.MaxCells)]
    public IExpressionProperty<int> Rows { get; } = Property.CreateExpression(MeshWarpGrid.DefaultCells);

    [Display(Name = nameof(GraphicsStrings.MeshWarp_Columns), ResourceType = typeof(GraphicsStrings))]
    [Range(MeshWarpGrid.MinCells, MeshWarpGrid.MaxCells)]
    public IExpressionProperty<int> Columns { get; } = Property.CreateExpression(MeshWarpGrid.DefaultCells);

    [Display(Name = nameof(GraphicsStrings.Strength), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.MeshWarp_Interpolation), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<MeshWarpInterpolation> Interpolation { get; } = Property.CreateAnimatable(MeshWarpInterpolation.Smooth);

    [Display(Name = nameof(GraphicsStrings.MeshWarp_Mesh), ResourceType = typeof(GraphicsStrings),
        Description = nameof(GraphicsStrings.MeshWarp_Mesh_Description))]
    public IExpressionProperty<MeshWarpGrid> Mesh { get; } = Property.CreateAnimatable(MeshWarpGrid.Default);

    private void OnGridDimensionsChanged(object? sender, PropertyValueChangedEventArgs<int> e)
    {
        // History already holds the original meshes, including any detail a smaller grid discarded.
        if (HistoryReplay.IsActive) return;

        int rows = Rows.Value;
        int columns = Columns.Value;
        MeshWarpInterpolation interpolation = Interpolation.Value;
        Mesh.Value = Resize(Mesh.Value);
        if (Mesh.GetKeyFrameAnimation() is KeyFrameAnimation<MeshWarpGrid> animation)
        {
            foreach (KeyFrame<MeshWarpGrid> key in animation.KeyFrames.OfType<KeyFrame<MeshWarpGrid>>())
                key.Value.Value = Resize(key.Value.Value);
        }

        MeshWarpGrid Resize(MeshWarpGrid? grid)
            => grid?.Resample(rows, columns, interpolation) ?? MeshWarpGrid.CreateIdentity(rows, columns);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        Rows.ValueChanged -= OnGridDimensionsChanged;
        Columns.ValueChanged -= OnGridDimensionsChanged;
        try
        {
            base.Deserialize(context);
        }
        finally
        {
            Rows.ValueChanged += OnGridDimensionsChanged;
            Columns.ValueChanged += OnGridDimensionsChanged;
        }
    }

    /// <summary>Resets the base mesh or the mesh keyframe on the current frame to its rest grid.</summary>
    public void ResetMesh(MediaTime time) => EditMesh(time, grid => MeshWarpGrid.CreateIdentity(grid.Rows, grid.Columns));

    public IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time)
        => GetEditingHandles(time, default);

    public IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time, Rect fallbackBounds)
    {
        Rect bounds = GetEditingInputBounds(time, fallbackBounds);
        if (!HasArea(bounds)) return [];

        MeshWarpGrid grid = GetEditingGrid(new EvaluationContext(time));
        var handles = new List<EditingHandle>(grid.Points.Length);
        for (int row = 0; row <= grid.Rows; row++)
        {
            for (int column = 0; column <= grid.Columns; column++)
            {
                int index = row * (grid.Columns + 1) + column;
                Point position = MapPoint(bounds, grid.Points[index]);
                if (!MeshWarpGrid.IsFinite(position)) continue;

                bool isCorner = (row == 0 || row == grid.Rows) && (column == 0 || column == grid.Columns);
                handles.Add(new EditingHandle(index.ToString(CultureInfo.InvariantCulture), position,
                    string.Format(GraphicsStrings.MeshWarp_Point, row + 1, column + 1))
                {
                    Color = isCorner ? s_cornerColor : null,
                });
            }
        }

        return handles;
    }

    public IReadOnlyList<EditingGuideLine> GetEditingGuides(MediaTime time)
        => GetEditingGuides(time, default);

    public IReadOnlyList<EditingGuideLine> GetEditingGuides(MediaTime time, Rect fallbackBounds)
    {
        Rect bounds = GetEditingInputBounds(time, fallbackBounds);
        if (!HasArea(bounds)) return [];

        MeshWarpGrid grid = GetEditingGrid(new EvaluationContext(time));
        int stride = grid.Columns + 1;
        var points = grid.Points.Select(p => MapPoint(bounds, p)).ToArray();
        var guides = new List<EditingGuideLine>(grid.Rows * stride + grid.Columns * (grid.Rows + 1));
        for (int row = 0; row <= grid.Rows; row++)
        {
            for (int column = 0; column <= grid.Columns; column++)
            {
                int index = row * stride + column;
                if (column < grid.Columns) AddLine(index, index + 1);
                if (row < grid.Rows) AddLine(index, index + stride);
            }
        }

        return guides;

        void AddLine(int start, int end)
        {
            if (MeshWarpGrid.IsFinite(points[start]) && MeshWarpGrid.IsFinite(points[end]))
                guides.Add(new EditingGuideLine(points[start], points[end]));
        }
    }

    public Action<Point> BeginEditingHandleDrag(string id, MediaTime time)
        => BeginEditingHandleDrag(id, time, default);

    public Action<Point> BeginEditingHandleDrag(string id, MediaTime time, Rect fallbackBounds)
    {
        Rect inputBounds = GetEditingInputBounds(time, fallbackBounds);
        return position => MoveEditingHandle(id, position, time, inputBounds);
    }

    public void MoveEditingHandle(string id, Point position, MediaTime time)
        => MoveEditingHandle(id, position, time, GetEditingInputBounds(time, default));

    private void MoveEditingHandle(string id, Point position, MediaTime time, Rect bounds)
    {
        if (!HasArea(bounds) || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            return;

        var normalized = new Point((position.X - bounds.X) / bounds.Width, (position.Y - bounds.Y) / bounds.Height);
        EditMesh(time, grid => index < grid.Points.Length ? grid.WithPoint(index, normalized) : grid);
    }

    private void EditMesh(MediaTime time, Func<MeshWarpGrid, MeshWarpGrid> edit)
    {
        if (Mesh.HasExpression) return;

        var evaluation = new EvaluationContext(time);
        if (Mesh.GetKeyFrameAnimation() is not KeyFrameAnimation<MeshWarpGrid> animation)
        {
            Mesh.Value = edit(GetEditingGrid(evaluation));
            return;
        }

        Rational rate = Project.ReadFrameRate(this.FindHierarchicalParent<Project>());
        MediaTime localTime = animation.ToLocal(time).RoundToFrame(rate, MidpointRounding.AwayFromZero);
        var key = animation.KeyFrames.OfType<KeyFrame<MeshWarpGrid>>()
            .FirstOrDefault(key => MediaTimeJsonConverter.SnapToFrame(key.KeyTime.Value, rate) == localTime);
        MeshWarpGrid current = GetEditingGrid(evaluation, key?.Value.Value);
        MeshWarpGrid updated = edit(current);
        if (current.Equals(updated)) return;

        if (key is not null)
            key.Value.Value = updated;
        else
            animation.KeyFrames.Add(new KeyFrame<MeshWarpGrid>
            {
                Easing = { Value = new SplineEasing() },
                KeyTime = { Value = localTime },
                Value = { Value = updated },
            }, out _);
    }

    // The guides and handles show the authored mesh even when animated strength is zero.
    private MeshWarpGrid GetEditingGrid(EvaluationContext evaluation, MeshWarpGrid? keyValue = null)
    {
        int rows = Rows.Evaluate(evaluation);
        int columns = Columns.Evaluate(evaluation);
        MeshWarpGrid? grid = keyValue ?? Mesh.Evaluate(evaluation);
        return grid?.Resample(rows, columns, Interpolation.Evaluate(evaluation))
            ?? MeshWarpGrid.CreateIdentity(rows, columns);
    }

    private Rect GetEditingInputBounds(MediaTime time, Rect fallbackBounds)
    {
        if (IsEnabled.Value && TryGetCapturedInputBounds(time, out Rect captured))
            return captured;

        if (HasArea(fallbackBounds))
            return fallbackBounds;

        if (this.FindHierarchicalParent<Drawable>() is not { } drawable) return Rect.Empty;

        using Drawable.Resource resource = drawable.ToResource(new CompositionContext(time));
        return new Rect(resource.Measure(Size.Infinity));
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        ClearCapturedInputBounds();
    }

    private void ClearCapturedInputBounds()
    {
        lock (_inputBoundsLock)
        {
            _renderedInputBounds.Clear();
            _renderedInputBoundsOrder.Clear();
        }
    }

    private bool TryGetCapturedInputBounds(MediaTime time, out Rect bounds)
    {
        lock (_inputBoundsLock)
            return _renderedInputBounds.TryGetValue(time, out bounds);
    }

    private bool CaptureInputBounds(MediaTime time, Rect bounds)
    {
        if (!HasArea(bounds)) return false;

        lock (_inputBoundsLock)
        {
            if (!_renderedInputBounds.ContainsKey(time))
            {
                if (_renderedInputBounds.Count >= MaxCachedInputBounds)
                {
                    MediaTime oldest = _renderedInputBoundsOrder.Dequeue();
                    _renderedInputBounds.Remove(oldest);
                }

                _renderedInputBoundsOrder.Enqueue(time);
            }

            _renderedInputBounds[time] = bounds;
        }

        return true;
    }
}
