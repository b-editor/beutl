using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Beutl.UnitTests.TestInfrastructure;

// A value type in an assembly that the GC can unload, as a package's types are after a live unload.
internal static class CollectibleTypes
{
    // Passes the type to use, and returns a weak reference to it once nothing on this stack holds it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference Use(Action<Type> use)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("CollectibleTypes"), AssemblyBuilderAccess.RunAndCollect);
        Type type = assembly.DefineDynamicModule("CollectibleTypes")
            .DefineType("Value", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType))
            .CreateType();
        use(type);
        return new WeakReference(type);
    }

    public static bool IsCollected(WeakReference type)
    {
        for (int i = 0; type.IsAlive && i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return !type.IsAlive;
    }
}
