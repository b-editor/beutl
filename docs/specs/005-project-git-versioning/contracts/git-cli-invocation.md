# Contract: Git CLI invocation (`GitCliRunner`)

**Scope**: the single choke point through which every git child process is spawned. No other type starts a git process.

## Process rules

1. **No shell.** `ProcessStartInfo` with an argument list; never string-concatenated command lines.
2. **Working directory** = `RepositoryInfo.RepoRoot` (repo discovery itself runs from the project directory).
3. **Executable** = the path resolved by `GitInstallationLocator` (R-3), re-validated on config change.

## Environment (every invocation)

| Variable | Value | Why |
|---|---|---|
| `GIT_TERMINAL_PROMPT` | `0` | Never hang a GUI process on a credential/passphrase prompt; fail fast into the guidance dialog |
| `GIT_OPTIONAL_LOCKS` | `0` | `git status` must not write `.git/index` — breaks the watcher feedback loop (R-8) |
| `LC_ALL` | `C` | Stable, locale-independent parseable output |
| `GIT_SSH_COMMAND` | `ssh -oBatchMode=yes` | Network ops only; SSH fails fast instead of prompting |

The runner must **not** set `GIT_CONFIG_GLOBAL`/`GIT_CONFIG_NOSYSTEM` in production (the user's config is the credential story); tests set them for isolation (R-14).

## Output rules

- Machine-readable formats only, NUL-separated where supported:
  - status: `git status --porcelain=v2 --branch -z`
  - history: `git log --format=%H%x00%h%x00%an%x00%aI%x00%s%x00%(trailers:key=Beutl-Snapshot,valueonly)%x00 -z --skip=<n> -n <take> -- <pathspec>`
  - commit files: `git show --name-status --format= -z <sha> -- <pathspec>`
  - refs: `git for-each-ref --format=...` / `git rev-parse`
- Human-facing output is never parsed. `stderr` is captured and preserved on `GitOperationException` for error dialogs after credentials embedded in URLs are redacted.
- stdout/stderr are read concurrently with process execution (no deadlock on full pipes); output size for diff display is capped (1 MB) with a truncation marker.

## Lifecycle

- `WaitForExitAsync` with the caller's `CancellationToken`; cancellation kills the process tree.
- Timeouts: local operations 30 s (a wedged local git indicates a broken repo → surface, don't spin); network operations unbounded but cancelable with progress (`--progress` on push, parsed from stderr).
- Exit code ≠ 0 ⇒ typed failure. The runner never retries; retry policy is the caller's.

## Stale lock recovery (edge case: crash mid-commit)

On `index.lock`-style failures (`another git process seems to be running`): if no live git child of this Beutl process exists and the lock file's mtime is older than 10 minutes, offer one-click removal of the stale lock (explicit user consent, logged); otherwise surface guidance. Never auto-delete silently.
