namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderExecutionStatistics(
    int ShaderRunExecutions,
    int ShaderStageExecutions,
    int FusedShaderRunExecutions,
    int SpirvShaderRunExecutions,
    int IntermediateTargetAcquisitions,
    int ProgramCacheHits,
    int Synchronizations);
