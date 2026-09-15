# Data package publication recovery

Issue: [#2360](https://github.com/b-editor/beutl/issues/2360).

Material and template directories form one publication transaction, including
removal of a payload whose tag was removed. Missing enabled source directories
retain their existing no-op behavior. A staged payload's owner marker includes a
unique deployment ID in addition to package name and version.

Before the first destination rename, `install.json` records the package identity,
both destination paths, affected payloads, original ownership, registration intent,
and `Prepared` state in `.data-install-<id>`. Journal replacements are written to a
sibling temporary file, flushed, closed, and renamed over the previous record.
Payload files and owner markers are also flushed before publication.

| Last durable state | Startup action |
| --- | --- |
| `Prepared` | Persist rollback intent; restore both original payloads when their recorded owners can be verified. |
| `RollingBack` | Resume restoring originals; each rename can be retried. |
| `Published` | Both payload operations finished. Verify deployment ownership, finish requested package registration, and mark committed. |
| `Committed` | Keep the new payloads; remove staging and backups. |
| `Recovered` | Keep the restored payloads; remove staging. |

`Published` is recorded before registration. Therefore termination immediately
before or after repository persistence has the same recovery direction. A caught
registration failure records `RollingBack` before restoring payloads. Failure to
write the final commit record after successful registration retains `Published`
and reports success, so callers do not undo an already registered package.

Both executable entry points run recovery before constructing their font manager,
application, or payload watchers. The installer constructor also recovers for
other hosts. Publication, recovery, and uninstall share a file lock, preventing
another application process from recovering an active publication. An unresolved
journal blocks another publication or uninstall of that package. Startup recovery
waits for a live publisher to release its lock and retries only sharing violations;
other lock errors still fail immediately. Recovery callers can cancel this wait.
Ordinary publication and uninstall retain their existing busy-error behavior.

Recovery defers repository notifications until the committed state is durable and
both publication locks have been released. An observer can therefore wait for
additional installer work without holding recovery's locks. Failed terminal-record
writes retain the journal without notifying observers of an incomplete recovery.

Recovery validates the complete rollback before moving either payload. It rejects
unexpected destinations, linked transaction paths, and replacement directories
without the deployment's owner marker. It retains ambiguous or legacy staging
directories and logs their paths. Do not blindly delete these directories: a
`backup-*` directory may contain the only surviving old payload. Successful
cleanup removes the terminal journal last.

A journal with a markerless legacy original does not provide enough evidence to
identify that original after a restart. Such a rollback retains the journal and
all payloads for diagnosis, including during caught-failure rollback. A backup's
missing marker is never accepted merely because the recorded owner is also null.
Fully published upgrades can still complete after verifying their new owner
markers.

`PackageInstallerCrashRecoveryTests` launches separate testhost processes and
kills them after each backup/publication rename, journal transition, registration,
and rollback rename. The parent restarts recovery, checks both payloads and
registration, and repeats recovery to verify idempotence. Cases also cover first
installation, tag removal, invalid journals, and unowned destination collisions.
`PackageInstallerDataTests` covers caught registration failures, final journal
write failure, lock contention and cancellation, damaged staged copies, reentrant
observers, symbolic links, and ambiguous legacy backups. The subprocess suite also
verifies recovery waiting on a lock held by another process.

The PR #2405 claim that `File.GetAttributes` follows dangling Unix links was not
confirmed. .NET 10's [`FileStatus.Unix.cs`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Private.CoreLib/src/System/IO/FileStatus.Unix.cs)
uses `lstat`, retains a broken-link state, and reports `ReparsePoint` for it.
Regression tests exercise both live and dangling lock/staging links against the
existing rejection check and verify that external targets remain untouched.

This protocol targets process termination. These tests do not establish power-loss
durability of directory entries on every filesystem, nor make live watcher
notifications atomic across the two destination directories.
