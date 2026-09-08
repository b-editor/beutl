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
| `GIT_LITERAL_PATHSPECS` | `1` | Treat every generated project path as data, even when a directory name begins with Git pathspec magic such as `:(top)` |
| `LC_ALL` | `C` | Stable, locale-independent parseable output |
| `GIT_SSH_COMMAND` / `GIT_SSH` / `GIT_SSH_VARIANT` | Preserve inherited selection; otherwise set `GIT_SSH_COMMAND=ssh -oBatchMode=yes` for the default SSH transport | Network ops only; OpenSSH fails fast instead of prompting without replacing a user-selected SSH command or variant |

The runner must **not** set `GIT_CONFIG_GLOBAL`/`GIT_CONFIG_NOSYSTEM` in production (the user's config is the credential story); tests set them for isolation (R-14).

The sole `GIT_LITERAL_PATHSPECS` exception is the ignored-collision probe: `git check-ignore --stdin -z` receives already validated, NUL-delimited repository-relative paths on standard input and runs with the variable set to `0` so Git can apply ignore patterns. All command-line path arguments retain literal mode.

For network operations, the runner preserves inherited `GIT_SSH_COMMAND`, `GIT_SSH`, and `GIT_SSH_VARIANT` values. If none is present, it queries the effective repository/global `core.sshCommand` and `ssh.variant`. Only absent command and variant settings select the default OpenSSH transport and add `-oBatchMode=yes`; configured commands, explicit variants, and indeterminate configuration results are left untouched. Standard input is redirected and closed immediately after process start, so neither Git nor an SSH child can wait for input from the GUI process.

## Output rules

- Machine-readable formats only, NUL-separated where supported:
  - status: `git status --porcelain=v2 --branch -z`
  - history: `git log --format=%H%x00%h%x00%an%x00%aI%x00%s%x00%(trailers:key=Beutl-Snapshot,valueonly)%x00 -z --skip=<n> -n <take> -- <pathspec>`
  - commit files: `git show --name-status --format= -z <sha> -- <pathspec>`
  - refs: `git for-each-ref --format=...` / `git rev-parse`
- Human-facing output is never parsed. Up to 64 KiB of complete `stderr` records is captured on `GitOperationException` after credentials embedded in each record are redacted. An oversized undelimited record is replaced instead of retaining a mid-token suffix that could separate a secret from the URL prefix needed to redact it. The same bounded reader is used whether or not a progress sink is present.
- stdout/stderr are read concurrently with process execution (no deadlock on full pipes). Diff stdout is capped at 1 MiB while the pipe is read: excess bytes are discarded while the pipe continues to drain, the retained prefix ends on a complete UTF-8 sequence, and the service appends one truncation marker.

## Lifecycle

- `WaitForExitAsync` with the caller's `CancellationToken`; cancellation kills the process tree.
- Timeouts: local operations 30 s (a wedged local git indicates a broken repo → surface, don't spin); network operations unbounded but cancelable with progress (`--progress` on push, parsed from stderr).
- Exit code ≠ 0 ⇒ typed failure. The runner never retries; retry policy is the caller's.

## Guarded snapshot commits

Git 2.36 is the minimum because snapshot commits use `git hook run`. The service captures the attached branch ref/tip and the byte-exact live index while holding the worktree-private `HEAD.lock`, then builds the project tree in a temporary index. Broad staging and the generated portable `*.[tT][mM][pP]` ignore rule exclude `.beutl` state and every `.tmp` extension casing; serialized required `.tmp` files are force-added by exact repository path, and files required by the prior committed graph but no longer referenced are removed. Tracked scratch `.tmp` content is otherwise inherited unchanged. A later live-index change fails the byte/metadata compare-and-swap before publication and is never overwritten.

Before creating the commit object, the service reacquires `HEAD.lock`, revalidates the captured ref/tip, freezes author, committer, signing, cleanup, and automatic comment-character settings, and runs `pre-commit`, `prepare-commit-msg`, and `commit-msg` against the frozen temporary index. Message hooks receive a unique owned file through their normal arguments; `GIT_COMMIT_EDITMSG` also names that file. Message bytes are preserved according to `i18n.commitEncoding`, and cleanup follows `git commit -m` ordering. The final hook-modified tree must remain inside the project scope, contain only regular required entries, contain no gitlinks or reserved-state references, and retain the same required `.tmp` identity set. Invalid or empty hook output is rejected before any ref update.

The service creates the object with `git commit-tree`, reconciles the live index from that exact commit through its earlier compare-and-swap, and publishes the captured branch with `git update-ref <ref> <new> <expected-old>` from a temporary detached worktree. A lost update-ref response is accepted only when the captured ref is observed at the exact new object; a competing ref update restores only a still-owned reconciled index. `post-commit` runs once, with the real worktree index and frozen identity environment, only after durable publication. The HEAD lease is released first so the hook has ordinary Git mutation freedom. A post-hook failure is diagnostic only and cannot turn a durable commit into a retryable failure. Owned message files and the temporary worktree are cleaned on success and failure.

## Guarded tree-transition ref updates

A close/reopen tree transition resolves the original worktree's private `HEAD` and `index` through `git rev-parse --git-path`, acquires its `HEAD.lock`, and verifies the exact `ref: refs/heads/...` contents before mutating files. It validates the expected attached tip and scoped worktree/index fingerprints, then applies the target through Git's branch-mode checkout collision gate. The checkout runs from the temporary detached context with `GIT_WORK_TREE` pointing to the original repository worktree and `GIT_INDEX_FILE` pointing to that worktree's private index: `git -c core.hooksPath=/dev/null checkout --detach --no-overwrite-ignore <target>`. This moves only the temporary HEAD while Git refuses late tracked, untracked, and ignored collisions in the original worktree. Hooks are disabled only for this internal forward/reverse checkout so a `post-checkout` hook cannot mutate or reverse the protected transaction outcome; ordinary user-facing Git commands retain the user's hooks. `git update-ref <ref> <target> <expected>` remains the final durable step.

Git refuses to update a branch checked out in a worktree while that same worktree's `HEAD.lock` is held. Before acquiring the lock, Beutl therefore creates a uniquely named temporary worktree at the captured current tree with `git worktree add --detach --no-checkout`. That context owns the protected checkout's temporary HEAD and the final expected-old `update-ref`; the user's project HEAD never becomes detached. Creation failure is pre-mutation; removal is best-effort after the transition and cannot reverse a durable success. The same in-process exclusive transaction prevents two Beutl transitions from creating competing writers.

Checkout failure can occur after Git has updated the selected worktree/index but before it updates the temporary HEAD. Recovery therefore observes all three independently. If the exact target tree/index is present, a temporary HEAD still at the captured current tree is aligned to the target with `git update-ref --no-deref HEAD <target> <current>` before the protected reverse checkout; an already-target temporary HEAD proceeds directly, and any other value yields `OwnershipLost`. A response failure after that temporary-HEAD CAS is resolved by observing the ref. Unknown or partially written worktree content is never overwritten; only an index fingerprint proven to belong to Beutl may be restored before returning the uncertain outcome.

## Stale lock recovery (edge case: interrupted repository mutation)

On repository-lock failures (`index.lock`, the worktree-private `HEAD.lock`, or `another git process seems to be running`), resolve lock paths through the repository's Git directories. If no live Git child of this Beutl process exists and a lock file's mtime is older than 10 minutes, report that specific stale lock. Never auto-delete silently.

One-click removal requires explicit user consent and an atomic conditional-delete primitive. On Windows, capture the lock's volume/file ID when offering recovery, then open the path with `DELETE | FILE_READ_ATTRIBUTES` and no sharing, revalidate the mtime and volume/file ID through that exclusive handle, and set `FileDispositionInfo` on the same handle. A replacement cannot be removed by the stale path after the identity check. Platforms without an equivalent handle-bound primitive refuse in-app deletion and surface manual guidance; an ordinary path recheck followed by `File.Delete`, Unix unlink, or `FileShare`/`DeleteOnClose` sequence is not sufficient.
