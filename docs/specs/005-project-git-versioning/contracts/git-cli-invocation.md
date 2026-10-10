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

The `GIT_LITERAL_PATHSPECS` exceptions are the nested-repository probe, where `git check-ignore --stdin -z` receives already validated, NUL-delimited repository-relative paths on standard input and runs with the variable set to `0` so Git can apply ignore patterns, and the pathspecs that carry explicit magic (`:(top,literal)`, `:(exclude,glob)`). Other command-line path arguments retain literal mode.

For network operations, the runner preserves inherited `GIT_SSH_COMMAND`, `GIT_SSH`, and `GIT_SSH_VARIANT` values. If none is present, it queries the effective repository/global `core.sshCommand` and `ssh.variant`. Only absent command and variant settings select the default OpenSSH transport and add `-oBatchMode=yes`; configured commands, explicit variants, and indeterminate configuration results are left untouched. Standard input is redirected, written as UTF-8 without a byte-order mark, and closed immediately after the payload, so neither Git nor an SSH child can wait for input from the GUI process.

## Output rules

- Machine-readable formats only, NUL-separated where supported:
  - status: `git status --porcelain=v2 --branch -z`
  - history: `git log --format=%H%x00%h%x00%an%x00%aI%x00%s%x00%(trailers:key=Beutl-Snapshot,valueonly)%x00 -z --skip=<n> -n <take> -- <pathspec>`
  - commit files: `git show --name-status --format= -z <sha> -- <pathspec>`
  - refs: `git for-each-ref --format=...` / `git rev-parse`
- Human-facing output is never parsed. Up to 64 KiB of complete `stderr` records is captured on `GitOperationException` after credentials embedded in each record are redacted. An oversized undelimited record is replaced instead of retaining a mid-token suffix that could separate a secret from the URL prefix needed to redact it. The same bounded reader is used whether or not a progress sink is present.
- stdout/stderr are read concurrently with process execution (no deadlock on full pipes). Diff stdout is capped at 1 MiB while the pipe is read: excess bytes are discarded while the pipe continues to drain, the retained prefix ends on a complete UTF-8 sequence, and the service appends one truncation marker.

## Lifecycle

- Each command owns a process group from launch (`GitProcess`). On Linux and macOS it is started with `posix_spawn` as the leader of a new session, which leaves it without a controlling terminal; a C library without `POSIX_SPAWN_SETSID` gives it a new process group instead. On Windows it is started suspended with `CreateProcess`, joins a job object of its own, and only then resumes, so everything it starts is born inside the job. `Process` cannot start a suspended process, so this start builds the command line, environment block, and pipes the way `Process` does and takes the lock `Process.Start` holds while inheritable pipe ends exist. A command that cannot join the job is ended before it runs and created again outside the job Beutl itself is in, where that job allows it to leave; a command that still cannot join is not run, and its start fails. A command that cannot be ended before it runs keeps its handle, and ending it is retried until it has ended. Only where the runtime does not expose that lock is the command started with `Process` instead (with a warning), because a start made meanwhile could inherit its pipe ends. Cancellation and timeouts terminate that group rather than walking the process tree, which loses a descendant once its parent has exited.
- The launched process is not reaped until its group is released. While it is an unreaped zombie neither its id nor the group id can be given to another process, so a group signal reaches only processes the command started, never Beutl's own group or a process that reused an id.
- A command that exits and closes its pipes releases its group. Background work that already closed the command's pipes is neither waited for nor killed. A descendant that leaves the group on purpose (`setsid`, `setpgid`, or `CREATE_BREAKAWAY_FROM_JOB`) is not owned either; after a kill, closing the command's pipes detaches it instead of waiting for it.
- On Windows, the parent pipe ends use `PipeStream` asynchronous reads and writes with a stream-lifetime cancellation token. Closing a stream cancels its pending I/O through the runtime's synchronous-I/O cancellation support, including when an escaped descendant still holds the other end. Disposing a synchronous `FileStream` alone does not interrupt those operations. Closing the pipes does not waive the job-exit check below.
- After a kill, cleanup is confirmed only when the pipes have closed and no member of the group remains: Linux reads `/proc` while the leader still reserves the group id, macOS probes the group after reaping the leader, and Windows reads the job's accounting. A job that cannot be terminated is logged and its process tree is ended instead; the call is not repeated, since none of its failures is known to be transient. Accounting or a process list that cannot be read leaves cleanup unconfirmed. Until then, beyond the 1 s grace period of the call itself, the runner is quarantined: `HasActiveProcess` stays true, stale-lock recovery is withheld, and later commands wait.
- An exit status that cannot be collected because the process was reaped elsewhere is reported as exit code `-1` with a diagnostic, never as success. A runtime that started with SIGCHLD ignored reaps every child, so once this happens later commands keep `Process`, which that runtime reaps in step with, and a group is signalled only while its leader is confirmed to reserve its id.
- A Unix C library without `posix_spawn_file_actions_addchdir_np` (glibc before 2.29) keeps `Process` and the process-tree kill, and so does a Windows runtime without that lock. On Windows such a command is owned through the process tree as far as it can be: the tree is walked after the command exits too, because the handle `Process` holds keeps its id reserved, and cleanup waits until the system's process list shows no live descendant, including one this process cannot open.
- Timeouts: local operations 30 s (a wedged local git indicates a broken repo → surface, don't spin); network operations unbounded but cancelable with progress (`--progress` on push, parsed from stderr).
- Exit code ≠ 0 ⇒ typed failure. The runner never retries; retry policy is the caller's.

## Snapshot commits

A snapshot is an ordinary Git commit of the project scope. The service stages the project with `git add -A` limited to the project pathspec, with excludes for `.beutl` state and every `.tmp` extension casing (the paths the generated ignore rules also name), and records it with `git commit --only` on the same pathspecs, so content the user has staged outside the project stays staged and uncommitted. Every other ignore rule is the user's: a snapshot leaves an ignored file out as plain Git does, and a one-time notice lists the ignored project files (an ignored folder once) so the choice does not go unnoticed. Git runs the commit hooks, honours `commit.gpgSign` for a manual commit (automatic snapshots pass `--no-gpg-sign`), and updates the checked-out branch and the index under its own locks; a concurrent Git process surfaces as Git's own lock error. The commit runs without a timeout because hooks and signers can wait on the user, and only cancellation stops it. The message carries a `Beutl-Snapshot: <kind>` trailer. Before staging, the service refuses a merge, rebase or similar operation in progress and an unignored nested repository, which Git would record as a gitlink. Git 2.36 is the minimum because tree inspection uses `git ls-tree --format`.

## Guarded tree-transition ref updates

A close/reopen tree transition resolves the original worktree's private `HEAD` and `index` through `git rev-parse --git-path`, acquires its `HEAD.lock`, and verifies the exact `ref: refs/heads/...` contents before mutating files. It validates the expected attached tip and scoped worktree/index fingerprints, then applies the target through Git's branch-mode checkout collision gate. The checkout runs from the temporary detached context with `GIT_WORK_TREE` pointing to the original repository worktree and `GIT_INDEX_FILE` pointing to that worktree's private index: `git -c core.hooksPath=/dev/null checkout --detach --no-overwrite-ignore <target>`. This moves only the temporary HEAD while Git refuses late tracked, untracked, and ignored collisions in the original worktree. Hooks are disabled only for this internal forward/reverse checkout so a `post-checkout` hook cannot mutate or reverse the protected transaction outcome; ordinary user-facing Git commands retain the user's hooks. `git update-ref <ref> <target> <expected>` remains the final durable step.

Git refuses to update a branch checked out in a worktree while that same worktree's `HEAD.lock` is held. Before acquiring the lock, Beutl therefore creates a uniquely named temporary worktree at the captured current tree with `git worktree add --detach --no-checkout`. That context owns the protected checkout's temporary HEAD and the final expected-old `update-ref`; the user's project HEAD never becomes detached. Creation failure is pre-mutation; removal is best-effort after the transition and cannot reverse a durable success. The same in-process exclusive transaction prevents two Beutl transitions from creating competing writers.

Checkout failure can occur after Git has updated the selected worktree/index but before it updates the temporary HEAD. Recovery therefore observes all three independently. If the exact target tree/index is present, a temporary HEAD still at the captured current tree is aligned to the target with `git update-ref --no-deref HEAD <target> <current>` before the protected reverse checkout; an already-target temporary HEAD proceeds directly, and any other value yields `OwnershipLost`. A response failure after that temporary-HEAD CAS is resolved by observing the ref. Unknown or partially written worktree content is never overwritten; only an index fingerprint proven to belong to Beutl may be restored before returning the uncertain outcome.

## Stale lock recovery (edge case: interrupted repository mutation)

On repository-lock failures (`index.lock`, the worktree-private `HEAD.lock`, or `another git process seems to be running`), resolve lock paths through the repository's Git directories. If no live Git child of this Beutl process exists and a lock file's mtime is older than 10 minutes, report that specific stale lock. Never auto-delete silently.

One-click removal requires explicit user consent and an atomic conditional-delete primitive. On Windows, capture the lock's volume/file ID when offering recovery, then open the path with `DELETE | FILE_READ_ATTRIBUTES` and no sharing, revalidate the mtime and volume/file ID through that exclusive handle, and set `FileDispositionInfo` on the same handle. A replacement cannot be removed by the stale path after the identity check. Platforms without an equivalent handle-bound primitive refuse in-app deletion and surface manual guidance; an ordinary path recheck followed by `File.Delete`, Unix unlink, or `FileShare`/`DeleteOnClose` sequence is not sufficient.
