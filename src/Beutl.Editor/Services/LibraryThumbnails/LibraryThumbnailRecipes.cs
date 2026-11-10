using System.Reflection;
using Beutl.Engine;

namespace Beutl.Editor.Services.LibraryThumbnails;

/// <summary>
/// How the library's moving thumbnails show each type: the values a filter effect or a transition is set to, and
/// the values that move while the thumbnail plays.
/// </summary>
/// <remarks>
/// A filter effect plays over the project's content, such as the frame the preview shows or the object it is added to,
/// and a transition between the project's clips; where there is no content, over the sample picture and clips. A type
/// without a recipe plays with its default values, and many effects change nothing at theirs, so an extension
/// registers a recipe for each of its effects when it loads. A recipe of an unloaded assembly is removed with it.
/// </remarks>
public static class LibraryThumbnailRecipes
{
    private static readonly Lock s_lock = new();
    private static readonly Dictionary<Type, Recipe> s_recipes = [];

    static LibraryThumbnailRecipes()
    {
        TypeUnloadNotifier.TypesUnloading += OnTypesUnloading;
    }

    /// <summary>
    /// Raised after recipes are registered or removed, on the thread that changed them, with the types whose
    /// thumbnails changed.
    /// </summary>
    public static event Action<IReadOnlyList<Type>>? Changed;

    /// <summary>
    /// Sets how the thumbnail of <typeparamref name="T"/> is shown, replacing any recipe registered before.
    /// </summary>
    /// <param name="recipe">
    /// Called with a new instance each time the thumbnail is drawn, before its first frame. It sets the values to
    /// show, and makes values move through <see cref="LibraryThumbnailContext.Animate"/>.
    /// </param>
    public static void Register<T>(Action<T, LibraryThumbnailContext> recipe)
        where T : EngineObject
    {
        ArgumentNullException.ThrowIfNull(recipe);
        lock (s_lock)
        {
            s_recipes[typeof(T)] = new Recipe(recipe.Method.Module, (obj, context) => recipe((T)obj, context));
        }

        Changed?.Invoke([typeof(T)]);
    }

    /// <summary>Removes the recipe of <typeparamref name="T"/>, so its thumbnail shows its default values.</summary>
    public static bool Unregister<T>()
        where T : EngineObject
    {
        bool removed;
        lock (s_lock)
        {
            removed = s_recipes.Remove(typeof(T));
        }

        if (removed)
            Changed?.Invoke([typeof(T)]);

        return removed;
    }

    internal static Recipe? Find(Type type)
    {
        lock (s_lock)
        {
            return s_recipes.GetValueOrDefault(type);
        }
    }

    // Drops the recipes for the unloading types and those an unloading assembly wrote, which would keep it loaded.
    private static void OnTypesUnloading(Type[] types)
    {
        var assemblies = types.Select(type => type.Assembly).ToHashSet();
        var removed = new List<Type>();
        lock (s_lock)
        {
            foreach ((Type type, Recipe recipe) in s_recipes.ToArray())
            {
                if (assemblies.Contains(type.Assembly) || assemblies.Contains(recipe.Module.Assembly))
                {
                    s_recipes.Remove(type);
                    removed.Add(type);
                }
            }
        }

        if (removed.Count > 0)
            Changed?.Invoke(removed);
    }

    // The module is the one whose code the recipe runs, so a new build of it draws the thumbnail again.
    internal sealed record Recipe(Module Module, Action<EngineObject, LibraryThumbnailContext> Apply);
}
