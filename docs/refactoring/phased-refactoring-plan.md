# Beutl Phased Refactoring Plan

Status: **Proposed** (2026-06)
Owner: maintainers (b-editor)
Tracking: per-phase Draft items on [GitHub Projects #9](https://github.com/orgs/b-editor/projects/9); per-PR follow-ups in PR descriptions per `AGENTS.md`.

This plan is the result of a four-track audit of the repository (architecture & coupling, tech-debt indicators, test landscape, spec/roadmap collision check). It orders work so that **each phase reduces the risk of the next one**: guardrails → mechanical cleanup → debt removal under existing test cover → expanding the safety net → structural surgery in the riskiest, least-tested areas last.

## Audit summary (evidence base)

**What the audit found — the codebase is healthier than "years of accumulated waste" suggests, but the debt that exists is concentrated:**

- ~210K LOC across 22 `src/` projects; dependency graph is **acyclic**, dependency stack is modern (net10.0, Avalonia 11.3.x, SkiaSharp 3.x, zero stale packages, no Newtonsoft).
- Only ~30 TODO/FIXME markers, but **24 `[Obsolete]` members** with live callers — UI code crosses the obsolete boundary via `#pragma warning disable CS0618` (`src/Beutl/Views/MainView.axaml.cs`, `MacWindow.axaml.cs`, `MainViewModel.cs`).
- **Three god classes that are also the highest-churn files** in the last 18 months: `src/Beutl/ViewModels/PlayerViewModel.cs` (1,642 lines, top churn), `src/Beutl/Views/PlayerView.axaml.MouseControl.cs` (1,946 lines), `src/Beutl.Editor.Components/TimelineTab/ViewModels/TimelineTabViewModel.cs` (1,174 lines). High churn × high size = highest payoff for decomposition.
- **One critical layering violation**: `Beutl.Controls` (supposedly presentation-only) owns GPU rendering integration — `HdrBitmapView` directly manages `VulkanSwapchainRenderer`.
- **Duplicated utility families**: two unrelated `AvaloniaTypeConverter` classes (`src/Beutl.Controls/AvaloniaTypeConverter.cs` vs `src/Beutl/Helpers/AvaloniaTypeConverter.cs`), three generic `Helper.cs` files, 30+ scattered Json/Type converters with no shared registration, duplicated Beutl↔Avalonia geometry conversions.
- **Test cover is bimodal**: Beutl.Core / Beutl.Engine / Beutl.Editor services are well-tested (220 test files, 80% CI gate); **ViewModels/Views, Beutl.Api, Beutl.Extensibility, Beutl.NodeGraph core, Beutl.PackageTools.UI have effectively zero automated tests**.
- `docs/specs/` is empty — no in-flight spec collides with this plan.

**Design policy leverage**: `AGENTS.md` explicitly prefers breaking the API over compatibility shims ("update the call sites in the same change"). Phases 2 and 6–7 rely on this: no `[Obsolete]` bridges, no V2 types; `refactor!:` commits with `BREAKING CHANGE:` footers instead.

## Ground rules for every phase

1. One PR per numbered task below; keep PRs reviewable (<~800 lines of non-mechanical diff).
2. Behavior-preserving phases (1, 3, 5, 6) must not change any serialized project format or public rendering output; add a regression test before touching code that lacks one.
3. Public-surface changes (`Beutl.Engine`, `Beutl.Extensibility`, `Beutl.NodeGraph`, `Beutl.FFmpegIpc`) go through the `beutl-design-reviewer` audit.
4. Never cross the GPL/MIT boundary; `Beutl.FFmpegWorker` stays IPC-only.
5. Each phase ends with: full `dotnet test Beutl.slnx -f net10.0` green, coverage not lower than at phase start, and a phase-retro note appended to this document.
6. Anything discovered-but-deferred goes to the PR's `## Follow-ups` section or Projects #9 — nothing evaporates.

---

## Phase 0 — Baseline & guardrails (no code changes)

**Goal:** make "did the refactor break anything / help anything" answerable.

| # | Task | Notes |
|---|------|-------|
| 0.1 | Record baseline metrics: per-project LOC, top-20 file sizes, coverage per namespace (from `coverlet` cobertura output), project dependency edge list. Commit as `docs/refactoring/baseline-2026-06.md`. | Re-measure at each phase exit. |
| 0.2 | Snapshot rendering output: add a small golden-frame test that renders a fixture scene (existing `TestMediaHelper` infra) and compares hashes, so Engine-adjacent refactors have a pixel-level tripwire. | Lives in `tests/Beutl.UnitTests/Engine/`. |
| 0.3 | File one Projects #9 Draft item per phase of this plan, linking back here. | Keeps the board the single tracker. |
| 0.4 | Decide and document the public-API deprecation stance for out-of-tree plugin authors (per `AGENTS.md`, default is *no* deprecation window unless explicitly requested). | One paragraph appended here. |

**Exit criteria:** baseline doc merged; golden-frame test green in CI.
**Risk:** none. **Estimated size:** 2–3 small PRs.

## Phase 1 — Mechanical cleanup (low risk, behavior-preserving)

**Goal:** delete trivially dead weight so later diffs are clean. No design decisions in this phase.

| # | Task | Evidence |
|---|------|----------|
| 1.1 | Remove the `#if false` localhost block in `src/Beutl.Api/BeutlApiApplication.cs:27` and the `#if true` anti-pattern in `src/Beutl/Services/Telemetry.cs:28`; replace with a single configurable endpoint (env var or `Beutl.Configuration`). | Dead/always-true preprocessor branches. |
| 1.2 | Rename collision-prone generic helpers: `src/Beutl.Engine/Media/Decoding/APNG/Helper.cs`, `src/Beutl.Engine/Graphics/Helper.cs`, `src/Beutl.Api/Services/Helper.cs` → intent-revealing names. | Three unrelated `Helper` classes. |
| 1.3 | Apply trivially safe TODO renames, e.g. `PathOperationEditorViewModel` → `PathSegmentEditorViewModel` (`src/Beutl.ViewModels/Editors/PathOperationEditorViewModel.cs:7`). | Self-identified naming debt. |
| 1.4 | Audit the 21 `#pragma warning disable` sites: keep the justified ones (platform interop, generator artifacts) with a one-line reason comment, delete any that no longer fire. **Do not** touch the `CS0618` ones yet — Phase 2 removes their cause. | |
| 1.5 | Reclassify non-test playground projects (`tests/DirectoryViewTest`, `tests/KeySplineEditor`, `tests/XamlPreview`, `tests/TextFormattingPlayground`, `tests/PropertyEditorViewTests`) — move under a `playgrounds/` solution folder in `Beutl.slnx` so `dotnet test` scope and coverage reporting are honest. | They are interactive apps, not test suites. |

**Exit criteria:** zero dead preprocessor branches; no generic `Helper.cs` names; playgrounds separated from tests in the solution.
**Risk:** very low. **Estimated size:** 4–5 small PRs. Commits: `refactor:` / `chore:`.

## Phase 2 — Obsolete-API elimination (breaking, but well-tested area)

**Goal:** drive `[Obsolete]` count from 24 to ~0 by migrating call sites and deleting the members — per the no-shim policy. This unblocks the UI layer from `CS0618` suppressions before we touch it structurally.

| # | Task | Scope |
|---|------|-------|
| 2.1 | **Encoding family**: migrate remaining consumers of `EncodingExtension`, `IEncoderInfo`, `EncoderRegistry`, `MediaWriter` to `EncodingController`; delete the old types. `refactor!:` with `BREAKING CHANGE:` footer (affects `Beutl.Extensibility` — plugin-facing). | `src/Beutl.Extensibility/EncodingExtension.cs` + callers. |
| 2.2 | **Byte-based LUTs**: remove the obsolete LUT overloads in `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs:406,437`; route remaining callers to the shader-based path. Golden-frame test (0.2) guards output. | HDR-unsafe API. |
| 2.3 | **Collection marshalling**: remove `GetMarshal()` from `ICoreList`/`CoreList` after migrating callers to spans/enumeration. | `src/Beutl.Core/Collections/`. |
| 2.4 | **Geometry/animation leftovers**: delete obsolete `PathGeometry.StartPoint`/`IsClosed`, `Dispatcher.Shutdown`, obsolete `EffectTarget` constructors, obsolete converter classes in `src/Beutl.Engine/Converters/`. | |
| 2.5 | **UI followers**: with the causes gone, delete the `CS0618` pragmas in `MainView.axaml.cs`, `MacWindow.axaml.cs`, `MainViewModel.cs`, and the obsolete `ElementPropertyTabViewModel` / `InlineAnimationLayerViewModel` once their replacements are wired. | |

**Sequencing note:** 2.1 and 2.2 touch the published extensibility surface → run `beutl-design-reviewer` on each; state the migration in each PR's `BREAKING CHANGE:` footer.
**Exit criteria:** `grep -r "\[Obsolete" src/` returns only attributes added intentionally during this plan (target: zero); no `CS0618` suppressions remain.
**Risk:** medium (plugin-facing breaks) but mitigated by strong Engine/Core test cover.
**Estimated size:** 5–6 PRs.

## Phase 3 — Utility & conversion-layer consolidation (behavior-preserving)

**Goal:** one canonical home for each cross-cutting utility, especially the Beutl↔Avalonia type-conversion seam.

| # | Task | Scope |
|---|------|-------|
| 3.1 | **Unify `AvaloniaTypeConverter`**: merge `src/Beutl.Controls/AvaloniaTypeConverter.cs` (basic ToAva/ToBtl) and `src/Beutl/Helpers/AvaloniaTypeConverter.cs` (gradient/geometry/composition adapters) into a single conversion layer in `Beutl.Controls` (the lowest UI project that may reference both type systems). Delete the duplicate; update call sites in the same change. | Two sibling classes, overlapping methods, no namespace separation. |
| 3.2 | **Centralize Point/Size/Rect/Matrix conversions** currently duplicated across `Beutl.Controls` and `Beutl` extension-method families into that same layer. | |
| 3.3 | **Converter organization**: co-locate the 30+ `JsonConverter`/`TypeConverter` implementations by domain (decide: keep per-project but add a registration index, or introduce a lightweight registry). Document the chosen rule in `src/Beutl.Core/CLAUDE.md`. | `Beutl.Engine/Converters/`, `Beutl.Core/JsonConverters/`, `Beutl.Editor.Components/Converters/`. |
| 3.4 | Fix `src/Beutl.Core/OptionalJsonConverter.cs:16` (missing `JsonArray` support) with a regression test — known serialization hole in a load-bearing converter. | |

**Exit criteria:** exactly one Beutl↔Avalonia conversion entry point; converter placement rule documented; no duplicate conversion helpers.
**Risk:** low–medium (serialization touchpoints — covered by Core tests; add tests where missing).
**Estimated size:** 3–4 PRs. Commits: `refactor:`.

## Phase 4 — Safety-net expansion (tests only, no production changes)

**Goal:** build the test cover that Phases 5–7 need. This phase is the gate: **structural surgery on untested code is forbidden until its line in this table is done.**

| # | Task | Why |
|---|------|-----|
| 4.1 | **NodeGraph core tests**: cover `GraphNode`, connection validation, evaluation order in `tests/Beutl.UnitTests/` (only mutation-service tests exist today). Prerequisite for the 7.3 API redesign. | Core graph logic untested. |
| 4.2 | **ViewModel test harness**: stand up headless-Avalonia (or pure-VM, Avalonia-free where possible) testing for `Beutl/ViewModels/`. First targets: `PlayerViewModel` playback-state transitions and `TimelineTabViewModel` layout math — characterization tests written *against current behavior*, as the safety net for Phase 5. | Zero VM tests today; these are the Phase 5 subjects. |
| 4.3 | **Beutl.Extensibility contract tests**: lifecycle of extension load/registration with fake extensions. Prerequisite for any future extensibility changes. | Plugin seam untested. |
| 4.4 | **Beutl.Api tests** with a mocked HTTP handler for the service classes (`LibraryService`, `DiscoverService`). | Untested HTTP layer; also pre-work for the 7.4 async migration. |
| 4.5 | Convert `tests/Beutl.Graphics3DTests` from a console runner into a real NUnit project so it counts in CI, and include non-UnitTests coverage files in the report (`.github/workflows` currently reports only `Beutl.UnitTests` coverage). Requires explicit approval before touching workflows (AGENTS.md rule 5). | CI blind spot. |

**Exit criteria:** characterization tests exist for every Phase 5 target; NodeGraph/Extensibility/Api have first-class suites; coverage report includes all test projects.
**Risk:** low (test-only). **Estimated size:** 5–7 PRs. Commits: `test:`.

## Phase 5 — God-class decomposition (highest payoff: churn × size)

**Goal:** split the three high-churn god classes into orthogonal units, guided by the Phase 4 characterization tests. Behavior-preserving.

| # | Task | Decomposition |
|---|------|---------------|
| 5.1 | **`PlayerViewModel` (1,642 L, top churn)**: extract a non-reactive, thread-safe `AudioPlaybackEngine` (OpenAL/XAudio2 backend selection, buffering, playback direction/speed) — candidate home: `Beutl.Engine` audio area or a new `Audio.Playback` namespace; `PlayerViewModel` becomes a thin reactive wrapper. | Audio-thread management is currently interleaved with reactive UI state. |
| 5.2 | **`PlayerView.axaml.MouseControl.cs` (1,946 L)**: replace the nested per-mode handler classes with an `IMouseInteractionHandler` strategy + mode state machine (pan/zoom, transform-gizmo, scrubbing); move gizmo math into a testable, Avalonia-free service in `Beutl.Editor`. | Three interaction modes in one file. |
| 5.3 | **`TimelineTabViewModel` (1,174 L)**: extract `TimelineLayout` (pure scale/offset/keyframe-position math — unit-testable) and a drag-interaction service; child-VM management stays in the VM. | Layout math mixed with command dispatch. |
| 5.4 | **`EditViewModel.CommandHandler.cs`** (2nd-highest churn): audit after 5.1–5.3 land; fold its command dispatch into `Beutl.Editor` services where they belong (`IElement*Service` family) instead of VM-resident handlers. | Churn signal; service layer already exists. |

**Exit criteria:** no file in the decomposed set exceeds ~600 lines; extracted logic (gizmo math, timeline layout, audio engine) has direct unit tests in `tests/Beutl.UnitTests/`; characterization tests from 4.2 still green.
**Risk:** medium-high (hot UI paths) — mitigated by the Phase 4 gate.
**Estimated size:** 6–8 PRs, one extraction at a time. Commits: `refactor:`.

## Phase 6 — Layering corrections (project-boundary moves)

**Goal:** make the module-boundary map in `AGENTS.md` true.

| # | Task | Move |
|---|------|------|
| 6.1 | **Evict rendering integration from `Beutl.Controls`**: move `HdrBitmapView` (owns `VulkanSwapchainRenderer`), `BitmapView`, `CurveVisualizationRenderer`, and other Engine-coupled controls to `Beutl.Editor.Components`. Goal: `Beutl.Controls` drops its `Beutl.Engine` ProjectReference entirely. | Controls → presentation-only. |
| 6.2 | **Hoist non-UI services out of `Beutl.Editor.Components`** into `Beutl.Editor` where they have no Avalonia dependency; for the few that need a platform gateway (clipboard, etc.), keep a thin interface in `Beutl.Editor` and the Avalonia adapter in Components. | Improves testability; matches the existing `IElement*Service` pattern. |
| 6.3 | Re-derive the dependency edge list (0.1) and assert the intended layering in a NetArchTest-style unit test (or a simple csproj-parsing test) so violations fail CI rather than accumulate again. | Permanent guardrail. |

**Exit criteria:** `Beutl.Controls.csproj` has no `Beutl.Engine` reference; architecture test enforcing the layer rules is green in CI.
**Risk:** medium (large mechanical moves, namespace churn — `refactor!:` if any moved type is public plugin surface).
**Estimated size:** 3–4 PRs.

## Phase 7 — Cross-cutting design unification (highest risk, design-review-heavy)

**Goal:** resolve the systemic overlaps. Each item here is big enough to warrant the Spec-Kit flow (`/speckit-specify → plan → tasks`) per `AGENTS.md`; treat the entries below as the spec backlog, not as directly implementable tasks.

| # | Item | Problem statement |
|---|------|-------------------|
| 7.1 | **Property-observation channel audit**: properties are observable via three channels (`PropertyChanged`, `CoreProperty.Changed` observable, `ReactiveProperty`). Define which channel each layer uses (Engine: CoreProperty; VM: Reactive; bridge: one adapter), document it in `.claude/rules/csharp.md`, and migrate mixed-channel hotspots. | Triple observation paths confuse serializers and undo/redo observers. |
| 7.2 | **Serialization map**: `ICoreSerializable` (project files) vs `IJsonSerializable` (UI state) vs ad-hoc `JsonConverter`s vs the generated `Resource` diff pattern serve different purposes but are undocumented as a system. Write the map, then eliminate any genuine overlap found. | Maintenance complexity, not necessarily redundancy. |
| 7.3 | **NodeGraph API redesign**: execute the `GraphNode.cs:101` TODO (`AddInput/AddOutput/AddProperty`) as a proper `refactor!:` with the 4.1 test suite as the net. Plugin-facing → `beutl-design-reviewer` + `BREAKING CHANGE:` footer. | Self-identified API instability. |
| 7.4 | **`System.Interactive.Async` → `System.Linq.Async` migration** in `Beutl.Api` services (3 blocked TODOs) once upstream allows; 4.4 tests are the net. | Known migration blocker. |
| 7.5 | **`Hierarchical` multiple-parent support** (`src/Beutl.Core/Hierarchy/Hierarchical.cs:7`): evaluate, then either spec it or explicitly reject it and delete the TODO. A core object-model change — do not start before everything above is stable. | Deepest-reaching open design question. |

**Exit criteria:** per-item specs merged under `docs/specs/`; implementations land one at a time with their own exit criteria.
**Risk:** high — that is why it is last, behind every guardrail this plan builds.
**Estimated size:** one Spec-Kit cycle per item.

---

## Sequencing rationale

```
Phase 0 (guardrails)
  └─ Phase 1 (mechanical)         — trivial, clears noise from later diffs
       └─ Phase 2 (obsolete APIs) — breaking but inside the well-tested zone
            └─ Phase 3 (utilities)— consolidation while surface is fresh
Phase 4 (tests) ──────────────────┐ can start in parallel with Phases 1–3
       └─ Phase 5 (god classes)   ← gated on 4.2
            └─ Phase 6 (layering) ← easier after 5.x shrinks the moved files
                 └─ Phase 7 (design unification) ← needs every net built above
```

- Phases 1–3 stay inside the strong Core/Engine/Editor test net; Phase 4 builds the net where it is missing; Phases 5–7 only then enter the untested zones.
- Phase 2 before Phase 5/6: deleting obsolete APIs first means the structural moves never have to carry deprecated members along.
- Phase 4 can run concurrently with 1–3 (different files, different reviewers).

## What this plan deliberately does NOT do

- **No big-bang rewrite.** The dependency graph is acyclic and the stack is modern; the debt is local (3 god classes, 1 layering violation, 24 obsolete members, duplicated utilities), so the plan is local too.
- **No new "v2" projects or parallel hierarchies** — forbidden by the design policy; every change migrates call sites in the same PR.
- **No CI workflow changes without explicit approval** (affects 4.5 — flagged there).
- **No GPL/MIT boundary changes.** `Beutl.FFmpegWorker` stays behind IPC.

## Progress tracking

| Phase | Status | Started | Completed | Retro |
|-------|--------|---------|-----------|-------|
| 0 | not started | — | — | — |
| 1 | not started | — | — | — |
| 2 | not started | — | — | — |
| 3 | not started | — | — | — |
| 4 | not started | — | — | — |
| 5 | not started | — | — | — |
| 6 | not started | — | — | — |
| 7 | not started | — | — | — |
