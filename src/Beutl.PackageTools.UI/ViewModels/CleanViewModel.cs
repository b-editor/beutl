using System.Reactive.Linq;

using Beutl.Logging;
using Beutl.Utilities;

using NuGet.Packaging.Core;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public record CleanPackage(PackageIdentity Package, ReactiveProperty<bool> Condition);

public class CleanViewModel : IProgress<double>
{
    private readonly ILogger _logger = Log.CreateLogger<CleanViewModel>();
    private readonly PackageInstaller _installer;

    public CleanViewModel(BeutlApiApplication app)
    {
        _installer = app.GetResource<PackageInstaller>();
        PackageCleanContext context = _installer.PrepareForClean(null);

        Items = context.UnnecessaryPackages.Select(v => new CleanPackage(v, new(true)))
            .Do(v => v.Condition.Skip(1).Subscribe(c => ConditionChanged(v.Package, c)))
            .ToArray();

        SizeToBeReleased.Value = context.SizeToBeReleased;
        SizeToBeReleasedString = SizeToBeReleased.Select(v => StringFormats.ToHumanReadableSize(v))
            .ToReadOnlyReactiveProperty()!;

        Finished = Succeeded.CombineLatest(Failed, Canceled)
            .Select(t => t.First || t.Second || t.Third)
            .ToReadOnlyReactiveProperty();
    }

    private void ConditionChanged(PackageIdentity package, bool condition)
    {
        _logger.LogDebug("Condition changed for package {PackageId} to {Condition}.", package.Id, condition);
        long size = SizeToBeReleased.Value;

        string directory = Helper.PackagePathResolver.GetInstalledPath(package);
        foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (condition)
            {
                size += new FileInfo(file).Length;
            }
            else
            {
                size -= new FileInfo(file).Length;
            }
        }

        SizeToBeReleased.Value = size;
        _logger.LogDebug("Updated SizeToBeReleased to {Size}.", size);
    }

    public CleanPackage[] Items { get; }

    public ReactiveProperty<long> SizeToBeReleased { get; } = new();

    public ReadOnlyReactiveProperty<string> SizeToBeReleasedString { get; }

    public ReactiveProperty<double> Progress { get; } = new();

    public ReactiveProperty<string> Message { get; } = new();

    public ReactiveProperty<string> ErrorMessage { get; } = new();

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public ReactiveProperty<bool> Canceled { get; } = new();

    public ReadOnlyReactiveProperty<bool> Finished { get; }

    public void Run(CancellationToken token)
    {
        try
        {
            _logger.LogInformation("Starting to delete unnecessary packages.");

            PackageCleanContext context = _installer.PrepareForClean(Items.Where(v => !v.Condition.Value).Select(v => v.Package), token);

            Message.Value = Strings.Deleting_unnecessary_packages;
            _installer.Clean(context, this, token);
            Message.Value = Strings.Deleted_unnecessary_packages;

            if (context.FailedPackages?.Count > 0)
            {
                ErrorMessage.Value = $"""
                    {Strings.These_packages_were_not_deleted_successfully}
                    {string.Join('\n', context.FailedPackages.Select(i => $"- {Path.GetFileName(i)}"))}
                    """;
                _logger.LogWarning("Some packages were not deleted successfully: {FailedPackages}", context.FailedPackages);
                Failed.Value = true;
            }
            else
            {
                _logger.LogInformation("Successfully deleted all unnecessary packages.");
                Succeeded.Value = true;
            }

        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Operation was canceled.");
            ErrorMessage.Value = Strings.Operation_canceled;
            Canceled.Value = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while deleting packages.");
            ErrorMessage.Value = ex.Message;
            Failed.Value = true;
        }
    }

    void IProgress<double>.Report(double value)
    {
        if (double.IsFinite(value))
        {
            Progress.Value = value * 100;
        }
    }
}
