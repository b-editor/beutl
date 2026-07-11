# Public API / design review

Design-axis review of the proxy-media public surface on branch `yuto-trd/proxy`
(base `main`), against the AGENTS.md "adopt better designs eagerly" priorities:
orthogonality, library-user flexibility, compatibility-shim avoidance, and
breaking-change hygiene. The four `beutl-reviewer` axes (GPL/MIT, XAML bindings,
NUnit, SourceGen) are out of scope here.

## Severity summary

| Severity | Count | Findings |
|---|---|---|
| High | 0 | — |
| Medium | 2 | F1 (CompositionContext dual-bool redundancy), F2 (IProxyEvictionPolicy over-promising name) |
| Low | 1 | F3 (ProxyStoreConfig.DefaultPreset unvalidated int) |
| Nit | 0 | — |

Overall the surface is in good shape: the three real extensibility seams
(generator, resolver, store) are interfaces, the concrete classes are `sealed`
behind them, every breaking change carries a correct `refactor!:` / `feat!:`
Conventional Commit with a `BREAKING CHANGE:` footer and migration notes, and no
`[Obsolete]` shims / `V2` types / duplicate compat overloads were introduced. The
"legacy" tokens in the diff are on-disk sidecar-format migration, not API
back-compat wrappers. The findings below are refinements, not blockers.

## [Medium] CompositionContext.ForceOriginalSource + PreferProxy — two derived bools model one tri-state; collapse to a single signal

`CompositionContext` now carries three signals for the same "use proxy or
original" decision: `ForceOriginalSource`, `PreferProxy`, and (upstream) the
`EditorConfig.PreviewSourceMode` enum. The XML doc itself concedes this ("three
signals, one source of truth") and pins the invariant `ForceOriginalSource =>
!PreferProxy` that must be maintained by hand at the producer.

The redundancy is load-bearing across projects, not local: `ForceOriginalSource`
is threaded through `SceneCompositor`, `SceneComposer`, `SceneRenderer`,
`GraphSnapshot`, and `NodeGraphFilterEffect.GraphResource` purely to be
re-projected, while the only *ultimate* consumer — `VideoSource.cs` — reads
`context.PreferProxy` exclusively. `SceneDrawable` / `SceneSound` read both but
immediately collapse them to `context.ForceOriginalSource || !context.PreferProxy`,
which the doc admits reduces to `!PreferProxy` given the invariant. So
`ForceOriginalSource` carries no information the consumer can't get from
`PreferProxy`; it is a parallel channel kept consistent by convention.

### Location
- `src/Beutl.Engine/Composition/CompositionContext.cs:46-51`
- `src/Beutl.ProjectSystem/SceneCompositor.cs:25,42-44`
- `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs:91`, `SceneSound.cs:89`
- `src/Beutl.NodeGraph/Composition/GraphSnapshot.cs:173-174,296-297`, `NodeGraphFilterEffect.cs:53-55,91-92`

### Severity
medium

### Why this matters
AGENTS.md "Orthogonality first": two fields encoding one decision with a
hand-maintained invariant is exactly the kind of muddled state the policy asks us
to collapse. Any future producer that sets the two bools inconsistently silently
violates the invariant, and there is no type-level guard. Because Engine already
depends on `Beutl.Configuration`, the context could carry the
`PreviewSourceMode` enum (plus the existing `PreferredProxyPreset`) directly, and
the derived bit would be computed once at the single decode site — removing the
invariant instead of documenting it.

### Suggested direction
Replace both bools with one signal. Cleanest: give `CompositionContext` a single
`PreviewSourceMode PreviewSourceMode { get; set; }` (Engine already references
Configuration) and let `VideoSource` compute "prefer proxy" locally; or, if the
enum dependency is unwanted here, keep a single `bool PreferProxy` and delete
`ForceOriginalSource`, seeding `PreferProxy` at the one producer. Note the
trade-off: this ripples into ProjectSystem + NodeGraph render-node replay paths
(the `set`-not-`init` hot-path mutation the doc describes), so it is a
non-trivial diff. Surface it to the user rather than treating the current
documented redundancy as settled — the doc explains *how* it stays consistent but
not why one signal wouldn't do.

## [Medium] IProxyEvictionPolicy — the name promises a strategy seam it does not expose (only a byte cap)

`IProxyEvictionPolicy` has exactly one member, `long MaxTotalBytes`, and its sole
consumer reads it to render the store-cap label in `ProxiesTabViewModel`. All the
actual eviction *policy* — LRU ordering, open-project affinity, disk-pressure
sweeps, active-generation protection — is baked into the concrete
`ProxyEvictionService` with no virtual hooks or injectable strategy. So the
interface is a read-only projection of one config value, but its name advertises a
pluggable eviction strategy.

### Location
- `src/Beutl.Engine/Media/Proxy/IProxyEvictionPolicy.cs:3-6`
- `src/Beutl.Engine/Media/Proxy/ProxyEvictionService.cs:3` (only implementer)
- `src/Beutl.Editor.Components/ProxiesTab/ViewModels/ProxiesTabViewModel.cs:596` (only consumer)

### Why this matters
AGENTS.md "Library-user flexibility first" and orthogonality. Compared to the
sibling seams in this same change — `IProxyGeneratorFactory`, `IProxyResolver`,
`IProxyStore`, all genuine replacement points — this interface is the odd one out:
its name implies a substitutable policy, but a plugin author cannot replace the
eviction *decision* through it, only read a number. That mismatch will mislead a
future implementer into thinking eviction is pluggable when it is not.

### Suggested direction
Pick one of two coherent shapes, don't leave the half-way one:
1. If eviction strategy is intentionally *not* pluggable in the MVP (a reasonable
   scope call), rename to what it actually is — a cap readout, e.g.
   `IProxyStoreCapInfo` / `IProxyEvictionStatus` — so the name stops promising a
   strategy seam, and document the closed-strategy decision as you did for
   `ProxyPreset`/FR-017.
2. If eviction strategy *should* be replaceable (LRU vs LFU vs size-weighted is a
   natural plugin want), widen the interface to expose the selection seam (e.g. a
   method that ranks/selects entries for eviction) and have `ProxyEvictionService`
   consume an injected policy. This is the more "adopt better designs eagerly"
   direction but a larger change; surface the trade-off.

## [Low] ProxyStoreConfig.DefaultPreset — unvalidated `int` public surface can desync from the closed ProxyPreset set

`DefaultPreset` is a raw `int` (documented as avoiding a Configuration→Engine
cycle, with a comment that `2` == `ProxyPreset.Quarter`). The getter/setter do no
range validation, so a persisted or programmatically-set value outside {1,2,3}
round-trips unchanged and only blows up later at `ProxyPresetDefinitions.Get`,
which throws `ArgumentOutOfRangeException`. Contrast `MaxTotalBytes`, which clamps
on both get and set.

### Location
- `src/Beutl.Configuration/ProxyStoreConfig.cs:28-30,46-50`

### Why this matters
Public config surface that can hold an invalid value and defers the failure to a
distant call site is a small robustness gap, and it clusters with the deliberately
closed `ProxyPreset` value set (FR-017) — the config is the one place the closed
set is represented as an unconstrained int. Minor, but cheap to close.

### Suggested direction
Clamp/validate `DefaultPreset` to the known preset ints on get (and/or set), the
way `MaxTotalBytes` clamps — falling back to the default `2` for out-of-range
values — so an out-of-range persisted setting degrades gracefully instead of
throwing downstream. The int-vs-enum boundary choice itself is fine and correctly
justified by the module boundary.

## Sound by design

- **`ProxyGeneratorRegistry` + `IProxyGeneratorFactory` + `ProxyExtension`** —
  clean pluggable-generator seam that deliberately mirrors
  `DecoderRegistry`/`IDecoderInfo`/`DecodingExtension`. Store-agnostic factory
  bound lazily at composition time, symmetric Register/Unregister at Load/Unload,
  and the built-in `FFmpegProxyExtension` goes through the same public path with no
  compile-time app→FFmpeg reference. Consistent-with-precedent static registry is
  the right call here, not a smell.
- **`IProxyResolver` (interface) + `ProxyResolver` (sealed)** — correct seam
  shape. Moving `IsPinned(string)` onto the interface (commit `289a452c2`) is the
  right fix so a custom resolver's pins are honored by the eviction service;
  properly flagged as `BREAKING CHANGE:`.
- **`IProxyGenerator` vs `IProxyGeneratorAvailability`** — good orthogonal
  capability split: an always-available generator need not implement the
  availability probe. FFmpeg's implements both.
- **`IProxyJobQueue` priority overload via default interface method** — the
  priority overload defaulting to the arrival-order overload is genuine optional
  extension (simple implementers ignore priority), not a compat shim to spare
  callers. Acceptable.
- **`ProxyPreset` closed enum + `ProxyPresetDefinitions` Register/Unregister/All**
  — the closed value set is a documented product decision (FR-017) with an
  explicit string-keyed follow-up path, while encode *parameters* are overridable
  via a non-mutating snapshot API. The trade-off is surfaced in XML docs, exactly
  as the policy asks. `Register`/`Unregister` restoring the built-in default
  (rather than removing the key) keeps `Get` total for built-ins — a deliberate,
  correct invariant.
- **`ProxyFingerprint`** — identity/dedup key (folded `AbsolutePath`) cleanly
  separated from the I/O path (`SourcePath`), with equality/hashing intentionally
  excluding `SourcePath`. Case-fold and symlink-resolution rules are internal, so
  callers never reproduce them. Well-factored value type.
- **`PreviewSourceMode` move Scene → `Beutl.Configuration.EditorConfig`**
  (commit `81407370d`) — a positive orthogonality change: a user preview
  preference belongs in global config, not the Scene document model. Breaking
  move is fully documented (removed CoreProperty, ignored JSON key, dropped
  service parameter, converter removal) with migration notes.
- **`ProxySourceEnumerator` unification** (commit `d6055f822`) — collapsing the
  two parallel "which media does this reference?" queries into one public home in
  `Beutl.Editor` is the orthogonality-first direction; breaking namespace move and
  removed `CollectProjectFileSources` are documented, and the repo grep confirms
  no leftover references to the removed symbol.
- **`IProxyStore`** — a cohesive repository abstraction. The breadth (query,
  mutate, transition, LRU touch, byte accounting, flush/reconcile, change events)
  is the normal surface of a store, not two unrelated responsibilities bolted
  together.
- **`CompositionContext` `set`-not-`init` mutability** — the hot-path render-node
  replay rationale is documented and legitimate; this is not a compat concession.
