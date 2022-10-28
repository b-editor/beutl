namespace Beutl.Api.Services;

public interface IPackageInstallProgress
{
    void Download(long downloaded, long length);

    void DownloadComplete();

    void Indeterminate(ActionType type);

    public enum ActionType
    {
        Downloading,
        Extracting,
        ResolvingDependencies,
    }
}
