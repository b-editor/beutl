# Editor / UI layer review

Scope: Avalonia ViewModels + XAML and the `Beutl.Editor` support types listed in the review request,
diffed with `git diff main...HEAD`. File contents read from the working tree (standalone branch under
review). The four standard axes plus the proxy-specific checks (compiled bindings, subscription
lifetime, recursive enumeration cycle guards, badge relevance gate, export preflight) were assessed.

## Severity summary

| Severity | Count |
|----------|-------|
| Blocker  | 0 |
| High     | 0 |
| Medium   | 2 |
| Low      | 2 |

The GPL/MIT boundary is clean (all proxy access flows through the `IProxy*` abstractions in
`Beutl.Engine`; the FFmpeg generator is reached only via `ProxyGeneratorRegistry`, never referenced
from a ViewModel). XAML compiled bindings are correct everywhere (see Clear). Subscription lifetimes
are disciplined — every event subscription and `ReactiveProperty` is registered on a
`CompositeDisposable`/`_disposables` and every store/queue handler is detached via `Disposable.Create`.
The recursive `ProxySourceEnumerator` walk has correct cycle guards (visited scenes / graph groups /
filter-effect groups). Fingerprint normalization used to match store events was verified consistent
between `EditViewModel.ElementUsesAnySource` (`ResolveComparableKey`) and
`ElementViewModel.ElementUsesChangedSource` (`TryFromFile().AbsolutePath`) — both reduce to
`NormalizeAbsolutePath(ResolveFinalPath(path))`, so symlinked sources match. Test coverage for the new
`Beutl.Editor` logic is strong (`ProxySourceEnumeratorTests`, `ExportSourceValidatorTests`,
`ElementViewModelProxyStateTests`, `ProxyStoreConfigTests`).

---

## [Medium] ProxiesTabViewModel.cs:427,679-707 — un-coalesced full-project rebuild per store/job event

`Refresh()` is invoked directly from `OnStoreChanged` (Registered/StateChanged/Deleted) and from
`OnJobChanged` on every terminal kind (Succeeded/Failed/Canceled/Skipped). Each `Refresh()` disposes
and recreates the entire `Clips` collection and re-runs `EnumerateProjectVideoSources()`, which walks
every scene/element in the project through `ProxySourceEnumerator` and calls
`ProxyFingerprint.TryFromFile` (a `FileInfo` + `ResolveLinkTarget` syscall) per source, all on the UI
thread.

Failure scenario: with the Proxies tab open during a "Generate All" over N clips, each completing job
raises roughly two events (job `Succeeded` + store `Registered`/`StateChanged`), so ~2N full
project-graph walks plus N-times-sources file stats plus 2N full clip-list rebuilds occur — O(N)
disk-touching UI-thread passes that scale with clip count. `EditViewModel` deliberately coalesces its
own proxy-invalidation burst into one UI-thread pass per tick
(`_pendingProxyInvalidations`/`FlushPendingProxyInvalidations`); this ViewModel does not, so the
timeline stays smooth while the Proxies tab does not.

Recommendation: debounce/coalesce `Refresh()` the same way `EditViewModel.FlushPendingProxyInvalidations`
does — collect the change kinds and post a single rebuild per UI tick. A lighter `RefreshJobs()`
(which already only mutates existing rows) can stay immediate; only the full `Refresh()` rebuild needs
coalescing.

## [Medium] ElementViewModel.cs:1063-1080 (ElementUsesChangedSource) — UI-thread file I/O in the badge relevance gate

The relevance gate that is supposed to *avoid* work itself performs disk I/O on the UI thread.
`OnProxyStateInvalidated(...)` marshals to the UI thread and then calls the static
`ElementUsesChangedSource(Model, e.Source.AbsolutePath)`, which walks the element's video sources and
calls `ProxyFingerprint.TryFromFile` — i.e. `FileInfo` + `ResolveLinkTarget` (a filesystem syscall) —
for each source until a match. This runs for every `ElementViewModel` on every
Registered/StateChanged/Deleted store event and every non-Progressed job event. Note that the
per-element fingerprint cache (`ResolveCachedFingerprints`) used by `RefreshProxyState` does *not*
cover this gate — `ElementUsesChangedSource` is a static that re-stats every time.

Failure scenario: a timeline with M video elements bulk-generating K sources fires on the order of
K×M UI-thread `ResolveLinkTarget`/`stat` calls in bursts, which can visibly stutter the timeline
during "Generate All".

Recommendation: cache each element's resolved comparable keys (invalidated by the existing
`ThumbnailsInvalidated` signal, which already forces a re-stat via `invalidateFingerprintCache: true`)
and match the incoming `e.Source.AbsolutePath` against that cached set rather than re-stat per event.
The gate then becomes an in-memory `HashSet` lookup.

## [Low] ProxiesTabViewModel.cs:159 — test seam added but no ProxiesTabViewModelTests exercises it

`ConfirmDeleteAllForProjectAsync` was deliberately made an injectable `Func<int, Task<bool>>` with the
XML remark that "tests substitute it to drive the accept and decline paths without a UI", yet no
`ProxiesTabViewModelTests` exists. The delete-all flow, bulk-eligibility heuristic
(`IsEligibleForBulkGeneration` / `TryGetSourcePixelCount`), and default-preset resolution
(`FindDefaultPreset`) are all internal-testable logic that currently ships untested while the rest of
the proxy stack is thoroughly covered. Per the repo rule "new logic ships with a NUnit test", add a
test that drives at least the accept/decline delete-all paths and the eligibility floor. (Kept Low
only because the surrounding proxy engine/`Beutl.Editor` logic is well tested; paired with the item
below.)

## [Low] EditorSettingsPage.axaml:139-140 — proxy default-preset dropdown shows non-localized enum names

The `ProxyDefaultPreset` combo binds `ItemsSource="{CompiledBinding ProxyPresetOptions}"` where
`ProxyPresetOptions = Enum.GetValues<ProxyPreset>()`, so the UI renders the raw enum member names
("Half" / "Quarter" / "Eighth") via `ToString()`. This is inconsistent with `ProxiesTabView`, which
shows the localized `Strings.ProxyPresetHalf/Quarter/Eighth` (via
`ProxiesTabViewModel.GetPresetDisplayName`). Users see English enum names on the settings page and
localized names in the tab. Recommend projecting the options through the same localized display-name
helper (or a value converter) so both surfaces agree.

---

## Clear

- `src/Beutl.Editor/ProxySourceEnumerator.cs` — correct cycle guards (visited scenes / graph groups /
  filter-effect groups), covers property/animated/graph-input/referenced-scene/filter-effect-subgraph
  paths; duplicates are deduped by consumers. Well tested.
- `src/Beutl.Editor/ExportSourceValidator.cs` — preflight walks all media file sources
  (`EnumerateMediaFileSources`, object URIs excluded) including audio and referenced scenes; ordinal,
  stable ordering. Wired into the export path (`OutputViewModel`) with `forceOriginalSource: true`.
- `src/Beutl.Editor/Services/ISceneSettingsService.cs`, `SceneSettingsService.cs` — comment-only edits.
- `src/Beutl.Editor.Components/ProxiesTab/Views/ProxiesTabView.axaml` — `x:CompileBindings="True"` +
  `x:DataType="vm:ProxiesTabViewModel"`; the item `DataTemplate` declares `x:DataType="vm:ProxyClipViewModel"`.
  No `ReflectionBinding`.
- `src/Beutl.Editor.Components/ProxiesTab/Views/ProxiesTabView.axaml.cs` — tap-to-select correctly
  excludes interactive descendants (Button/ComboBox/CheckBox).
- `src/Beutl.Editor.Components/ProxiesTab/Views/ProxyPresetIndexConverter.cs` — index mapping matches
  the 3-item combo order; `ConvertBack` returns `BindingOperations.DoNothing` on out-of-range.
- `src/Beutl.Editor.Components/ProxiesTab/ProxiesTabExtension.cs` — standard `ToolTabExtension`.
- `src/Beutl.Editor.Components/TimelineTab/ViewModels/ElementViewModel.cs` — badge state machine,
  Combine/Aggregate/Rank helpers, and change-kind gates are internal-testable and covered by
  `ElementViewModelProxyStateTests`; subscriptions all on `_disposables`. (Perf note above is the only
  finding.)
- `src/Beutl.Editor.Components/TimelineTab/Views/ElementView.axaml` — new proxy indicator binds to
  existing `ElementViewModel` properties under the file's existing `x:CompileBindings`/`x:DataType`.
- `src/Beutl.Editor.Components/ViewModels/PreviewSettingsTabViewModel.cs`,
  `Views/PreviewSettingsTabView.axaml` — no diff.
- `src/Beutl/Services/ProxyMediaServices.cs` — shutdown deadlock guard (`s_disposing`) and
  single-in-flight disk-pressure sweep (`Interlocked`) are sound; `DiskHeadroomProxyGenerator` forwards
  availability unchanged; MIT/GPL boundary respected (generator via registry).
- `src/Beutl/ViewModels/EditViewModel.cs` — proxy-invalidation burst is coalesced and disposal-guarded
  (`_disposed` checked under lock and again after the frame-cache check); `GetService` proxy routes are
  null-safe.
- `src/Beutl/ViewModels/Tools/OutputViewModel.cs` — export preflight blocks on missing sources and
  forces original decode in renderer + composer.
- `src/Beutl/ViewModels/PlayerViewModel.cs` — `forceOriginalSource: true` on the export renderer;
  `QueuePreviewRender` is a thin re-render trigger.
- `src/Beutl/ViewModels/MainViewModel.cs` — proxy services disposed on exit with a guarded try/catch.
- `src/Beutl/ViewModels/SettingsPages/EditorSettingsPageViewModel.cs` — GiB round-trip is lossless in
  the [5,500] range (no feedback loop); root-path write-back is inequality-guarded. (See Low note on
  the preset dropdown localization in the view.)
- `src/Beutl/Pages/SettingsPages/EditorSettingsPage.axaml` — now adds the previously-missing
  `x:CompileBindings="True"` (improvement); `PreviewSourceMode` combo index matches the enum's 0/1
  values.
