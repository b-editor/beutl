# Quickstart: Proxy Media Workflow

**Feature**: 002-proxy-media | **Phase**: 1 | **Date**: 2026-05-20

End-to-end developer/manual-test walkthrough for the proxy media feature. The headline guarantee — **preview decodes from proxy, export decodes from original** — is verified at the end.

---

## 0. Prerequisites

- Beutl built with `dotnet build Beutl.slnx` (or `/beutl-build`).
- **The 003 resolution-independent pipeline is the implemented baseline** this feature builds on. The headline verification below depends on the 003 supply-density seam (a proxy keeps the clip's logical footprint and only lowers its `EffectiveScale`). If 003 is not present, several steps (4a, the export rationale in 8) will not apply.
- FFmpeg installed via the existing `FFmpegInstallService` flow (Help → Install FFmpeg, or it'll prompt when needed).
- One **heavy** source clip available locally (≥4K, ≥60 Mbps, ≥30 s long) — call its path `$SRC`.
- One **lightweight** test source available (1080p ≥30 s) — call it `$SRC_SMALL`. Used for fast iteration.

## 1. Set up a project

1. Launch Beutl. **File → New Project**.
2. Import `$SRC` and `$SRC_SMALL` into the project.
3. Drop both onto the timeline so the project preview pipeline references them.
4. Save the project. **Confirm**: `Scene.PreviewSourceMode == PreferProxy` in the saved JSON.

## 2. Inspect the empty proxy store

1. Open the new **Proxies** tool tab (View → Tool Tabs → Proxies, or whatever the implementation wires).
2. **Confirm**:
   - Per-project clip list shows both `$SRC` and `$SRC_SMALL` with state `None`.
   - "Store totals" shows 0 B / 50 GB (default cap).
   - "Pending jobs" is empty.

## 3. Generate a proxy for `$SRC_SMALL`

1. Select `$SRC_SMALL` in the Proxies tab clip list.
2. Choose preset = `Quarter` (the default).
3. Click **Generate**.
4. **Confirm during generation**:
   - The job appears in "Pending jobs" with a progress bar.
   - The clip's state transitions `None → Generating`.
   - Beutl remains responsive (timeline scroll/edit/etc. still works).
5. **Confirm after completion**:
   - The clip's state is `Ready`.
   - The "Store totals" reflects the proxy's size.
   - On disk, `<store-root>/<hash>/quarter.mp4` exists; a sibling `meta.json` is also present.
   - `index.json` contains one entry for `$SRC_SMALL` + `Quarter`.

## 4. Verify preview now uses the proxy (P1 acceptance)

1. Add a verbose log or use the debug overlay to confirm that `DecoderRegistry.OpenMediaFile` returned an `FFmpegReaderProxy` opened against `<store-root>/<hash>/quarter.mp4` for this clip.
2. **Confirm**: scrubbing the timeline on `$SRC_SMALL` is visibly snappier than it was in step 1 (or measurably so via the perf overlay — meet SC-001 on `$SRC` later in step 7).

## 4a. Verify the proxy preserves the logical footprint (post-003 — FR-021 / FR-022)

> This step exists because of the 003 integration. Under 003 a source's logical footprint is derived from its decoded `FrameSize`; a naive smaller-proxy-file swap would shrink the clip on the canvas. The logical-size seam must prevent that.

1. Note the on-canvas position and size of `$SRC_SMALL` with **Preview source = Original** (e.g. select the clip and read its transform/bounds, or screenshot it).
2. Switch **Preview source = Proxy** (its `Quarter` proxy is `Ready` from step 3).
3. **Confirm**: the clip occupies the **same** on-canvas area at the **same** position — it is **not** shrunk to 1/4 size and **not** moved. The proxy only lowered the supply density (the op's `EffectiveScale` is now `0.5`/`0.25`, not `1`); content, layout, and hit-testing are unchanged (003 US3 / SC-007).
4. **If the clip moves or resizes**, the logical-size seam (T062–T065) is broken — escalate as a defect, not as expected behavior.

## 5. Verify fallback when proxy is missing (P1 acceptance)

1. Without generating a proxy for `$SRC`, scrub the timeline over `$SRC`.
2. **Confirm**: preview still plays (decoding from original). The Proxies tab still shows `$SRC` as `None`. No error dialog.

## 6. Verify the "Force Original" toggle (P3 acceptance)

1. **Project Settings → Preview source**: switch to **Original**.
2. Scrub the timeline over `$SRC_SMALL`.
3. **Confirm**: even though a `Ready` proxy exists, the decoder is opened against the original `$SRC_SMALL`, not the proxy. (Verify via the same log/overlay as step 4.)
4. Switch back to **Proxy**. Scrub. Verify the proxy is used again.

## 7. Generate a proxy for `$SRC` (heavy source, exercises SC-001)

1. Select `$SRC` in the Proxies tab. Preset = `Quarter`. Click **Generate**.
2. **Confirm**: queue is serial — only one job runs at a time (the `$SRC` job runs after the `$SRC_SMALL` job in step 3 has completed; if both were enqueued together, `$SRC_SMALL` finished first).
3. After generation completes, scrub `$SRC` on the timeline.
4. **Confirm SC-001**: frame-decode is ≥3× faster than scrubbing `$SRC` with proxies forced off (toggle preview source to Original to compare).

## 8. Export the project — verify SC-002 (THE headline guarantee)

1. **File → Export** with any target format/resolution that matches `$SRC`.
2. Watch the existing export progress UI complete.
3. **Confirm**:
   - The exported file's resolution matches `$SRC` (NOT 1/4 of it).
   - Bit-comparable to an export run with proxies deleted: delete all proxies via "Delete all for this project", re-export, and either checksum the resulting files (if encoding is bit-deterministic) or verify visually-indistinguishable output.
   - At the code level, the export render context never carries `PreferProxy = true`: audit `OutputViewModel`'s `new SceneRenderer(Model, renderScale, disableResourceShare: true, maxWorkingScale)`, `SceneCompositor`'s context seeding, and the sole video `MediaOptions` construction in `VideoSource.Resource.Update`. Rationale (post-003): at export `s_out ≥ 1` (Off / 2× / 4× supersampling), `w = min(max(s_out, densest supply), MaxWorkingScale)` with export ceiling `+∞` would lift a sub-output proxy to at least `s_out` and upsample it (soft); routing through the original is the only full-fidelity export.
4. **If this step fails**, the entire feature is broken — escalate immediately. (FR-002 / FR-004 / SC-002.)

## 9. Verify staleness detection (US2 acceptance #2)

1. Externally `touch` (update mtime of) `$SRC_SMALL`.
2. Reopen the project (or trigger a manual rescan if implemented).
3. **Confirm**:
   - The Proxies tab marks `$SRC_SMALL` as `Stale`.
   - The system does NOT auto-queue a regeneration job (Q3 clarification).
   - Preview falls back to decoding from the original until the user clicks **Regenerate**.

## 10. Verify cancel-mid-job (FR-009 / FR-012)

1. Delete the proxy for `$SRC` (the heavy one).
2. Click **Generate** for `$SRC`. While it's running, click **Cancel**.
3. **Confirm**:
   - The job's terminal state is `Canceled`.
   - No `*.tmp` file remains in the store directory.
   - The next time you click **Generate**, the previous `.tmp` is NOT treated as complete.

## 11. Verify LRU eviction (FR-018a / FR-018b)

> If the default 50 GB cap is large relative to your test corpus, temporarily set `ProxyStoreConfig.MaxTotalBytes` to a smaller value (e.g., 100 MB) via the configuration UI for this step.

1. With the smaller cap in effect, generate proxies for several heavy clips until the cap is exceeded.
2. **Confirm**:
   - The least-recently-used proxy is evicted from disk and `index.json`.
   - A non-blocking notification tells the user "Evicted N proxies, reclaimed X bytes" (FR-018b).
   - The evicted source falls back to original on next preview (no error).
3. **Confirm safety clause**: while a proxy is being read for preview (you are actively scrubbing it), it is NOT evicted even if it's the LRU candidate. Eviction picks the next candidate.

## 11a. Measurement protocol for SC-001 and SC-004 (official manual verification)

> **MVP scope decision**: SC-001 (≥3× faster decode in preview vs. no-proxy) and SC-004 (3-layer 4K timeline holds project frame rate with proxies vs. drops without) are verified **manually via this protocol**. No automated benchmark ships in MVP. This section is the official verification path — if it passes here, the success criterion is met for the release.

### Setup (shared across SC-001 and SC-004)

- Capture machine identity once: OS, CPU model, logical core count, total RAM, GPU (relevant to any GPU-accelerated decode path), free disk space, FFmpeg version (`FFmpegInstallService` reported version).
- Use the **same Beutl build** for both with-proxy and without-proxy runs (no rebuild between).
- Close other heavy apps before each run; record what was running.

### SC-001 — ≥3× preview decode speed-up

1. Project: a single track with `$SRC` (the 4K ≥60 Mbps clip from §0) at native resolution, 30 s in.
2. Run A (no proxy): delete any existing proxy for `$SRC`. With **Preview source = Original**, scrub the cursor across a fixed 10 s range at a steady manual cadence; record frame-decode time via the perf overlay (or, if absent, via `dotnet trace` capturing time-in-decoder over the same window). Repeat 3 times, average.
3. Run B (with proxy): generate the `Quarter` proxy for `$SRC`. With **Preview source = Proxy**, scrub the same 10 s range with the same cadence. Repeat 3 times, average.
4. **Pass criterion**: `avg_decode_A / avg_decode_B >= 3.0`. Record both numbers, the ratio, and the machine identity in the PR.

### SC-004 — 3-layer 4K holds frame rate

1. Project: 3 layers of 4K source (use `$SRC` × 3, lightly trimmed so they don't perfectly overlap and the renderer composites them). Project frame rate = `$SRC`'s native rate (typically 30 or 60 fps).
2. Run A (no proxy): delete all proxies. With **Preview source = Original**, play back the same 10 s timeline range; record dropped-frame count via the perf overlay (or screen-recording analysis if the overlay does not surface drops).
3. Run B (with proxy): generate `Quarter` proxies for all 3 source instances. With **Preview source = Proxy**, play back the same range; record dropped frames.
4. **Pass criterion**: Run A drops ≥1 frame AND Run B drops 0 frames over the same range on the same machine. If Run A drops 0 frames (machine too fast), document that the SC is not exercisable on this machine and either pick a heavier source or accept the result as "no regression" rather than "≥3× win".

If either pass criterion fails on a representative machine, treat it as a feature defect (likely a regression in the routing layer or in preset parameters), not as quickstart drift.

---

## 12. Run the test suite

```bash
dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~Media.Proxy"
dotnet test tests/Beutl.FFmpegIpc.Tests/Beutl.FFmpegIpc.Tests.csproj -f net10.0 --filter "FullyQualifiedName~ProxyGeneration"
```

Both must be green before opening the PR. The FFmpegIpc test is gated on FFmpeg availability; on CI it runs in the same environment as existing FFmpeg-dependent tests.

---

## What success looks like

Every numbered step above passes without manual intervention beyond the documented actions. In particular:

- Step 8 (export uses original) **must** pass — it is the spec's headline guarantee.
- Step 4a (proxy preserves the logical footprint) **must** pass — otherwise the 003 integration is broken and proxies visibly misplace content on the canvas.
- Step 5 (fallback to original on missing proxy) **must** pass — proxies are an optimization, not a hard dependency.
- Step 11 (eviction respects the pin set) **must** pass — otherwise we yank files out from under live decoders.

If any step requires "well, you have to do this trick first", treat that as a defect in the feature, not in the quickstart.
