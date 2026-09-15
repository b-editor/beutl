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
/// </remarks>
internal sealed class StorageWriteTransaction : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<StorageWriteTransaction>();
    private static readonly AsyncLocal<StorageWriteTransaction?> s_current = new();
    private static readonly AsyncLocal<Action<StorageWriteStep, string>?> s_faultInjector = new();

    private readonly object _sync = new();
    // File entries and compensations, in the order they were recorded.
    private readonly List<object> _journal = [];
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.Ordinal);
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
            File.Move(temporaryPath, destinationPath, overwrite);
        }
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
                WriteBytesAtomically(entry.FullPath, entry.PreviousBytes);
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

    private static void WriteBytesAtomically(string path, byte[] bytes)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

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
        bool isCompatibilityGate)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        lock (_sync)
        {
            ThrowIfCompleted();
            if (_files.TryGetValue(fullPath, out FileEntry? journaled))
            {
                File.Move(temporaryPath, destinationPath, overwrite);
                journaled.IsCompatibilityGate |= isCompatibilityGate;
                return;
            }

            // A move that may not overwrite either creates the destination or fails, so only a replacement has
            // previous bytes to keep. The entry is added after the move because a failed move changes nothing.
            byte[]? previousBytes = overwrite && File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
            File.Move(temporaryPath, destinationPath, overwrite);
            var entry = new FileEntry(fullPath, previousBytes) { IsCompatibilityGate = isCompatibilityGate };
            _files.Add(fullPath, entry);
            _journal.Add(entry);
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

    private sealed class FileEntry(string fullPath, byte[]? previousBytes)
    {
        public string FullPath { get; } = fullPath;

        // Null when the destination did not exist until this transaction created it.
        public byte[]? PreviousBytes { get; } = previousBytes;

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
}
