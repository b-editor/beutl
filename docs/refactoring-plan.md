# Beutl Full Refactoring Plan (Phased)

> Status: **proposed** — 2026-06-11
> Source: multi-agent waste audit of the entire repository (80 agents: 13 subsystem
> surveys + 2 re-runs, 60 adversarial verifications, 4 gap surveys). 175 findings
> total; the 60 highest-risk findings were adversarially verified — **59 confirmed,
> 1 refuted**. Unverified findings (medium/low severity) must be re-verified at the
> top of the PR that addresses them.

## Overall diagnosis

The codebase (~210k LOC across 23 `src/` projects) is not rotten — it is carrying
the residue of **several design migrations that stopped halfway**. The waste
clusters into four themes:

1. **Migration leftovers** — old-generation mechanisms coexisting with their
   replacements: two property systems (`CoreProperty` registry vs Engine
   `IProperty`), three serialization contracts, the legacy `Pcm<T>` DSP surface,
   the obsolete `MediaWriter`/`EncoderRegistry` encoder pipeline, a never-built
   in-process FFmpeg configuration.
2. **Unfinished layering** — the `Beutl.Editor` / `Beutl.Editor.Components`
   extraction (PRs #1612, #1845) stalled halfway: the ~11k-line property-editor
   subsystem still lives in the untestable `src/Beutl` app project, and 45
   `HistoryManager.Commit` call sites bypass the mandated service layer.
3. **Copy-paste drift that has already become bugs** — GraphEditor keyframe paste
   inserts duplicates, `DelayNode` keeps stale state across forward seeks,
   `MacWindow` misses fixes applied to `MainView`.
4. **Undetected death** — ~10k+ LOC of verified-dead code, ~27 MB of dead binary
   assets (unreachable UI font weights + stale dylibs), and one silent feature
   loss: the Script filter-effect node group is never registered and is unreachable
   from the UI. **Correction (Phase 1 verification): the original "~80 MB" figure
   conflated the removable UI weights/dylibs with the ~27 MB test-assembly font
   family, which is LIVE (loaded by `tests/Beutl.UnitTests/Engine/TypefaceProvider.cs`)
   and was not removed — see Phase 1 *Assets and tests*.**

**Expected outcome**: −15–20k LOC, −~27 MB of binary assets (revised down from the
original ~80 MB estimate by Phase 1 verification — see Phase 1 *Assets and tests*),
CI build time roughly halved, and the in-flight layering migration completed. Total
effort ≈ 5–7 person-months; Phases 3⇄4 and 5⇄6 can run in parallel.

---

## Phase 0 — Immediate fixes for verified bugs

*Size: days. Commits: `fix:`. Independent of all refactoring decisions.*

| Bug | Location |
|---|---|
| Script filter-effect node group built but never registered (`AddGroup` API trap); nodes unreachable from node-add menu and library search | `src/Beutl.NodeGraph/Nodes/NodesRegistrar.cs:68-71`, `GraphNodeRegistry.cs:245-263` |
| GraphEditor keyframe paste only Inserts (Inline path Removes+Inserts) → two keyframes at the same `KeyTime` | `GraphEditorViewModel.cs:386-388` |
| `DelayNode` reset condition (`_lastTimeRangeStart > TimeRange.Start`, tracker updated only inside the reset branch) keeps stale delay-line state across forward seeks | `src/Beutl.Engine/Audio/Graph/Nodes/DelayNode.cs:37` |
| `Beutl.Extensions.AVFoundation` unconditionally sets `Optimize=false` → Release ships unoptimized IL | its csproj |
| `viewMenuItem.SubmenuOpened` handler registered twice in `MainView` | `MainView.axaml.cs` |
| `PathOperationEditorViewModel` copy-paste bug hidden by a silent `catch {}` | `src/Beutl/ViewModels/Editors/` |
| MIT `Beutl.csproj` takes a dev ProjectReference to GPL `Beutl.FFmpegWorker` without `ReferenceOutputAssembly=false` | `src/Beutl/Beutl.csproj` |

Some root causes (the `AddGroup` API shape, the reset-condition unification) get
their durable fix in Phases 3–4; Phase 0 applies minimal targeted fixes plus
regression tests.

## Phase 1 — Dead code and dead asset sweep

*Size: 1–2 weeks. Deletes ~10k+ LOC and ~27 MB of assets. Items below were
adversarially verified unless marked otherwise; **Phase 1 execution re-verification
(PRs #1895–#1910) refuted several originally-listed items, corrected inline below**
(the dead-style-theme, package, audio `Pcm<T>`, font-asset, and `HistoryManager`
entries — re-verify every "dead" claim at deletion time, per the cross-phase rule).
Engine/Extensibility deletions are plugin-facing: batch them into one `refactor!:`
release train with a `BREAKING CHANGE:` footer listing affected projects.*

**Engine**
- Legacy encoder pipeline: `MediaWriter`, `IEncoderInfo`, `EncoderRegistry`,
  `EncodingExtension` (all `[Obsolete]`, zero implementers) and the obsolete
  `PageExtension` + its live app-shell plumbing; the empty `LayerExtension`.
- Legacy `Pcm<T>`/`ISample` DSP surface — **correction (Phase 1 verification): only
  the 3 never-used sample structs are actually dead** (removed in PR #1906). The rest
  of `Pcm<T>`/`IPcm`/`ISample` is **live** at the audio I/O boundaries; full retirement
  and the latent `Slice` bug move to Phase 3 (see *3c-bis. Audio I/O type convergence*).
- `[Obsolete]` byte-LUT chain: `LookupTable`, `FilterEffectContext` overloads,
  `EffectTarget` obsolete members.
- Dead public surface: `ICanvas`/`INode` interfaces, the ~900-line `TypeConverter`
  cross-type conversion matrices (no production consumer, contains copy-paste
  bugs), `IKeyFrame.SetParent/GetParent`, `TransformParser.Parse`,
  `GraphicsException`, `FpsText`/`IRenderer.DrawFps`, 8 of 15 `AudioMath` helpers,
  csproj-excluded `VertexMode.cs`, and `RenderNodeCache` test-only members.

**Core / Controls / App / Api**
- `UnmanagedArray`/`UnmanagedList` (759 lines, already excluded from compilation),
  `JsonHelper.JsonSave2/JsonRestore2`, `IProjectItemGenerator`,
  `ViewConfig._primaryProperties` (write-only).
- **`BcTabView` cluster, 1,614 lines** (`BcTabView/`, `BcTabItem/`,
  `BcTabItemContainerGenerator`, `ItemDragBehavior`, `TabControlExtensions`, two
  style files, two `ResourceInclude` lines). Repo-wide grep confirmed zero external
  consumers; all remaining references are internal to the cluster itself.
- `DirectoryTreeView` (905 lines, zero references — re-confirm at deletion time),
  `MultiplyConverter`, and the dead style-only `SegmentedControl` theme
  (`SegmentedTabStrip`/`SegmentedTabStripItem`, zero consumers). **Correction
  (Phase 1 verification): `FlipButton` and `SimpleBreadcrumbBar` are LIVE and were
  NOT removed** — `FlipButton` is the base `ControlTheme` for the property-editor
  flip buttons (`BasedOn="{StaticResource FlipButton}"` in
  `PropertyEditorResources.axaml`), and the `simple-breadcrumb-bar` style class plus
  the `SimpleLargeBreadcrumbBar*` themes back 5+ settings pages (Editor / Extensions /
  KeyMap / Telemetry / View settings, decoder/extension-priority pages, et al.).
  Do not delete them.
- Dead observable machinery (`PackageManager.GetObservable`/`IsLoaded`,
  `PackageInstaller.GetObservable`), the storage-usage endpoint chain, the startup
  guard that waits on a process name that no longer exists.
- Unused PackageReferences and orphan `PackageVersion` pins in
  `Directory.Packages.props`. **Correction (Phase 1 verification, PR #1896):
  `ReactiveUI.Avalonia`, `OpenTelemetry` + `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  and `System.Interactive` are LIVE and were kept** — `ReactiveUI.Avalonia` is
  referenced by `Beutl`; the OpenTelemetry core/OTLP packages by `Beutl` / `Beutl.Api` /
  `Beutl.PackageTools.UI`; `System.Interactive` by `Beutl` / `Beutl.Api` (re-verify
  the exact call sites before any future removal). Only `System.Interactive.Async`,
  the Console/Zipkin exporters, and orphan pins were removable. Keep
  `Microsoft.Extensions.DependencyInjection` in `Beutl.Core`/`Beutl` — Phase 5 uses it.

**Media backends**
- The never-built in-process FFmpeg variant: `FFmpegOutOfProcess` csproj matrix,
  `#if !FFMPEG_OUT_OF_PROCESS` forks in ~12 files, `FFmpegPath.cs`, the duplicated
  loader. **Done.** PR #1909 removed `FFmpegPath.cs` and the dead
  `#if !FFMPEG_OUT_OF_PROCESS`/`#else` branches; the deferred follow-up — collapsing
  the `FFmpegOutOfProcess` csproj matrix and the `FFMPEG_OUT_OF_PROCESS` define — has
  since landed, so the out-of-process/worker path is now unconditional (no
  `FFmpegOutOfProcess`/`FFMPEG_OUT_OF_PROCESS` references remain in `src/`).
- Worker legacy (non-ring-buffer) `ReadVideo` path (unreachable).
- MediaFoundation rot: dead `MFDecoder` audio path + `MFSampleCache` audio caching
  (`MFReader` uses NAudio), hardcoded-off DXVA2 machinery, the `Vortice.Direct3D9`
  dependency, **and the user-visible no-op settings they expose**.
- Dead FFmpegIpc protocol surface: `QueryDefaultCodec`, `EncodeProgress`,
  `Handshake`, never-populated/never-read response fields.

**Assets and tests**
- Unreachable embedded NotoSansJP UI weights (~27 MB; `src/Beutl/Resources/Fonts/`
  now ships only the 4 weights actually used); stale `libBeutlAVF.dylib` copies under
  `src/Beutl/runtimes/`; byte-identical fat dylib duplicated across osx-x64/osx-arm64
  RID folders; orphaned images. **Correction (Phase 1 verification): the ~27 MB
  NotoSansJP/Roboto font family under `tests/Beutl.UnitTests/Assets/Font/` is LIVE** —
  `Engine/TypefaceProvider.cs` loads all of it and it is embedded via
  `<EmbeddedResource Include="Assets\**\*.*" />`, so it was NOT removed. The original
  "~80 MB" headline conflated this live test font family with the removable UI
  weights; the real removable total is ~27 MB.
- Tests that exist solely to keep dead code alive — delete **in the same PR** as
  the production code: the `GraphicsException` / `StringHash` /
  `BaseUriHelper.FindBaseUri` fixtures. **Correction (Phase 1 verification):
  `HistoryManager.SuppressRecording` is LIVE (6 internal callers in
  `HistoryManager.cs`) — do not remove it.** The remaining `HistoryManager` API
  review (`BeginRecordingScope`, `ExecuteInTransaction`, `PeekUndo`/`PeekRedo` and the
  ~670 lines of `HistoryManagerTests` covering them) moves to **Phase 4** — the editor
  layering / Commit-routing migration owns the `HistoryManager` surface — not the
  Phase 1 dead sweep.
- `tests/KeySplineEditor` leftover prototype app.

## Phase 2 — Build & test foundation

*Size: 1–2 weeks. Makes every later phase faster and safer.*

- **Drop the blanket dual-TFM default**: `Directory.Build.props` defaults to
  `net10.0`; only the 2 projects with compile-time Windows code plus the exes Nuke
  publishes as `-windows` opt in. Halves restore/compile work in CI and locally.
  ⚠️ Touches `.github/workflows` → **requires explicit approval first**
  (mandatory rule 5).
- **Reorganize `tests/`**: only 3 of 13 projects are real NUnit suites. Move the 7
  interactive previewer/sample apps to `tools/` or `samples/` and out of the CI
  build. Fix the `tests/ArtifactProvider.cs` force-include (11 projects carry
  opt-out boilerplate; 1 uses it).
- **Consolidate test scaffolding**: the Scene/HistoryManager scaffolding
  copy-pasted across 25 Editor test files, the `AddElement` helper duplicated 9×,
  the ~731 lines of `TestCoreObject`/`TestEngineObject` doubles re-declared in
  12+ files, and the triplicated audio-node test doubles → one shared helper
  layer.
- **Unblock untestable layers**: add a `Beutl.Editor.Components` reference to
  `Beutl.UnitTests` (prerequisite for Phase 4).
- **Make fake test gates real**: ✅ done. `Beutl.Graphics3DTests` was an
  `OutputType=Exe` console script with zero assertions and `SourceGeneratorTest`
  was a compile-only library with zero `[Test]` methods (absent from `Beutl.slnx`,
  so CI never ran it). Both are now real NUnit suites: `Beutl.Graphics3DTests`
  renders its PBR-grid / shadow scenes and asserts framebuffer pixel invariants
  (Vulkan-gated via a local `Assert.Ignore` skip-helper); `SourceGeneratorTest`
  runs the engine generators through a `CSharpGeneratorDriver` harness and asserts
  on the generated output (keeping `Class1.cs` + the 226-line stubs as the
  generator input), and is now wired into `Beutl.slnx` so CI runs it.
- Unify coverage collection (`coverlet.collector` only in `Beutl.UnitTests`;
  CI threshold disabled at 0) and hoist the ~5 copy-pasted MSBuild blocks
  (RID narrowing ×5, sideload output ×3, ResXGenerator assets ×5, AVF dylib
  copy ×2) into `Directory.Build.targets`.
- Mechanical cleanups while in the area: 108 legacy `ClassicAssert` call sites,
  fixture naming drift, the `*ExtraTests` second-fixture pattern.

## Phase 3 — Engine core consolidation

*Size: 6–10 weeks; the heaviest phase. Everything here is plugin-facing surface:
route public-API changes through `beutl-design-reviewer`, use `refactor!:` +
`BREAKING CHANGE:` footers, never add compat shims.*

**3a. Property plumbing unification (do first; M)**
Extract `PropertyBase<T>` from the token-identical ~200 lines shared by
`SimpleProperty<T>` and `AnimatableProperty<T>` (value storage, validation,
hierarchy/edited wiring, expression evaluation, serialization), with
`AnimatableProperty<T>` adding only the animation slot. Fixes the verified drift
in one stroke: the double-validate in `SimpleProperty.ValidateAndCoerce`, the
`DeserializeValue` `IReference`-resolution divergence, and `ToAnimatable` dropping
the validator. Centralize the `ValidationAttribute → IValidator` conversion
(currently 4 copies).

**3b. End-state for the two property systems (XL; 🚩 decision gate)**
`CoreProperty` registry ceremony: ~145 hand-written registrations across 44 files
(~1,450 lines) vs the Engine `IProperty` model (467 uses). Options:

- **(A)** Migrate the remaining `CoreObject` users (Configuration, ProjectSystem,
  NodeGraph, encoder settings) to `IProperty`. Verified constraint:
  `Beutl.Configuration` references only `Beutl.Core` (Engine depends on
  Configuration), so this requires dependency restructuring.
- **(B)** Keep `CoreProperty` for non-engine objects and add a Roslyn source
  generator (in `Beutl.Engine.SourceGenerators`) that emits the static field +
  registration + CLR wrapper from one annotated partial-property declaration.

Drive the decision through `/speckit-specify`. **Leaving both idioms as the
permanent answer is the one option that is off the table.**

**3c. Audio graph scaffolding (L)**
Extract the per-node copy-paste into the graph layer: a chunked multi-parameter
`AnimationSampler` helper (one chunk-size constant — currently 8192 in `GainNode`
vs 1024 elsewhere), a `DiscontinuityTracker` for the `_lastSampleRate` /
`_lastTimeRange` reset idiom, and centralized parameter sanitization so **every**
animated node gets `[Range]` enforcement (today only `CompressorNode` sanitizes).
Migrate Gain/Delay/Compressor/Equalizer in the same change; new effects then
implement only the per-sample kernel. Also collapse the seven hand-rolled
`AudioContext.Create*Node` reuse blocks onto the existing generic
`CreateNode<TNode,TParams>` (note: the generic is **live** — `SceneSound` calls
it — and `AudioContext` is plugin-facing via `AudioEffect.CreateNode`).
Add the missing tests: Equalizer/Resample/Mixer/Clip/Shift/Source nodes currently
have zero coverage.

**3c-bis. Audio I/O type convergence (`Pcm<T>` → `AudioBuffer`; XL; 🚩 plugin-contract break)**
The DSP graph and composition already run on `AudioBuffer` (planar float32, N-channel);
`Pcm<T>`/`IPcm`/`ISample` (interleaved, sample-type-parameterized: mono/stereo ×
int16/int32/float) survive only at four I/O edges: decoder output
(`MediaReader.ReadAudio(out Ref<IPcm>)`), the public `ISampleProvider.Sample` contract
(the live `EncodingController` consumes audio through it), device output
(`AudioBuffer.ToPcm()` → `Pcm<Stereo32BitFloat>`, queued as float32 for XAudio2;
converted to `Stereo16BitInteger` only for the OpenAL backend), and the GPL
worker IPC (`IpcSampleProvider`). Converge on `AudioBuffer` as the single in-memory audio
type: add interleave + sample-format helpers to `AudioBuffer` (planar float32 →
interleaved float32 for XAudio2, → interleaved int16 for OpenAL; the FFmpeg/AVF
encoders take interleaved **float** and let their own `SampleConverter` pick the
target `AVSampleFormat`, so codec output keeps its existing float/sample-format
choices), flip `ISampleProvider.Sample` and
`MediaReader.ReadAudio` to `AudioBuffer` (decoders deinterleave codec output), route
device/codec output through the new helpers, ship planar float over IPC (make the
IPC protocol/buffer sizing channel-aware — today it is stereo-hardcoded,
`SampleRate * 8` bytes), then delete
`Pcm<T>`/`IPcm`/`ISample`/the sample structs/`Convert`/`AudioBuffer.ToPcm` (this also
removes the latent `Pcm<T>.Slice` lifetime bug). Breaks the published `ISampleProvider`
and `MediaReader.ReadAudio` plugin contracts and the IPC surface → `refactor!:` +
`BREAKING CHANGE:`, no `[Obsolete]` shims, route through `beutl-design-reviewer`; drive
via `/speckit-specify`. Pairs with 3f (audio device backends). This **corrects the Phase 1
premise** that `Pcm`/`ISample` was already dead.

**3d. Animation hierarchy and editing-logic placement**
Decide (🚩) whether to collapse the five-layer abstraction
(`IAnimation → IAnimation<T> → IKeyFrameAnimation → KeyFrameAnimation →
KeyFrameAnimation<T>`, exactly one implementation, ~20 downcast bypass sites) or
commit to it and fix the downcasts. Unify the two generations of integer
animators. Move editor-interaction logic out of the Engine: `SplineEasingHelper.
Move/Remove`, `KeyFrameDeltaHelper` → `Beutl.Editor`, removing the reflection
dispatch and `InternalsVisibleTo` reliance. Relocate the `Beutl.Editor`-namespace
types physically living in `Beutl.Core` (incl. the identical `PublishingSuppression`
/ `RecordingSuppression` copy-paste pair).

**3e. Graphics deduplication**
`Renderer.RenderDrawable` / `UpdateFrame` / `RecalculateBoundaries` triplicated
node-cache/render loop; `DrawableGroup` vs `DrawableDecorator` render plumbing and
the three copies of alignment/transform math; the static SKSL shader-compilation
boilerplate repeated in ~10 filter effects; the color-matrix pool
`Get/try/finally` block repeated 7× in `FilterEffectContext`;
`GraphicsContext2D.Clear`'s defunct Equals-based diff; `RenderNodeProcessor.
Rasterize` vs `RasterizeToRenderTargets`.

**3f. Media deduplication**
Merge `AnimatedImageReader` / `AnimatedPngReader` (parallel implementations of the
same frame-lookup/cache/compositing algorithm — the same hang-fix was applied to
both); unify the triplicated `MediaSource` resource-sharing logic and the six
near-identical `FileSourceJsonConverter` subclasses; fix `FormattedText` double
shaping and the duplicated tag-type ladder; `SoundGroup.Compose` triplication.
Move audio playback device backends (OpenAL/XAudio2) out of `Beutl.Engine`
(`Vortice.XAudio2` is currently referenced on all platforms).

**3g. Serialization consolidation**
Make `ICoreSerializable`/`JsonSerializationContext` the single contract.
Migrate the legacy `IJsonSerializable` (`WriteToJson`/`ReadFromJson`) — anchored in
`Beutl.Extensibility`'s `OutputExtension`/`PropertyEditorExtension` — and its ~12
implementers in one change. 🚩 This is a **published plugin contract break**:
decide whether it gets a deprecation window (the one sanctioned exception in
repo policy) or an immediate break. Collect the scattered version-compat forks
(`"$type"`/`"@type"`, `"font"`/`"Font"`, 1.x discriminator patching) into a single
upgrade step. Fold the stringly-typed `Preferences` store into
`GlobalConfiguration`. Extract one atomic tmp+rename save helper (currently
copy-pasted twice; two other save paths drifted and are non-atomic).

## Phase 4 — Finish the editor layering migration

*Size: 5–8 weeks. Continues the #1612 → #1845 trajectory; direction is already
decided, this phase executes the remainder. Can partially overlap Phase 3.*

- **4a. Move the property-editor subsystem (~11.3k lines)** —
  `src/Beutl/ViewModels/Editors` (55 files), `src/Beutl/Views/Editors` (81 files),
  `PropertyEditorService` (524 lines) — into `Beutl.Editor.Components`, sinking
  Avalonia-free logic (matching/grouping, including the ~50 lines stranded in
  `PropertyEditorFactoryAdapter`) into `Beutl.Editor`. Delete the two app-side
  adapters and implement `IPropertyEditorFactory`/`IPropertiesEditorFactory` next
  to the editors. Replace the T4 template with the repo's Roslyn source-generator
  infrastructure. The seven identical private `Visitor` records die here too.
- **4b. Finish Commit routing (#1845)**: extract keyframe-mutation, path-point,
  and curve services into `Beutl.Editor/Services` (mutate + single Commit per user
  operation) and migrate the 45 inline `HistoryManager.Commit` sites. Exemplar
  drift to kill: `GraphEditorView.axaml.cs` re-implements the Scene Start/Duration
  drag that `SceneTimeRangeService` already owns. NUnit tests per service (the
  already-migrated services all have them — keep that bar). Also fold in the
  `HistoryManager` API review deferred from Phase 1: confirm whether
  `BeginRecordingScope` / `ExecuteInTransaction` / `PeekUndo` / `PeekRedo` (currently
  test-only callers) gain production callers here or should be removed with their
  tests; `SuppressRecording` is live (6 callers) and stays.
- **4c. Keyframe clipboard consolidation**: collapse the 4 copy-paste sites
  (byte-identical `CopyAsync` pairs, duplicated ~70-line notification switches)
  into an extended `IKeyFrameClipboardService`; pick one replace-vs-insert
  behavior (makes Phase 0's targeted fix durable); add the missing
  `BeutlClipboardFormats` constants. Also unify the three coexisting clipboard
  access patterns.
- **4d. Relocate editor infrastructure**: `FrameCacheManager`, `BufferedPlayer`,
  `IPlayer`, `EditorClockImpl`, `TimelineOptionsProviderImpl`,
  `EditorSelectionImpl` → `Beutl.Editor` (tests for cache eviction and clock
  recalculation). Rescue `ContextCommandManager` / `ContextCommandHandlerRegistry`
  (keyboard shortcuts, Avalonia-typed) out of `Beutl.Api` so the server-API
  project stops being an editor-UI dependency.
- **4e. `EditViewModel` service registry**: replace the 25-branch hand-rolled
  `GetService` with a small registry in `Beutl.Editor` (Type → factory, lazy);
  doubles as a plugin seam for replacing individual services.
- **4f. Timeline cluster cleanup** (from the re-run survey; re-verify per item):
  split `TimelineTabViewModel` (1,174 lines: layer lifecycle, nudge debouncing,
  inline-animation coordination, frame-cache UI); extract `ElementViewModel`'s
  ~250 lines of thumbnail/waveform async composition into a composer service;
  unify pixel↔frame conversion math (verified rounding-policy drift between
  `TimelineTabView` and `ElementView`) into one coordinate-space helper; give the
  8 tool tabs a base class owning the CompositeDisposable / `GetService` /
  view-state plumbing; replace the ~25 silent `catch {}` blocks around view-state
  ReadFromJson/WriteToJson with one logging call site.
- **4g. Migrate the last tabs**: Output tool tabs and the History tab
  (16 of 17 sibling tabs already live in `Beutl.Editor.Components`).

## Phase 5 — App-shell DI and unification

*Size: 3–5 weeks. Start after Phase 4 stabilizes service homes.*

- **Composition root**: pick constructor injection as the single idiom.
  Build a Microsoft.Extensions.DependencyInjection root in `App` (package already
  referenced; no `ServiceCollection` exists anywhere today), register
  `EditorService` / `ProjectService` / `OutputPresetService` / `Telemetry` /
  notification handler, and migrate the ~150 static `.Current`/`.Instance` call
  sites. The per-`IEditorContext` `IServiceProvider` is scope-separation, not a
  competitor — keep it for editor-scoped services. Delete the `AppHelper` static
  delegate bridge.
- **Unify the `MacWindow` fork**: ~350 duplicated lines (byte-identical
  `OnClosing`/capture-drain/bounds persistence, item-for-item copies of
  `InitializeRecentItems`/`InitExtMenuItems`/`OpenToolWindowAsync`, 177 lines of
  duplicated menu XAML) with demonstrated drift (MacWindow still has the
  `IsEnabled` workaround MainView fixed; never disposes its DynamicData
  subscriptions). Extract shared shell behavior + an extension-menu service with
  thin `MenuItem`/`NativeMenuItem` adapters (NativeMenu lacks ItemsSource).
  ⚠️ `MacWindow.axaml.cs` has uncommitted local changes (bounds-restore rewrite)
  — coordinate before starting.
- **Settings UI**: helper/base for the ~25 hand-written config↔ReactiveProperty
  bridges (three base-class conventions today); collapse the page registry spread
  across five hand-maintained mappings (with entries for deleted pages) into one
  source of truth.
- **Satellite executables**: ExceptionHandler / WaitingDialog / PackageTools.UI
  are justified as separate processes (verified), but replace the
  `<Compile Include>` file-linking (`Telemetry.cs`, `LinuxDistro.cs`,
  `BeutlEnvironment.cs`, CS0436 pragmas) with a small shared library and unify the
  thrice-implemented theme/culture bootstrap.
- Startup hygiene: fix the fire-and-forget `async void` constructor chain; add
  unit tests for the now-injectable app-shell services (1000+ lines of pure logic
  currently untestable).

## Phase 6 — Beutl.Api split and media backends

*Size: 3–5 weeks. Can run in parallel with Phase 5.*

- **Split `Beutl.Api`** (only ~⅓ is actually the documented "server API client"):
  keep a pure REST client (Refit `Clients/`, `Objects/`, auth); move
  NuGet/plugin-loading into a new `Beutl.PackageManagement` project; move
  `ExtensionProvider` and extension settings next to `Beutl.Extensibility` or the
  app layer. Replace the `IBeutlApiResource`/`GetResource` service locator with
  constructor injection (call sites in `Beutl`, `Beutl.Editor.Components`,
  `Beutl.PackageTools.UI` in the same change; no shims). Fix the N+1 HTTP fan-out
  in every package-listing path, the sign-in copy-paste twins with locking drift,
  and the v1+v3 dual update-check path. Rename the typo surface
  (`Managemant`, `Refesh`, `Varify`, `Acceped`, `AuthorizedUser.cs` vs
  `AuthenticatedUser`). **Add tests — the project has zero coverage and a latent
  first-iteration-`return` bug survived in `CoreLibraries`.**
- **Media backends**: converge the three independent frame-cache/seek-threshold
  implementations on one shape; deduplicate the settings classes and the
  13-member acceleration enum + 12-arm switch (decoding vs encoding); fix
  `AVFDecoderInfo` advertising Windows-only formats (.wmv/.asf/.wma/.sami) pasted
  from MediaFoundation; make the three copy-pasted sync-over-async FFmpeg
  property editors async (they block the UI thread today).
- **Worker/IPC tests**: `VideoRingBuffer` concurrency, handler coverage, IPC
  protocol round-trip. (The worker CLAUDE.md claims process-level tests exist;
  they never have.)
- 🚩 **ColorPicker decision**: the 4.2k-line FluentAvalonia port
  (`FAColorPicker`, `ColorSpectrum`, 14 sibling files in the
  `FluentAvalonia.UI.Controls` namespace) — evaluate replacing with the upstream
  FluentAvalonia/Avalonia ColorPicker; if not feasible, document the maintenance
  burden. Same evaluation for `ProgressRing` and `OptionsDisplayItem`
  (vs `SettingsExpander`).

## Phase 7 — Localization, docs, re-audit

*Size: 1–2 weeks.*

- **resx consolidation**: 184 pure-duplicate keys across the 9 buckets (with
  user-visible ja drift in the non-pure ones), 22 dead keys, the Curves and
  AudioVisualizer vocabularies duplicated between `Strings` and `GraphicsStrings`
  (same-named key with drifted ja), hardcoded English UI strings
  (AudioVisualizerTab ×6, Window Capture ×~14), the PackageTools.UI silo with its
  divergent key style. Add a test for the 23 `MessageStrings` keys consumed only
  via reflection templating.
- **Docs drift**: module boundary map errors (Engine "no project dependencies";
  `Beutl.Editor` misclassified as UI), `InternalsVisibleTo` documented as 3 grants
  vs 14 actual (incl. one dead grant), NodeGraph docs routing to a test directory
  that never existed, FFmpegWorker CLAUDE.md's phantom tests, stale counts in the
  AI-workflow README, GPL/MIT boundary invariants drifting across three documents.
- ⚠️ CI workflow dedup (six copies of checkout/setup-dotnet boilerplate in the
  `build-*` family) — **only with explicit approval** (mandatory rule 5).
- **Re-audit**: run the same multi-agent audit workflow again; declare the program
  done when a round produces no new findings. Add analyzer rules to lock in the
  fixed patterns (ban empty `catch`, restrict `async void`, etc.).

---

## Cross-phase rules

- **Small PRs**: one finding-cluster per PR. Phases are an ordering guarantee, not
  a PR size.
- **Commit convention**: deletions and public-API changes use `refactor!:` with a
  `BREAKING CHANGE:` footer naming affected projects. No `[Obsolete]` shims, no
  "v2" duplicate types, no compat wrappers — migrate call sites in the same change
  (repo policy). The single sanctioned exception is 3g's `IJsonSerializable` if a
  deprecation window is chosen.
- **Gates per PR**: `dotnet build` + `dotnet test Beutl.slnx -f net10.0` +
  `dotnet format`; `beutl-design-reviewer` for public-surface changes; for every
  deletion PR, a final liveness grep (CoreProperty registrations, XAML, reflection,
  JSON discriminators, source generators) before merging.
- **Unverified findings**: the 59 adversarially verified findings are
  ready-to-execute. The remaining ~115 medium/low findings must be re-verified at
  the start of the PR that addresses them.
- **Decision gates (🚩)**: 3b property-system end-state, 3d animation hierarchy,
  3g `IJsonSerializable` break vs deprecation window, Phase 6 ColorPicker
  replacement. Decide at phase start; 3b should go through `/speckit-specify`.
- **Approval required**: Phase 2 dual-TFM change and Phase 7 workflow dedup touch
  `.github/workflows` (mandatory rule 5).
