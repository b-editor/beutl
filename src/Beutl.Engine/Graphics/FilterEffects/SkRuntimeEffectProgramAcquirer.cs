using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

internal delegate ProgramCacheLease<CachedSkRuntimeEffect> SkRuntimeEffectProgramAcquirer(
    EffectTarget target,
    string source);
