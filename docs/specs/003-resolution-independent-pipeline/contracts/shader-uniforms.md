# Contract: Custom-shader uniform scale contract (SKSL / GLSL)

**Feature**: 003 | FR-014. Audience: authors of `SKSLScriptEffect` / `GLSLScriptEffect` (in-tree and plugin) and the engine code that binds uniforms.

## Principle

Existing uniforms **keep their device-pixel meaning** = the size of the *scaled* target (`ceil(logicalBounds × w)`, where `w` is this effect's **working scale** — the supply-driven scale its `CustomFilterEffectContext` target is allocated at, FR-036); they are NOT redefined to logical. A new, explicitly-named **scale uniform** carries `w` so author code can scale absolute-pixel literals. **Scale-unaware shaders behave as `w = 1.0`** (device == logical) — fully backward compatible.

> **`w` is the CLAMPED buffer density (FR-037(b)).** The `w` bound into `iScale` / `width` / `height` / `Width` / `Height` is the density the target buffer was actually **allocated** at — `ClampWorkingScaleToBufferBudget(bounds, WorkingScale)` — which drops **below** the nominal working scale when `ceil(bounds × WorkingScale)` would exceed the 16384-px GPU axis limit. On a very large target `iScale` and the resolution uniforms shrink to keep the buffer allocatable, always agreeing with the buffer the shader iterates. In the common (unclamped) case the bound equals the working scale.

## SKSL (`SKSLScriptEffect.cs:99-104`)

| Uniform | Meaning | Under working scale `w` |
|---|---|---|
| `width`, `height` | target size, device px | `ceil(logicalBounds.W/H × w)` — smaller at reduced preview, larger when oversampled |
| `iResolution` | `(width, height)` — a 2-component `float2` (bound as an `SKPoint`, `SKSLScriptEffect.cs`); declare it `uniform float2 iResolution`, NOT `float3` | as above |
| `fragCoord` | device pixel coord | ranges over the scaled target |
| **`iScale`** *(new)* | working scale `w` | `w` (default `1.0`) |

Author rule: a UV-normalized shader (`fragCoord / iResolution`) auto-corrects across scales; a shader with an absolute pixel literal multiplies it by `iScale`, e.g. `float radius = 10.0 * iScale;`. Per-texel kernels (blur/edge/sharpen) are inherently resolution-sensitive (FR-013): reduced-scale preview is best-effort, full fidelity at export (`w=1`, i.e. `s_out=1.0` over a unit-scale input).

## GLSL (`GLSLScriptEffect.cs`)

GLSL carries the working scale in a dedicated **`scale`** push constant, mirroring SKSL's `iScale`. The private `PushConstants` struct is `Progress`/`Duration`/`Time`/`Width`/`Height`/`Scale` and the default-template `layout(push_constant)` block declares the matching `float scale;` member.

| Push constant | Meaning | Under working scale `w` |
|---|---|---|
| `width`, `height` | target size, device px (× w) | `ceil(logicalBounds.W/H × w)` |
| **`scale`** | working scale `w` | `w` (default `1.0`) |

`scale` is the **clamped buffer density** — `ResolveTargetDensity(bounds)` = `ClampWorkingScaleToBufferBudget(bounds, WorkingScale)`, the density the `ceil(bounds × w)` device buffer is allocated at — so it agrees with the buffer the shader iterates, identical to the value SKSL binds to `iScale`. A GLSL author multiplies an absolute-pixel literal by `scale`, e.g. `float border = 10.0 * pc.scale;`, instead of recovering `w` from `Width`/`Height` — a shader cannot do that without the logical bounds, and the recovery breaks across clips and under scale animation.

Adding the field is **purely additive and within the existing 128-byte push-constant budget**: `VulkanPipeline3D` hard-codes a 128-byte push-constant range and sizes the upload dynamically from `sizeof(T)`; the 6-float struct is 24 bytes, and `PushConstants` is private so there is no public ABI surface. GLSL effects run in-process via the engine's Vulkan/SkSL pipeline, NOT the FFmpeg GPL worker, so the MIT/GPL boundary is unaffected.

## Backward-compatibility rule

- A shader that does not reference `iScale` (SKSL) / `scale` (GLSL) produces output identical to today at `w=1.0` and renders at the smaller/larger device target at `w≠1.0`, behaving as if scale were 1.0 in its own pixel space.
- Existing uniforms are **never** silently redefined to logical units.
- Document `iScale` (SKSL) and `scale` (GLSL) in the shader-authoring docs; add a golden test asserting a scale-unaware shader is byte-identical at `w=1.0` and a scale-aware shader (`radius = N * iScale` / `radius = N * pc.scale`) matches its 1.0 reference within the SSIM threshold at a reduced scale.
