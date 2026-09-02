using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Effects;

internal delegate ProgramCacheLease<CachedSkRuntimeEffect> SkRuntimeEffectProgramAcquirer(
    EffectTarget target,
    string source);
