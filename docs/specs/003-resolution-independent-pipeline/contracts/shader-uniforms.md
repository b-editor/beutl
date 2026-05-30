# Contract: Custom-shader uniform scale contract (SKSL / GLSL)

**Feature**: 003 | FR-014. Audience: authors of `SKSLScriptEffect` / `GLSLScriptEffect` (in-tree and plugin) and the engine code that binds uniforms.

## Principle

Existing uniforms **keep their device-pixel meaning** = the size of the *scaled* target (`ceil(logicalBounds × w)`, where `w` is this effect's **working scale** — the supply-driven scale its `CustomFilterEffectContext` target is allocated at, FR-036). They are NOT redefined to logical. A new, explicitly-named **scale uniform** carrying `w` is added so author code can scale absolute-pixel literals. **Scale-unaware shaders behave as `w = 1.0`** (device == logical) — fully backward compatible.

## SKSL (`SKSLScriptEffect.cs:99-104`)

| Uniform | Meaning | Under working scale `w` |
|---|---|---|
| `width`, `height` | target size, device px | `ceil(logicalBounds.W/H × w)` — smaller at reduced preview, larger when oversampled |
| `iResolution` | `(width, height, 1)` | as above |
| `fragCoord` | device pixel coord | ranges over the scaled target |
| **`iScale`** *(new)* | working scale `w` | `w` (default `1.0`) |

Author rule: a UV-normalized shader (`fragCoord / iResolution`) auto-corrects across scales. A shader with an absolute pixel literal multiplies it by `iScale`, e.g. `float radius = 10.0 * iScale;`. Per-texel kernels (blur/edge/sharpen) are inherently resolution-sensitive (FR-013): reduced-scale preview is best-effort, full fidelity at export (`w=1`, i.e. `s_out=1.0` over a unit-scale input).

## GLSL (`GLSLScriptEffect.cs:91-109`)

| Push constant | Meaning | Under working scale `w` |
|---|---|---|
| `Width`, `Height` | target size, device px | `ceil(logicalBounds.W/H × w)` |
| **`Scale` / `uScale`** *(new)* | working scale `w` | `w` (default `1.0`) |

The new push-constant field is a `StructLayout` + `push_constant` block change. **GLSL runs in `Beutl.FFmpegWorker`? No** — GLSL effects run in-process via the engine's Vulkan/SkSL pipeline (not the FFmpeg GPL worker), so this is an MIT-side ABI change, not a GPL/MIT boundary change. (The license firewall is unaffected by feature 003.)

## Backward-compatibility rule

- A shader that does not reference `iScale`/`uScale` produces identical output to today at `w=1.0` and renders at the (smaller/larger) device target at `w≠1.0` — i.e. it behaves exactly as if scale were 1.0 in its own pixel space.
- Existing uniforms are **never** silently redefined to logical units.
- Document `iScale`/`uScale` in the shader-authoring docs; add a golden test asserting a scale-unaware shader is byte-identical at `w=1.0` and a scale-aware shader (`radius = N * iScale`) matches its 1.0 reference within the SSIM threshold at a reduced scale.
