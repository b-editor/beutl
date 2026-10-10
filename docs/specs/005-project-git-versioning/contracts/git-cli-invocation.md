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

- Each command runs through `System.Diagnostics.Process` (`GitProcess`). Cancellation and timeouts kill its process tree (`Kill(entireProcessTree: true)`; on Unix a command that has already exited is left alone, since its id may have been reused).
- Once the command has exited or been killed, the output pipes get up to 2 s to reach end of file. A process it left behind can keep them open: a hook's background job or an SSH connection master after a normal exit, or after a kill a descendant the kill could not reach (on Unix, one whose parent had already exited). Reading then stops and the pipes are closed. After a normal exit the result stands with what was read: stderr keeps the records read so far, and stdout is marked truncated, which a caller that set a limit already treats as incomplete; for a caller without a limit the command fails with exit code `-1` and a diagnostic. Nothing tracks the process afterwards: a repository lock it still holds surfaces as Git's normal lock error on a later command.
- Background work that already closed the command's pipes is neither waited for nor killed.
- Timeouts: local operations 30 s (a wedged local git indicates a broken repo → surface, don't spin); network operations unbounded but cancelable with progress (`--progress` on push, parsed from stderr).
- Exit code ≠ 0 ⇒ typed failure. The runner never retries; retry policy is the caller's.

## Snapshot commits

A snapshot is an ordinary Git commit of the project scope. The service stages the project with `git add -A` limited to the project pathspec, with excludes for `.beutl` state and every `.tmp` extension casing (the paths the generated ignore rules also name), and records it with a plain `git commit`. When something is also staged outside the project, it uses `git commit --only` on the same pathspecs instead, so that content stays staged and uncommitted; `--only` reads the changed files through their clean filters again, which a plain commit avoids for Git LFS media. Every other ignore rule is the user's: a snapshot leaves an ignored file out as plain Git does, and a one-time notice lists the ignored project files (an ignored folder once) so the choice does not go unnoticed. The listing runs with the snapshot's own excludes, so Beutl's state and scratch files cannot fill its 64 KiB capture limit; a listing cut off there is marked incomplete, and the full list is logged with the notice. Git runs the commit hooks, honours `commit.gpgSign` for a manual commit (automatic snapshots pass `--no-gpg-sign`), and updates the checked-out branch and the index under its own locks; a concurrent Git process surfaces as Git's own lock error. The commit runs without a timeout because hooks and signers can wait on the user, and only cancellation stops it. The message carries a `Beutl-Snapshot: <kind>` trailer. Before staging, the service refuses a merge, rebase or similar operation in progress, and an unignored nested repository, which Git would record as a gitlink. The snapshot's revision is HEAD as read after `git commit` returns. Git 2.36 is the minimum because tree inspection uses `git ls-tree --format`.

Stopping tracking of the `.beutl` and `.tmp` paths a repository already tracks, which needs the user's consent, is its own commit. `git commit --only` would record the files still on disk instead of their removal, so the commit is built from HEAD in a temporary index with `update-index --force-remove`, `write-tree` and `commit-tree --no-gpg-sign`, the branch moves with `git update-ref HEAD <new> <old>` (Git's own compare-and-swap), and `git rm -r --cached -f` then drops those paths from the live index. The files stay on disk. A failure other than a lock failure is logged and leaves the paths tracked.

## Pull, restore and branch switch

These run with the project closed, as plain Git porcelain under Git's own locks; a concurrent Git process, a hook, or any other setup of the user's surfaces as Git's own error. Each command that rewrites files clears the repository's LFS path filters, so media is never left as pointer text.

- **Pull** merges the commit the preflight fetched with `git merge --ff-only --no-overwrite-ignore <commit>`. Local changes in the project scope are first set aside with `git stash push --include-untracked -m "beutl: local changes before pull" -- <snapshot pathspecs>` (so `.beutl` state and `.tmp` files stay put) and brought back with `git stash pop`. A failed merge pops the stash back; a pop that conflicts leaves the entry in the stash, as Git always does.
- **Restore** runs `git restore --source=<commit> --staged --worktree -- <snapshot pathspecs>`, which deletes files the commit does not have, and records the result as a snapshot. Until that commit is made, a failure puts the scope back with `git restore --source=HEAD --staged --worktree`.
- **Branch switch** is `git switch --no-overwrite-ignore <name>` (`-c <name> --track origin/<name>` for a branch only origin has), and a new branch is `git switch -c`.

## Stale lock recovery (edge case: interrupted repository mutation)

On repository-lock failures (`index.lock`, the worktree-private `HEAD.lock`, or `another git process seems to be running`), resolve lock paths through the repository's Git directories. If no live Git child of this Beutl process exists and a lock file's mtime is older than 10 minutes, report that specific stale lock. Never auto-delete silently.

One-click removal requires explicit user consent and an atomic conditional-delete primitive. On Windows, capture the lock's volume/file ID when offering recovery, then open the path with `DELETE | FILE_READ_ATTRIBUTES` and no sharing, revalidate the mtime and volume/file ID through that exclusive handle, and set `FileDispositionInfo` on the same handle. A replacement cannot be removed by the stale path after the identity check. Platforms without an equivalent handle-bound primitive refuse in-app deletion and surface manual guidance; an ordinary path recheck followed by `File.Delete`, Unix unlink, or `FileShare`/`DeleteOnClose` sequence is not sufficient.
