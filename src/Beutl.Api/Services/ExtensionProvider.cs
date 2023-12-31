using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Extensibility;

using static Beutl.Configuration.ExtensionConfig;

namespace Beutl.Api.Services;

public sealed class ExtensionProvider : IBeutlApiResource
{
    private readonly Dictionary<int, Extension[]> _allExtensions = [];
    private readonly ExtensionConfig _config = GlobalConfiguration.Instance.ExtensionConfig;
    private readonly Dictionary<Type, Array> _cache = [];
    private readonly CoreList<Extension> _extensions = [];
    private readonly object _lock = new();
    private bool _cacheInvalidated;

    public ExtensionProvider()
    {
    }

    public static ExtensionProvider Current { get; } = new();

    public ICoreReadOnlyList<Extension> AllExtensions => _extensions;

    public TExtension[] GetExtensions<TExtension>()
        where TExtension : Extension
    {
        lock (_lock)
        {
            if (_cacheInvalidated)
            {
                _cache.Clear();
                _cacheInvalidated = false;
            }

            if (_cache.TryGetValue(typeof(TExtension), out Array? result))
            {
                return (TExtension[])result;
            }
            else
            {
                TExtension[] exts = AllExtensions.OfType<TExtension>().ToArray();
                _cache[typeof(TExtension)] = exts;
                return exts;
            }
        }
    }

    public EditorExtension? MatchEditorExtension(string file)
    {
        lock (_lock)
        {
            string? fileExt = Path.GetExtension(file);

            if (_config.EditorExtensions.TryGetValue(fileExt, out ICoreList<TypeLazy>? list))
            {
                foreach (Extension extension in AllExtensions)
                {
                    Type extType = extension.GetType();
                    if (extension is not EditorExtension editorExtension) continue;

                    foreach (TypeLazy type in list.GetMarshal().Value)
                    {
                        if (extType == type.Type
                            && editorExtension.IsSupported(file))
                        {
                            return editorExtension;
                        }
                    }
                }
            }

            foreach (Extension extension in AllExtensions)
            {
                if (extension is EditorExtension editorExtension &&
                    editorExtension.IsSupported(file))
                {
                    return editorExtension;
                }
            }

            return null;
        }
    }

    public ProjectItemExtension? MatchProjectItemExtension(string file)
    {
        lock (_lock)
        {
            foreach (Extension extension in AllExtensions)
            {
                if (extension is ProjectItemExtension wsiExtension &&
                    wsiExtension.IsSupported(file))
                {
                    return wsiExtension;
                }
            }

            return null;
        }
    }

    public IEnumerable<ProjectItemExtension> MatchProjectItemExtensions(string file)
    {
        lock (_lock)
        {
            foreach (Extension extension in AllExtensions)
            {
                if (extension is ProjectItemExtension wsiExtension &&
                    wsiExtension.IsSupported(file))
                {
                    yield return wsiExtension;
                }
            }
        }
    }

    public void AddExtensions(int id, Extension[] extensions)
    {
        lock (_lock)
        {
            if (!_allExtensions.TryAdd(id, extensions))
            {
                throw new Exception("");
            }

            _extensions.AddRange(extensions);
            InvalidateCache();
        }
    }

    public void InvalidateCache()
    {
        _cacheInvalidated = true;
    }
}
