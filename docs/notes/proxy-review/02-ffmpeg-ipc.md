# FFmpeg proxy / IPC / encoding review

Branch `yuto-trd/proxy` vs `main`. Scope: FFmpeg proxy generation, worker process lifecycle,
install-notifier state machine, IPC frame-format conversion, encode scale/resolution.

## Summary

| Severity | Count |
|----------|-------|
| Blocker  | 0     |
| High     | 0     |
| Medium   | 1     |
| Low      | 4     |

GPL/MIT boundary is clean: `FFmpegProxyGenerator` / `FFmpegEncodingControllerProxy` live in the MIT
extension host and reach the GPL worker only through `Beutl.FFmpegIpc`; no new `ProjectReference` to
`Beutl.FFmpegWorker`. NUnit coverage for the new logic is present and follows existing conventions
(`FFmpegEncodingScaleTests`, `FFmpegInstallNotifierTests`, `FFmpegProxyGeneratorPublishTests`,
`FFmpegWorkerProcessTests`, `ProxyGenerationE2ETests`). Cancellation semantics are preserved
(`CreateWorkerStartCanceledException` maps a cancelled connect to `OperationCanceledException`, only a
real timeout yields `TimeoutException`).

The one thing worth fixing before merge is that the publish rollback path is not itself
exception-safe (Medium). Everything else is Low / advisory.

---

## [Medium] FFmpegProxyGenerator.cs:136-149,359-376 — publish rollback can mask the primary exception and orphan the previous proxy

`PublishAsync` carefully builds an atomic temp→final move with sidecar backup/restore, but the
restore helpers it calls from the two catch clauses are not exception-guarded:

- `RestoreMetadata` (366-376) calls `File.Copy(backupPath, GetMetadataPath(finalPath), overwrite:true)`
  with no try/catch (only the `RemoveMetadataEntry` branch swallows).
- `RestoreFinalPath` (359-364) does `TryDelete(finalPath)` then
  `File.Move(backupPath, finalPath, overwrite:true)` — the `File.Move` is unguarded.

Failure scenario: a job replaces an existing proxy, `FinalizeAsync`/register fails or the token is
cancelled, and rollback runs on a full/locked disk. `RestoreMetadata` throws first, so the rollback
propagates that I/O exception instead of the original `OperationCanceledException`/cause, and
`RestoreOrDeleteCanceledFinal` / `RestoreFinalPath` never run — the freshly-moved final is left in
place (or, if the throw is inside `RestoreFinalPath` after its `TryDelete(finalPath)`, the final is
deleted while the old proxy survives only as an orphaned `*.bak`, and the store index still points at
the now-missing old entry). This defeats the atomicity the surrounding code is built to guarantee, on
exactly the error path where it matters.

Recommendation: wrap the restore operations so a rollback I/O failure is logged and swallowed rather
than replacing the primary exception, and ensure `RestoreOrDeleteCanceledFinal`/`RestoreFinalPath`
still runs even if metadata restore fails (restore file first, metadata second, each best-effort).
The index is authoritative and `ReconcileAsync` can recover, but the primary exception (especially
cancellation) must not be swallowed by a rollback error.

## [Low] IpcFrameProvider.cs:133-143 — color space inferred from BytesPerPixel, ignoring per-frame color-space metadata

`GetFrameFormat` maps `BytesPerPixel==4 → (Bgra8888, Srgb)` and `==8 → (RgbaF16, LinearSrgb)`. The
color *type* is correct — the worker converts SDR frames to `AV_PIX_FMT_BGRA`
(`FFmpegReader.cs:450`) and HDR to `rgba64le`, so 4bpp is genuinely BGRA. But the worker computes an
actual per-frame color space (`GetFrameColorSpace`, and `ReadVideoResponse` even carries
`TransferFn`/`ToXyzD50`), which this path discards in favor of a hardcoded `Srgb`. An SDR frame whose
tagged primaries/transfer are not sRGB would be mislabeled. If the streaming `ProvideFrameMessage`
does not currently carry that metadata this is moot today; flag as (speculative) coupling to verify
against the worker's contract for wide-gamut SDR sources.

## [Low] FFmpegProxyGenerator.cs:378-391 — meta.json read-modify-write is unsynchronized (speculative)

`WriteMetadata` reads `meta.json`, filters out the same preset, appends, and rewrites. All presets
for one source share a single `meta.json`. If two preset jobs for the same source ever publish
concurrently, the read-modify-write races and one sidecar update is lost. Impact is limited because
the sidecar is explicitly best-effort and the `ProxyStore` index is authoritative
(`ReconcileAsync` recovers), so this is low. Confirm the job queue serializes per-source publishes; if
it does, this is a non-issue.

## [Low] FFmpegProxyGenerator.cs:30-34 — AvailabilityChanged forwards to a static event with no unsubscribe path

The instance `AvailabilityChanged` add/remove forward to the static
`FFmpegInstallNotifier.AvailabilityChanged`. `FFmpegProxyGenerator` is not `IDisposable`, so a
consumer that subscribes through a short-lived generator and never explicitly unsubscribes leaks its
delegate into the process-lifetime static event. Likely fine if generators are long-lived (one per
store), but there is no lifecycle guard. Consider documenting the expected lifetime or exposing a
detach path. (speculative — depends on generator lifetime in `ProxyMediaServices`.)

## [Low] FFmpegProxyGenerator.cs:220 — hardcoded H.264 `level 4.0` may not fit every proxy resolution/framerate

`Configure` pins `profile high` + `level 4.0` for all presets. A Half proxy of a 4K source is
1920x1080 (~8160 macroblocks), right at the level-4.0 limit; a high frame rate could exceed the
level's macroblock-rate ceiling. libx264 typically warns and still encodes rather than erroring, so
the practical impact is a possibly-inaccurate level tag in the stream, which lenient players ignore.
Consider letting x264 auto-select the level (omit the option) or deriving it from the computed proxy
size/framerate. (speculative)

## Clear

- `src/Beutl.Extensions.FFmpeg/Proxy/FFmpegProxyExtension.cs` — thin registration shim; mirrors the
  decoder-extension pattern. No boundary or correctness concern.
- `src/Beutl.Extensions.FFmpeg/FFmpegWorkerProcess.cs` — cancellation/timeout split is correct
  (`CreateWorkerStartCanceledException` preserves OCE), exit-code-2 arms the re-probe cooldown, and
  handshake success clears the missing latch. Worker is killed on cancel (line 186). The pre-existing
  "handshake-failure leaves the process running because the handshake block is outside the try/catch"
  behavior is not introduced by this diff.
- `src/Beutl.Extensions.FFmpeg/FFmpegInstallService.cs` — the `MarkVerificationStarted` →
  `MarkInstalled` split correctly avoids resuming queued jobs until verification actually succeeds.
- `src/Beutl.Extensions.FFmpeg/FFmpegInstallNotifier.cs` — re-probe cooldown state machine is
  internally consistent; the read-modify-write of `s_librariesMissing` is unsynchronized but the
  consequences (a spurious/missed AvailabilityChanged) are benign for a best-effort notifier.
- `src/Beutl.Extensions.FFmpeg/Decoding/FFmpegDecoderInfo.cs` — records the missing condition then
  returns null so a non-FFmpeg fallback decoder can still open the file; correct.
- `CalculateProxySize` (FFmpegProxyGenerator.cs:223-248) — single-scale long-edge rounding with
  short-edge derived from the realized long edge, `MakeEven` clamped to ≥2; even-dimension and
  aspect handling are sound and covered by `FFmpegEncodingScaleTests`.
