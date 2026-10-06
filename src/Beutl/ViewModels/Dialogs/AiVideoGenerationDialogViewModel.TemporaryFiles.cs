using Beutl.Services.AI;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiVideoGenerationDialogViewModel
{
    private readonly HashSet<string> _temporaryFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _temporaryFileLeases = new(StringComparer.Ordinal);
    // Temporary frame files retained for each unsettled request name.
    private readonly Dictionary<string, IDisposable> _framesHeldByName =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> _temporaryFilesPendingDeletion = new(StringComparer.Ordinal);

    // Retain each temporary frame until the unsettled name that includes its exact contents is
    // settled. Deleting it on a model change would make the same request impossible to resend.
    private void HoldFramesFor(AiRequestName name, string? firstFrame, string? lastFrame)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;

        var held = new CompositeDisposable(
            AcquireTemporaryFileLease(firstFrame),
            AcquireTemporaryFileLease(lastFrame));
        lock (_lifetimeGate)
        {
            if (_framesHeldByName.Remove(name.Key, out IDisposable? previous))
                previous.Dispose();
            _framesHeldByName.Add(name.Key, held);
        }
    }

    private void ReleaseFramesOf(AiRequestName name)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;

        IDisposable? held;
        lock (_lifetimeGate)
        {
            if (!_framesHeldByName.Remove(name.Key, out held))
                return;
        }

        held.Dispose();
    }

    private bool IsTemporaryFile(string path)
    {
        lock (_lifetimeGate)
            return _temporaryFiles.Contains(path);
    }

    private IDisposable AcquireTemporaryFileLease(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Disposable.Empty;

        lock (_lifetimeGate)
        {
            if (!_temporaryFiles.Contains(path))
                return Disposable.Empty;

            _temporaryFileLeases[path] = _temporaryFileLeases.GetValueOrDefault(path) + 1;
        }

        return Disposable.Create(() => ReleaseTemporaryFileLease(path));
    }

    private void ReleaseTemporaryFileLease(string path)
    {
        lock (_lifetimeGate)
        {
            if (!_temporaryFileLeases.TryGetValue(path, out int count))
                return;

            if (count > 1)
            {
                _temporaryFileLeases[path] = count - 1;
                return;
            }

            _temporaryFileLeases.Remove(path);
            if (_temporaryFilesPendingDeletion.Contains(path))
            {
                DeleteTrackedTemporaryFile(path);
            }
        }
    }

    private void RequestTemporaryFileDeletion(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        lock (_lifetimeGate)
        {
            if (!_temporaryFiles.Contains(path))
                return;

            _temporaryFilesPendingDeletion.Add(path);
            if (!_temporaryFileLeases.ContainsKey(path))
            {
                DeleteTrackedTemporaryFile(path);
            }
        }
    }

    // Called with _lifetimeGate held so a new lease cannot race with deletion.
    private void DeleteTrackedTemporaryFile(string path)
    {
        if (DeleteTemporaryFile(path))
        {
            _temporaryFiles.Remove(path);
            _temporaryFilesPendingDeletion.Remove(path);
        }
    }

    private bool DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to remove temporary AI file {Path}", path);
            return false;
        }
    }
}
