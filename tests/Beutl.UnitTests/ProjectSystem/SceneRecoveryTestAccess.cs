using System.Reflection;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

// The private SceneRecovery steps and maps that the recovery tests reach directly, kept in one place.
internal static class SceneRecoveryTestAccess
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static Dictionary<TKey, Guid> GetRecoveryMap<TKey>(Scene scene, string fieldName)
        where TKey : notnull
        => (Dictionary<TKey, Guid>)typeof(SceneRecovery).GetField(fieldName, PrivateInstance)!
            .GetValue(scene.Recovery)!;

    // Records that the element Id from became to, and rewrites the references to it.
    public static void MigrateReferences(Scene scene, Guid from, Guid to)
    {
        GetRecoveryMap<Guid>(scene, "_pendingRecoveredElementIdMigrations")[from] = to;
        MigrateReferences(scene);
    }

    public static void MigrateReferences(Scene scene) => Invoke(scene, "MigrateRecoveredElementReferences");

    public static void ReassignDuplicateIds(Scene scene) => Invoke(scene, "ReassignDuplicateRecoveredIds");

    private static void Invoke(Scene scene, string methodName)
        => typeof(SceneRecovery).GetMethod(methodName, PrivateInstance)!.Invoke(scene.Recovery, null);
}
