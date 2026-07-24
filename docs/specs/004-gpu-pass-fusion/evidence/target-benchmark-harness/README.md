# Starting-SHA benchmark harness

This project is an intentionally standalone harness for building the GPU-pass benchmark against the unmodified
starting commit. It is not part of `Beutl.slnx`: the paired runner supplies `BaselineEngineProject` from a separate
clean baseline worktree, while the feature benchmark is built from `tests/Beutl.Benchmarks`.

Run both sides through the evidence runner rather than building this project as part of the feature solution:

```bash
docs/specs/004-gpu-pass-fusion/evidence/run-paired-benchmarks.sh \
  <baseline-worktree> <feature-worktree> <empty-output-directory>
```

The runner builds this project explicitly, verifies both worktrees and hardware fingerprints, executes the
baseline-feature-baseline sequence, and writes the paired evidence manifest.
