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
            _logger.LogInformation("Delete unnecessary packages.");

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
                Failed.Value = true;
            }
            else
            {
                Succeeded.Value = true;
            }

        }
        catch (OperationCanceledException)
        {
            ErrorMessage.Value = Strings.Operation_canceled;
            Canceled.Value = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occured.");
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
