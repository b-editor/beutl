# docs/specs/

This directory is Spec-Kit's output target for Beutl. Each large feature gets a numbered subdirectory containing `spec.md`, `plan.md`, `tasks.md`, and any supporting artifacts. The `/speckit-*` commands write here.

Until at least one feature has been driven through the full flow, this directory is intentionally near-empty.

## Expected layout

```
docs/specs/
├── README.md                 # this file
├── 001-<short-feature-slug>/
│   ├── spec.md               # /speckit-specify output
│   ├── plan.md               # /speckit-plan output
│   ├── tasks.md              # /speckit-tasks output
│   ├── checklist.md          # optional /speckit-checklist output
│   └── notes/                # free-form supplementary material
└── 002-<short-feature-slug>/
    └── ...
```

Numbering is sequential (`001`, `002`, …) and never reused. The slug is `kebab-case` and short — the spec itself carries the full title.

## When to use this flow

- Large or contentious features that need design alignment before code
- IPC protocol changes between MIT and `Beutl.FFmpegWorker` (GPL)
- New editor panes, new plugin extension points, anything that crosses module boundaries
- Anything you would otherwise write a design doc for

For small bug fixes or behaviour-preserving refactors, skip Spec-Kit entirely.

## Walkthrough

```text
/speckit-specify <one-paragraph description>      # generates 00N-slug/spec.md
/speckit-clarify                                  # optional: ≤5 targeted questions
/speckit-plan                                     # generates 00N-slug/plan.md
/speckit-tasks                                    # generates 00N-slug/tasks.md
/speckit-analyze                                  # optional: cross-artifact consistency check
/speckit-implement                                # executes tasks.md
```

`docs/ai-workflow/spec-driven-development.md` has the long-form reference, including how the Beutl-local `SPECS_DIR` patch redirects Spec-Kit's default output path here.

## Reviewing a spec without driving the flow

To browse existing specs without invoking `/speckit-*`, ask Claude Code to "look at what's already in `docs/specs/`" — the `beutl-spec-explorer` subagent is purpose-built for that traversal and only walks this directory.

## Lifecycle

Specs accumulate. We do not delete delivered specs — they are the canonical record of why a feature exists. A delivered feature's `tasks.md` should end with all items checked; an abandoned spec stays in place and gets a `STATUS: abandoned (<reason>)` line near the top of its `spec.md`.
