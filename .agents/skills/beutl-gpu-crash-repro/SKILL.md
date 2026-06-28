---
description: |
  Reproduce and root-cause a NATIVE crash (SIGSEGV / "Test host process crashed" with no managed stack)
  that fails only on CI's Linux + SwiftShader GPU test job and NOT on the dev Mac. Use when the `.NET` CI
  job aborts in the GPU golden / effect-scale tests, when a crash mentions SwiftShader / Vulkan / Skia, or
  when the user says "CIのGPUクラッシュを再現して", "SwiftShaderで落ちる", "reproduce the CI native crash",
  "ネイティブスタックを取って", "get a native stack". Covers the arm64-native Docker repro, kernel core
  capture, gdb/eu-stack native stacks, the managed-call-site file-trace, intermittent-crash looping, and
  fix verification. Delegates the noisy build+loop to the `beutl-gpu-crash-reproducer` subagent.
argument-hint: "[TestFilter FQN-substring]"
---

# Reproduce & debug a Beutl Linux/SwiftShader GPU native crash

A long, multi-step investigation. **Confirm direction with the user at the decision
points below** — a wrong fix costs a full ~25-minute verify cycle.

## When this applies
- CI `.NET` job aborts with "Test host process crashed" / SIGSEGV and NO managed stack, in the GPU golden /
  effect tests. That job sets `BEUTL_REQUIRE_GPU=1` and uses `Silk.NET.Vulkan.SwiftShader.Native`.
- It does NOT reproduce on the dev Mac: macOS render-target surfaces are raster (no imported `VkImage`), so
  GPU-path native faults never happen locally. You MUST reproduce in a Linux container.

## The one load-bearing lesson: reproduce ARM64-NATIVE, not qemu x64
On Apple silicon a `--platform linux/amd64` container runs x64 via qemu-user; the kernel core is the **qemu
emulator's arm64 core** (`readelf -n` shows `NT_ARM_*`), useless for the guest stack, and every ptrace tool
(createdump, gdb attach, `--blame-crash`) fails under qemu. SwiftShader ships `runtimes/linux-arm64`, so the
bug almost always reproduces **arm64-native** (`--platform linux/arm64`) where cores are real and gdb/eu-stack
work. See `references/native-stack-and-file-trace.md`.

## 1. Setup (once)
```bash
SKILL=.Codex/skills/beutl-gpu-crash-repro
git worktree add --detach /tmp/beutl-ss-arm64 HEAD          # isolated bin/obj; never build the repro in-tree
docker build --platform linux/arm64 -t beutl-ss:10.0-arm64 "$SKILL/scripts"
```
Every container run mounts: worktree→`/work`, `~/.nuget/packages`→`/root/.nuget/packages`, a FRESH dumps
dir→`/dumps`, `$SKILL/scripts`→`/scripts`; pass `--privileged` to set `core_pattern`. Env
(`BEUTL_REQUIRE_GPU=1`, `XDG_RUNTIME_DIR=/tmp`, minidump-off) is set inside the scripts.

## 2. Reproduce + capture the native stack
**Delegate to the `beutl-gpu-crash-reproducer` subagent** so the Docker/test output and multi-GB cores never
flood this context; it returns just the native backtrace + signal + crashing thread + iteration/repro-rate.
Or run by hand:
```bash
DRUN="docker run --rm --platform linux/arm64 --privileged \
  -v /tmp/beutl-ss-arm64:/work -v $HOME/.nuget/packages:/root/.nuget/packages \
  -v /tmp/ss-dumps:/dumps -v $PWD/$SKILL/scripts:/scripts beutl-ss:10.0-arm64 bash"
$DRUN /scripts/build.sh
$DRUN /scripts/loop-core.sh      # env TEST_FILTER, MAX_RUNS; loops until a core
$DRUN /scripts/analyze-core.sh   # gdb + eu-stack native backtrace
```

## 3. If gdb only shows `?? ()` for the managed caller
The native frame (e.g. `SkCanvas::restoreToCount`, `x0/this=0x0`) is often not enough, and `dotnet-dump` /
lldb SOS usually can't bind the net10 DAC from these cores — don't fight it. Use the **file-trace**: copy
`scripts/CrashTrace.cs.txt` into the engine, inject `CrashTrace.Mark("<site>")` before each candidate native
call, run one loop pass; `/dumps/lastrestore.txt` keeps the crashing site's tag. Details + a worked example:
`references/native-stack-and-file-trace.md`.

## 4. Intermittent crashes
These are usually GC/teardown-timing races on `Beutl.RenderThr` — the `--blame` "current test" is then
coincidental. Loop until it crashes; **prove a fix with ~30 consecutive clean runs PER ARCH**
(`$DRUN env RUNS=30 /scripts/verify-fix.sh`). A pre-fix repro rate of ~1-in-6 means a few clean runs prove
nothing. If the bug could be arch-sensitive, also verify with `--platform linux/amd64` (image
`beutl-ss:10.0-amd64` from the same Dockerfile).

## Decision points — ASK the user (AskUserQuestion)
- **Competing hypotheses**: when more than one root cause fits, surface them rather than guessing. (History:
  two plausible-but-wrong fixes — a deferred-draw UAF guard and an OOM theory — were shipped before the
  file-trace pinned the real site.)
- **Squashing verification churn**: rewriting pushed history needs a force-push to the FEATURE branch (never
  main/master — the hook denies it). Confirm scope first.

## Constraints
- Do NOT edit `.github/workflows/*` to dodge the crash (AGENTS.md rule #5) — fix the engine.
- The dangerous-bash hook blocks `rm -rf`. Clean up with `git worktree remove --force /tmp/beutl-ss-arm64`
  (+ `git worktree prune`) and `find /tmp/ss-dumps -name 'core.*' -delete` (cores are multi-GB).
- New engine logic ships with an NUnit test — add a deterministic regression test for the fixed contract
  (e.g. dispose-after-surface-disposed must not crash), even though the race itself is non-deterministic.

## See also
- Subagent `beutl-gpu-crash-reproducer` — the isolated build+loop+capture runner.
- Memory `beutl-gpu-ci-crash-debugging` — the concise field note.
- `references/native-stack-and-file-trace.md` — why-arm64, file-trace, DAC caveats, worked 003 example.
