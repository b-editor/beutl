using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Serialization;

/// <summary>
/// Journals the files one save replaces, so that a save which fails part-way can put every one of them back.
/// </summary>
/// <remarks>
/// <para>
/// A write still reaches its destination through a flushed temporary sibling and one rename, so no reader
/// observes a torn file. While a transaction is active on the current execution flow, including the workers a
/// <see cref="Parallel"/> loop starts from that flow, <see cref="MoveIntoPlace"/> records what the destination
/// held before its first replacement: its bytes, or that it did not exist.
/// </para>
/// <para>
/// <see cref="Rollback"/> restores those states newest first. A project file carries the compatibility gate
/// (<c>minAppVersion</c>) that older applications check before reading any sidecar, so it is restored only after
/// every other file has been. If any file cannot be restored, bytes the failed save wrote may remain on disk, and
/// the gate that save raised is retained rather than lowered beneath them.
/// </para>
/// <para>
/// The journal is held in memory. It covers failures the saving process observes; a terminated process still
/// leaves only whole files, and the gate is written before the sidecars it covers.
/// </para>
/// <para>
/// <see cref="WriteInBatch"/> keeps those guarantees for a save that changes many files at a cost that does not grow
/// with a flush per file: its files are all flushed before the first of them is renamed.
/// </para>
/// </remarks>
internal sealed class StorageWriteTransaction : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<StorageWriteTransaction>();
    private static readonly AsyncLocal<StorageWriteTransaction?> s_current = new();
    private static readonly AsyncLocal<Action<StorageWriteStep, string>?> s_faultInjector = new();

    private readonly object _sync = new();
    // File entries and compensations, in the order they were recorded.
    private readonly List<object> _journal = [];
    // Keyed by spelling. An alias of a journaled file, such as another case on a case-insensitive volume or a path
    // through a link, is journaled again, and newest-first replay still ends on the bytes the file held before the
    // transaction. A case-insensitive key would instead merge distinct files on a case-sensitive volume and leave
    // the second one unrestored. Gates are restored after every other entry, so journal a gate before any other
    // write that can reach the same file.
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.Ordinal);
    // The temporary files a running batch has written, with their destinations and what those held.
    private List<(string Temporary, string Destination, byte[]? PreviousBytes)>? _batch;
    private State _state;

    private StorageWriteTransaction()
    {
    }

    private enum State
    {
        Active,
        Committed,
        RolledBack,
    }

    internal static StorageWriteTransaction? Current => s_current.Value;

    // The transaction on this flow, while it runs a batch.
    internal static StorageWriteTransaction? CurrentBatch
        => s_current.Value is { _batch: not null } transaction ? transaction : null;

    internal static StorageWriteTransaction Begin()
    {
        if (s_current.Value is not null)
        {
            throw new InvalidOperationException("A storage write transaction is already active.");
        }

        var transaction = new StorageWriteTransaction();
        s_current.Value = transaction;
        return transaction;
    }

    /// <summary>
    /// Moves a completed temporary file to <paramref name="destinationPath"/>, journaling the destination's
    /// previous state when a transaction is active.
    /// </summary>
    /// <param name="temporaryPath">The flushed temporary file to move.</param>
    /// <param name="destinationPath">The file to create or replace.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="isCompatibilityGate">
    /// The destination is a project file whose version metadata guards the files saved with it.
    /// </param>
    internal static void MoveIntoPlace(
        string temporaryPath,
        string destinationPath,
        bool overwrite,
        bool isCompatibilityGate = false)
    {
        InjectFault(StorageWriteStep.Replace, destinationPath);
        if (s_current.Value is { } transaction)
        {
            transaction.MoveIntoPlaceCore(temporaryPath, destinationPath, overwrite, isCompatibilityGate);
        }
        else
        {
            MovePreservingPermissions(temporaryPath, destinationPath, overwrite);
        }
    }

    /// <summary>
    /// Deletes <paramref name="path"/>, journaling its bytes and permissions when a transaction is active, so a
    /// rollback restores it as it restores a replaced file.
    /// </summary>
    internal static void Delete(string path)
    {
        if (s_current.Value is { } transaction)
        {
            transaction.DeleteCore(path);
        }
        else
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Runs <paramref name="writes"/>, which may write from several threads, and moves the files they wrote into place
    /// only after the last of them is written and flushed.
    /// </summary>
    /// <remarks>
    /// Flushing a device's cache for each file takes milliseconds and holds up the volume's other writes, so a save
    /// that changes thousands of files would take minutes. On macOS the files of a batch reach the device through
    /// fsync, and one flush of its cache then covers them all, as Git's core.fsyncMethod=batch does. Other systems flush
    /// each file as it is written. A write that fails leaves every destination as it was; a move that fails leaves the
    /// moves before it for <see cref="Rollback"/> to restore.
    /// </remarks>
    internal void WriteInBatch(Action writes)
    {
        var batch = new List<(string Temporary, string Destination, byte[]? PreviousBytes)>();
        lock (_sync)
        {
            ThrowIfCompleted();
            if (_batch is not null)
            {
                throw new InvalidOperationException("A batch is already running.");
            }

            _batch = batch;
        }

        bool moved = false;
        try
        {
            try
            {
                writes();
            }
            finally
            {
                lock (_sync)
                {
                    _batch = null;
                }
            }

            FlushDeviceCaches(batch);
            foreach ((string temporary, string destination, byte[]? previousBytes) in batch)
            {
                InjectFault(StorageWriteStep.Replace, destination);
                MoveIntoPlaceCore(temporary, destination, overwrite: true, isCompatibilityGate: false, previousBytes);
            }

            moved = true;
        }
        finally
        {
            if (!moved)
            {
                foreach ((string temporary, _, _) in batch)
                {
                    TryDeleteTemporaryFile(temporary);
                }
            }
        }
    }

    // Reads what the destination holds here, on the writer's thread, because the moves run one at a time.
    internal void AddToBatch(string temporaryPath, string destinationPath)
    {
        byte[]? previousBytes = File.Exists(destinationPath) ? File.ReadAllBytes(destinationPath) : null;
        lock (_sync)
        {
            _batch!.Add((temporaryPath, destinationPath, previousBytes));
        }
    }

    internal static void FlushAheadOfBatch(FileStream stream)
    {
        if (OperatingSystem.IsMacOS())
        {
            stream.Flush(flushToDisk: false);
            // .NET's flush to disk also flushes the device's cache, which the end of the batch does once for all files.
            if (MacFSync(stream.SafeFileHandle) == 0)
            {
                return;
            }
        }

        stream.Flush(flushToDisk: true);
    }

    private static void FlushDeviceCaches(List<(string Temporary, string Destination, byte[]? PreviousBytes)> batch)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // One per folder rather than per device, which .NET does not tell: a batch rarely spans more than one.
        var folders = new HashSet<string?>(StringComparer.Ordinal);
        foreach ((string temporary, _, _) in batch)
        {
            if (folders.Add(Path.GetDirectoryName(temporary)))
            {
                InjectFault(StorageWriteStep.FlushDevice, temporary);
                using var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Flush(flushToDisk: true);
            }
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s_logger.LogWarning(ex, "Could not remove the temporary file {Path} of a failed save.", path);
        }
    }

    [DllImport("libSystem.B.dylib", EntryPoint = "fsync", SetLastError = true)]
    private static extern int MacFSync(SafeHandle file);

    internal static FileStream CreateTemporaryFile(string temporaryPath, string destinationPath, bool privateContents = false)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        // Keep replacement bytes private for the entire write, not only after publication.
        // Brand-new files retain the normal creation permissions subject to the user's umask.
        if (!OperatingSystem.IsWindows() && (privateContents || File.Exists(destinationPath)))
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(temporaryPath, options);
    }

    private static UnixFileMode? MovePreservingPermissions(string temporaryPath, string destinationPath, bool overwrite)
    {
        UnixFileMode? previousMode = null;
        if (!OperatingSystem.IsWindows() && overwrite && File.Exists(destinationPath))
        {
            previousMode = File.GetUnixFileMode(destinationPath);
            File.SetUnixFileMode(temporaryPath, previousMode.Value);
        }

        File.Move(temporaryPath, destinationPath, overwrite);
        return previousMode;
    }

    /// <summary>
    /// Invokes <paramref name="injector"/> with the full path before each replacement and restoration on the
    /// current flow. A test fails the chosen step by throwing from it.
    /// </summary>
    internal static IDisposable InjectFaultsForTesting(Action<StorageWriteStep, string> injector)
    {
        ArgumentNullException.ThrowIfNull(injector);
        var scope = new FaultInjectionScope(s_faultInjector.Value);
        s_faultInjector.Value = injector;
        return scope;
    }

    /// <summary>Runs <paramref name="compensation"/> if this transaction rolls back.</summary>
    /// <remarks>Use it for in-memory state that records a journaled write as done.</remarks>
    internal void OnRollback(Action compensation)
    {
        ArgumentNullException.ThrowIfNull(compensation);
        lock (_sync)
        {
            ThrowIfCompleted();
            _journal.Add(compensation);
        }
    }

    internal void Commit()
    {
        lock (_sync)
        {
            ThrowIfCompleted();
            _state = State.Committed;
            _journal.Clear();
            _files.Clear();
        }

        EndScope();
    }

    /// <summary>
    /// Restores every journaled file and runs the compensations, newest first. Failures are logged rather than
    /// thrown, so the caller can rethrow the failure that caused the rollback.
    /// </summary>
    /// <returns>Whether every journaled file was restored.</returns>
    internal bool Rollback()
    {
        object[] journal;
        lock (_sync)
        {
            ThrowIfCompleted();
            _state = State.RolledBack;
            journal = [.. _journal];
            _journal.Clear();
            _files.Clear();
        }

        EndScope();

        var gates = new List<FileEntry>();
        bool restoredAll = true;
        for (int i = journal.Length - 1; i >= 0; i--)
        {
            switch (journal[i])
            {
                case FileEntry { IsCompatibilityGate: true } gate:
                    gates.Add(gate);
                    break;
                case FileEntry file:
                    restoredAll &= TryRestore(file);
                    break;
                case Action compensation:
                    RunCompensation(compensation);
                    break;
            }
        }

        if (!restoredAll)
        {
            foreach (FileEntry gate in gates)
            {
                s_logger.LogWarning(
                    "Kept the compatibility metadata written to {Path} because the failed save could not restore every file it covers.",
                    gate.FullPath);
            }

            return false;
        }

        foreach (FileEntry gate in gates)
        {
            restoredAll &= TryRestore(gate);
        }

        return restoredAll;
    }

    public void Dispose()
    {
        bool active;
        lock (_sync)
        {
            active = _state == State.Active;
        }

        if (active)
        {
            Rollback();
        }
        else
        {
            EndScope();
        }
    }

    private static void InjectFault(StorageWriteStep step, string path)
    {
        if (s_faultInjector.Value is { } injector)
        {
            injector(step, Path.GetFullPath(path));
        }
    }

    private static bool TryRestore(FileEntry entry)
    {
        try
        {
            InjectFault(StorageWriteStep.Restore, entry.FullPath);
            if (entry.PreviousBytes is null)
            {
                if (File.Exists(entry.FullPath))
                {
                    File.Delete(entry.FullPath);
                }
            }
            else
            {
                WriteBytesAtomically(entry.FullPath, entry.PreviousBytes, entry.PreviousMode);
            }

            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Could not restore {Path} after a failed save.", entry.FullPath);
            return false;
        }
    }

    private static void RunCompensation(Action compensation)
    {
        try
        {
            compensation();
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Could not restore in-memory state after a failed save.");
        }
    }

    private static void WriteBytesAtomically(string path, byte[] bytes, UnixFileMode? previousMode)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = CreateTemporaryFile(temporaryPath, path, privateContents: true))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows() && previousMode is { } mode)
                File.SetUnixFileMode(temporaryPath, mode);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private void MoveIntoPlaceCore(
        string temporaryPath,
        string destinationPath,
        bool overwrite,
        bool isCompatibilityGate,
        byte[]? knownPreviousBytes = null)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        lock (_sync)
        {
            ThrowIfCompleted();
            if (_files.TryGetValue(fullPath, out FileEntry? journaled))
            {
                MovePreservingPermissions(temporaryPath, destinationPath, overwrite);
                journaled.IsCompatibilityGate |= isCompatibilityGate;
                return;
            }

            // A move that may not overwrite either creates the destination or fails, so only a replacement has
            // previous bytes to keep. The entry is added after the move because a failed move changes nothing.
            byte[]? previousBytes = knownPreviousBytes ?? (overwrite && File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null);
            UnixFileMode? previousMode = MovePreservingPermissions(temporaryPath, destinationPath, overwrite);
            var entry = new FileEntry(fullPath, previousBytes, previousMode) { IsCompatibilityGate = isCompatibilityGate };
            _files.Add(fullPath, entry);
            _journal.Add(entry);
        }
    }

    private void DeleteCore(string path)
    {
        string fullPath = Path.GetFullPath(path);
        lock (_sync)
        {
            ThrowIfCompleted();
            // A file journaled already keeps the state it had before the transaction. As for a move, the entry
            // is added after the delete, because a failed delete changes nothing.
            bool journal = !_files.ContainsKey(fullPath) && File.Exists(fullPath);
            byte[]? previousBytes = journal ? File.ReadAllBytes(fullPath) : null;
            UnixFileMode? previousMode = journal && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(fullPath) : null;
            File.Delete(fullPath);
            if (journal)
            {
                var entry = new FileEntry(fullPath, previousBytes, previousMode);
                _files.Add(fullPath, entry);
                _journal.Add(entry);
            }
        }
    }

    private void ThrowIfCompleted()
    {
        if (_state != State.Active)
        {
            throw new InvalidOperationException("The storage write transaction has already completed.");
        }
    }

    private void EndScope()
    {
        if (ReferenceEquals(s_current.Value, this))
        {
            s_current.Value = null;
        }
    }

    private sealed class FileEntry(string fullPath, byte[]? previousBytes, UnixFileMode? previousMode)
    {
        public string FullPath { get; } = fullPath;

        // Null when the destination did not exist until this transaction created it.
        public byte[]? PreviousBytes { get; } = previousBytes;

        public UnixFileMode? PreviousMode { get; } = previousMode;

        public bool IsCompatibilityGate { get; set; }
    }

    private sealed class FaultInjectionScope(Action<StorageWriteStep, string>? previous) : IDisposable
    {
        public void Dispose() => s_faultInjector.Value = previous;
    }
}

internal enum StorageWriteStep
{
    Replace,
    Restore,
    FlushDevice,
}
