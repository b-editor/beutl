using System.Text.Json;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    private const string DataInstallJournalFileName = "install.json";
    private static readonly Microsoft.Extensions.Logging.ILogger s_recoveryLogger = Log.CreateLogger<PackageInstaller>();

    internal Action<string>? AfterDataInstallStep { get; set; }

    private enum DataInstallPhase { Prepared, Published, RollingBack, Committed, Recovered }

    private sealed record DataInstallPayload(string Kind, string Destination, bool Enabled, bool HadOriginal, PayloadOwner? OriginalOwner);

    private sealed record DataInstallJournal(int Format, string DeploymentId, string Name, string Version,
        string MaterialsDestination, string TemplatesDestination, bool RegisterPackage,
        DataInstallPhase Phase, DataInstallPayload[] Payloads);

    private static FileStream AcquireDataInstallLock(bool waitForContention = false,
        CancellationToken cancellationToken = default, Action? onContention = null)
    {
        string home = BeutlEnvironment.GetHomeDirectoryPath();
        Directory.CreateDirectory(home);
        string path = Path.Combine(home, ".data-install.lock");
        bool reportedContention = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectDataInstallLink(path);
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (waitForContention && IsDataInstallLockContention(ex))
            {
                if (!reportedContention)
                {
                    s_recoveryLogger.LogInformation("Waiting for another process to finish publishing package data.");
                    onContention?.Invoke();
                    reportedContention = true;
                }
                // A crashed publisher releases the OS lock. Retry only sharing violations;
                // permission errors, invalid paths, and rejected links must still fail.
                cancellationToken.WaitHandle.WaitOne(50);
            }
        }
    }

    private static bool IsDataInstallLockContention(IOException exception)
        // .NET reports ERROR_SHARING_VIOLATION on Windows and raw EWOULDBLOCK on Unix.
        => exception.HResult == (OperatingSystem.IsWindows() ? unchecked((int)0x80070020)
            : OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() ? 35 : 11);

    // Run before fonts, templates, extension registration, or directory watchers read
    // payloads. The same gate protects publication in another application process.
    internal static void RecoverDataPackageInstalls(InstalledPackageRepository? repository = null,
        Action<string>? afterStep = null, CancellationToken cancellationToken = default)
    {
        List<Action> notifications = [];
        lock (s_dataPackageGate)
        {
            using FileStream installLock = AcquireDataInstallLock(waitForContention: true, cancellationToken,
                () => afterStep?.Invoke("waiting-for-lock"));
            foreach (string staging in Directory.GetDirectories(BeutlEnvironment.GetHomeDirectoryPath(), ".data-install-*"))
            {
                try
                {
                    DataInstallJournal journal = ReadDataInstallJournal(staging);
                    if (journal.Phase is DataInstallPhase.Prepared or DataInstallPhase.RollingBack)
                    {
                        WriteDataInstallJournal(staging, journal with { Phase = DataInstallPhase.RollingBack });
                        RollBackDataInstall(staging, journal, afterStep);
                    }
                    else if (journal.Phase == DataInstallPhase.Published)
                    {
                        foreach (DataInstallPayload payload in journal.Payloads)
                        {
                            if (payload.Enabled) RequirePublishedPayload(payload.Destination, journal);
                            else if (Path.Exists(payload.Destination))
                                throw new IOException($"Retired payload '{payload.Destination}' was replaced by another writer.");
                        }
                        Action? notify = null;
                        if (journal.RegisterPackage)
                        {
                            repository ??= new InstalledPackageRepository();
                            notify = repository.UpgradePackagesAndDeferNotifications(new PackageIdentity(journal.Name, NuGetVersion.Parse(journal.Version)));
                        }
                        WriteDataInstallJournal(staging, journal with { Phase = DataInstallPhase.Committed });
                        if (notify is not null) notifications.Add(notify);
                        afterStep?.Invoke("committed");
                    }

                    // Only terminal transactions reach cleanup. A crash during recursive
                    // deletion is harmless: the journal no longer authorizes any rollback.
                    DeleteRecoveredDataInstall(staging);
                    s_recoveryLogger.LogInformation("Recovered package data installation at {Staging}.", staging);
                }
                catch (Exception ex)
                {
                    s_recoveryLogger.LogWarning(ex,
                        "Keeping interrupted or legacy package data at {Staging}; automatic recovery could not establish a safe result.", staging);
                }
            }
        }
        // Repository observers can synchronously wait for more installer work. Publish
        // only durable terminal states, after releasing both publication locks.
        foreach (Action notify in notifications) notify();
    }

    private static DataInstallJournal CreateDataInstallJournal(string name, string version, string deploymentId,
        List<PayloadChange> changes, bool register)
    {
        var journal = new DataInstallJournal(1, deploymentId, name, version,
            Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name),
            Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), name), register,
            DataInstallPhase.Prepared, changes.Select(change => new DataInstallPayload(
                change.Kind, change.Destination, change.Enabled, Directory.Exists(change.Destination),
                ReadPayloadOwner(change.Destination))).ToArray());
        ValidateDataInstallJournal(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), ".data-install-" + deploymentId), journal);
        return journal;
    }

    private static DataInstallJournal ReadDataInstallJournal(string staging)
    {
        RejectDataInstallLink(staging);
        string path = Path.Combine(staging, DataInstallJournalFileName);
        RejectDataInstallLink(path);
        var journal = JsonSerializer.Deserialize<DataInstallJournal>(File.ReadAllText(path))
            ?? throw new IOException($"Missing install journal in '{staging}'.");
        ValidateDataInstallJournal(staging, journal);
        return journal;
    }

    private static void ValidateDataInstallJournal(string staging, DataInstallJournal journal)
    {
        ValidatePackageName(journal.Name);
        string materials = Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), journal.Name);
        string templates = Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), journal.Name);
        if (journal.Format != 1 || !Guid.TryParseExact(journal.DeploymentId, "N", out _)
            || Path.GetFileName(staging) != ".data-install-" + journal.DeploymentId
            || !NuGetVersion.TryParse(journal.Version, out _) || !Enum.IsDefined(journal.Phase)
            || journal.MaterialsDestination != materials || journal.TemplatesDestination != templates
            || journal.Payloads is null || journal.Payloads.Length > 2
            || journal.Payloads.Select(p => p.Kind).Distinct().Count() != journal.Payloads.Length)
            throw new IOException($"Invalid install journal in '{staging}'.");

        RejectDataInstallLink(staging);
        foreach (DataInstallPayload payload in journal.Payloads)
        {
            string expected = payload.Kind switch
            {
                MaterialsContentDirectory => materials,
                TemplatesContentDirectory => templates,
                _ => throw new IOException("Unknown data payload kind."),
            };
            if (payload.Destination != expected || (!payload.Enabled && !payload.HadOriginal))
                throw new IOException("Invalid data payload destination or operation.");
            RejectDataInstallLink(Path.GetDirectoryName(expected)!);
            RejectDataInstallLink(expected);
            RejectDataInstallLink(Path.Combine(staging, payload.Kind));
            RejectDataInstallLink(Path.Combine(staging, "backup-" + payload.Kind));
        }
    }

    private static void RejectDataInstallLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Package recovery cannot follow a link at '{path}'.");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
    }

    private static void RequirePublishedPayload(string path, DataInstallJournal journal)
    {
        RejectDataInstallLink(path);
        RejectDataInstallLink(Path.Combine(path, PayloadOwnerFileName));
        if (!Directory.Exists(path) || ReadPayloadOwner(path) != new PayloadOwner(journal.Name, journal.Version, journal.DeploymentId))
            throw new IOException($"Cannot identify the published payload at '{path}'.");
    }

    private static void ValidateDataInstallRollback(string staging, DataInstallJournal journal)
    {
        // Check both payloads before touching either one. Never move a directory merely
        // because its name matches a package: the new copy must carry this transaction ID.
        foreach (DataInstallPayload payload in journal.Payloads)
        {
            string staged = Path.Combine(staging, payload.Kind);
            string backup = Path.Combine(staging, "backup-" + payload.Kind);
            // A damaged staged copy is not an original and need not be trusted to
            // restore the backup. Only validate a published directory we must move.
            if (payload.HadOriginal && payload.OriginalOwner is null)
                throw new IOException($"The legacy original for '{payload.Destination}' has no recorded owner; keeping it for diagnosis.");
            if (Path.Exists(backup))
            {
                RejectDataInstallLink(Path.Combine(backup, PayloadOwnerFileName));
                if (!payload.HadOriginal || !Directory.Exists(backup) || ReadPayloadOwner(backup) != payload.OriginalOwner)
                    throw new IOException($"Cannot identify the original payload at '{backup}'.");
            }
            else if (payload.HadOriginal)
            {
                // The old-to-backup rename has not happened, or recovery already restored
                // it. In both cases the destination is left alone.
                RejectDataInstallLink(Path.Combine(payload.Destination, PayloadOwnerFileName));
                if (!Directory.Exists(payload.Destination) || ReadPayloadOwner(payload.Destination) != payload.OriginalOwner)
                    throw new IOException($"The original payload is missing at '{payload.Destination}'.");
                continue;
            }
            if (Path.Exists(payload.Destination))
            {
                RequirePublishedPayload(payload.Destination, journal);
                if (Path.Exists(staged)) throw new IOException($"Both staged and published payloads exist for '{payload.Kind}'.");
            }
        }
    }

    private static void RollBackDataInstall(string staging, DataInstallJournal journal, Action<string>? afterStep)
    {
        ValidateDataInstallJournal(staging, journal);
        ValidateDataInstallRollback(staging, journal);
        foreach (DataInstallPayload payload in journal.Payloads.Reverse())
        {
            string staged = Path.Combine(staging, payload.Kind);
            string backup = Path.Combine(staging, "backup-" + payload.Kind);
            if (Directory.Exists(backup) || !payload.HadOriginal)
            {
                if (Directory.Exists(payload.Destination))
                {
                    Directory.Move(payload.Destination, staged);
                    afterStep?.Invoke("unpublish-" + payload.Kind);
                }
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, payload.Destination);
                    afterStep?.Invoke("restore-" + payload.Kind);
                }
            }
        }
        WriteDataInstallJournal(staging, journal with { Phase = DataInstallPhase.Recovered });
        afterStep?.Invoke("recovered");
    }

    private static void WriteDataInstallJournal(string staging, DataInstallJournal journal)
    {
        string path = Path.Combine(staging, DataInstallJournalFileName);
        string temporary = path + ".tmp";
        RejectDataInstallLink(temporary);
        WriteDurableJson(temporary, journal);
        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteDurableJson<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteRecoveredDataInstall(string staging)
    {
        // Keep the terminal journal until every backup is gone. Directory.Delete(true)
        // alone could remove the journal first and strand an ambiguous legacy directory.
        foreach (string directory in Directory.GetDirectories(staging)) Directory.Delete(directory, recursive: true);
        foreach (string file in Directory.GetFiles(staging))
            if (Path.GetFileName(file) != DataInstallJournalFileName) File.Delete(file);
        File.Delete(Path.Combine(staging, DataInstallJournalFileName));
        Directory.Delete(staging);
    }

    private static void EnsureNoPendingDataInstall(string name, string? currentStaging)
    {
        foreach (string staging in Directory.GetDirectories(BeutlEnvironment.GetHomeDirectoryPath(), ".data-install-*"))
        {
            if (staging == currentStaging) continue;
            DataInstallJournal? journal;
            try
            {
                RejectDataInstallLink(staging);
                string path = Path.Combine(staging, DataInstallJournalFileName);
                RejectDataInstallLink(path);
                journal = JsonSerializer.Deserialize<DataInstallJournal>(File.ReadAllText(path));
            }
            catch { continue; } // Unknown legacy data is retained, never deleted or inferred to belong to this package.
            if (journal is null || !StringComparer.OrdinalIgnoreCase.Equals(journal.Name, name)) continue;
            // A readable identity still blocks writes when its paths or state are invalid.
            // Otherwise a failed recovery could be overwritten by the very next install.
            ValidateDataInstallJournal(staging, journal);
            if (journal.Phase is not (DataInstallPhase.Committed or DataInstallPhase.Recovered))
                throw new IOException($"Package '{name}' has an unfinished installation at '{staging}'; restart to recover it first.");
        }
    }
}
