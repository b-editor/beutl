using System.Reflection;

namespace Beutl;

public static class ArtifactProvider
{
    public static string GetArtifactDirectory()
    {
        var caller = new System.Diagnostics.StackFrame(1, false);
        MethodBase meth = caller.GetMethod()!;

        string callerMethodName = meth.Name;
        string callerClassName = meth.DeclaringType!.Name;
        string dir = Path.Combine("Artifacts", callerClassName, callerMethodName);

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return dir;
    }
}
