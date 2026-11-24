using System.Numerics;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

public sealed partial class Puppet
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(Puppet));

    private readonly record struct EditingCapture(Puppet Owner, MediaTime DisplayTime, MediaTime EvaluationTime)
    {
        public bool Capture(Rect bounds) => Owner._editingFrames.Capture(DisplayTime, bounds, EvaluationTime);
    }

    private readonly record struct RenderState(PuppetMeshCache Cache, Point[] Rest, Point[] Positions,
        EditingCapture? Editing, PlacementCapture? Placement);

    private static void Apply(RenderState state, CustomFilterEffectContext context)
    {
        if (context.Targets.Count == 0) return;

        using EffectTarget? composite = context.Targets.Count > 1 ? LayerEffect.CreateComposite(context) : null;
        EffectTarget source = composite ?? context.Targets[0];
        Rect bounds = source.Bounds;
        if (!EffectEditingFrames.HasArea(bounds) || !source.IsMaterialized) return;

        state.Editing?.Capture(bounds);
        if (state.Cache.GetOrCreate(source, state.Rest) is not { } prepared) return;

        (PuppetMesh mesh, PuppetDeformer deformer) = prepared;
        Vector2[] deformed = deformer.Deform(bounds.Size, state.Positions);
        Vector2 minimum = deformed[0];
        Vector2 maximum = minimum;
        foreach (Vector2 vertex in deformed)
        {
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y)) return;

            minimum = Vector2.Min(minimum, vertex);
            maximum = Vector2.Max(maximum, vertex);
        }
        if (state.Placement is { } capture)
            capture.Placement = new PinPlacement(bounds, mesh, deformed);
        if (ReferenceEquals(mesh.Rest, deformed)) return;

        var outputBounds = new Rect(bounds.X + minimum.X, bounds.Y + minimum.Y,
            maximum.X - minimum.X, maximum.Y - minimum.Y);
        if (!EffectEditingFrames.HasArea(outputBounds)) return;

        var rest = new Point[deformed.Length];
        var positions = new Point[deformed.Length];
        for (int i = 0; i < deformed.Length; i++)
        {
            rest[i] = PuppetMesh.Normalize(bounds.Size, mesh.Rest[i]);
            positions[i] = PuppetMesh.Normalize(bounds.Size, deformed[i]);
        }

        EffectTarget output = context.CreateTarget(outputBounds);
        try
        {
            if (!output.IsEmpty
                && TriangleWarpRenderer.Draw(bounds, rest, positions, mesh.Indices, context, source, output, s_logger))
            {
                context.Targets.Clear();
                context.Targets.Add(output);
            }
        }
        finally
        {
            if (output.Owner is null)
                output.Dispose();
        }
    }

    public new partial class Resource
    {
        private readonly PuppetMeshCache _meshCache = new();

        protected override void ApplyTo(FilterEffectContext context)
        {
            EditingCapture? editing = null;
            PlacementCapture? placement = null;
            if (context.IsPreviewFrame && GetOriginal() is Puppet owner)
            {
                if (s_placementCapture is { } requested && ReferenceEquals(requested.Owner, owner))
                {
                    placement = requested;
                }
                else if (Pins.Count > 0)
                {
                    MediaTime displayTime = EvaluationContext?.DisplayedTime ?? MediaTime.Zero;
                    editing = new EditingCapture(owner, displayTime, EvaluationContext?.Time ?? displayTime);
                }
            }

            if (Pins.Count == 0 && placement is null) return;

            var rest = new Point[Pins.Count];
            var positions = new Point[Pins.Count];
            bool moves = false;
            for (int i = 0; i < Pins.Count; i++)
            {
                rest[i] = Pins[i].RestPosition;
                positions[i] = Pins[i].Position;
                moves |= rest[i] != positions[i];
            }
            if (!moves && placement is null)
            {
                if (editing is { } capture && !capture.Capture(context.Bounds))
                    context.CustomEffect(capture, CaptureOnly, static (_, bounds) => bounds);
                return;
            }

            var state = new RenderState(_meshCache, rest, positions, editing, placement);
            if (moves)
            {
                // Alpha topology and displaced bounds are determined from the full, materialized input.
                context.CustomEffect(state, Apply);
            }
            else
            {
                context.CustomEffect(state, Apply, static (_, bounds) => bounds);
            }
        }

        private static void CaptureOnly(EditingCapture capture, CustomFilterEffectContext context)
            => capture.Capture(context.Targets.CalculateCompleteBounds());
    }
}
