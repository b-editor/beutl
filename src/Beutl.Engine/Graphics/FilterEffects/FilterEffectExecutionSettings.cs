using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The settings a <see cref="FilterEffectExecutor"/> runs with. The executors, callback contexts and stages it
/// starts take them whole, so a setting added here reaches all of them.
/// </summary>
internal sealed record FilterEffectExecutionSettings
{
    public FilterEffectExecutionSettings(RenderIntent intent, RenderRequestPurpose purpose)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");

        Intent = intent;
        Purpose = purpose;
    }

    /// <summary>Gets the explicit preview or delivery classification, which a nested execution keeps.</summary>
    public RenderIntent Intent { get; }

    /// <summary>Gets the explicit request purpose, which a nested execution keeps.</summary>
    public RenderRequestPurpose Purpose { get; }

    /// <summary>The render request's output scale <c>s_out</c>.</summary>
    public float OutputScale { get; init; } = 1f;

    /// <summary>The working density for buffer allocation.</summary>
    public float WorkingScale { get; init; } = 1f;

    /// <summary>The working-scale ceiling forwarded into nested canvases. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; init; } = float.PositiveInfinity;

    /// <summary>The effect-local to composition-device grid translation, or null to take the targets' own.</summary>
    public Vector? DeviceGridOffset { get; init; }

    public DrawableBrushMaterializer? DrawableBrushMaterializer { get; init; }

    public bool UseExecutorManagedCanvas { get; init; }

    /// <summary>The running render's lease session, which intermediates are allocated through.</summary>
    public RenderTargetLeaseSession? RenderTargetLeaseSession { get; init; }

    /// <summary>The budget an allocation is held to, or null for the allocation budget when it is made.</summary>
    public BufferDimensionBudget? Budget { get; init; }

    /// <summary>The request-level delivery region, not mapped into effect-local space.</summary>
    public Rect? TargetDomain { get; init; }

    public SkRuntimeEffectProgramAcquirer? ProgramAcquirer { get; init; }

    public ShaderGraphProgramAcquirer? ShaderGraphProgramAcquirer { get; init; }

    /// <summary>Renders dependencies through the active request, or null outside the renderer.</summary>
    public RenderExecutionContext? ExecutionContext { get; init; }

    /// <summary>The individual input an enclosing continuation is executing, or null outside one.</summary>
    public FilterEffectInstance? Instance { get; init; }

    // Per call: the graphics context may first answer after the settings are made.
    public BufferDimensionBudget ResolveBudget()
        => Budget ?? BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation);
}
