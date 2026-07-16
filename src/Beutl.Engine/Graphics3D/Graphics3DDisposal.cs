using System.Runtime.ExceptionServices;

namespace Beutl.Graphics3D;

internal static class Graphics3DDisposal
{
    public static void DisposeAll(IDisposable? first, IDisposable? second)
    {
        Exception? failure = null;
        Capture(first, ref failure);
        Capture(second, ref failure);
        ThrowIfFailed(failure);
    }

    public static void DisposeAll(params IDisposable?[] resources)
    {
        Exception? failure = null;
        foreach (IDisposable? resource in resources)
        {
            Capture(resource, ref failure);
        }

        ThrowIfFailed(failure);
    }

    public static void Capture(IDisposable? resource, ref Exception? failure)
    {
        if (resource == null)
            return;

        try
        {
            resource.Dispose();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }
    }

    public static void ThrowIfFailed(Exception? failure)
    {
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
