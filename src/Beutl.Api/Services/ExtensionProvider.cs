using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Extensibility;

using static Beutl.Configuration.ExtensionConfig;

namespace Beutl.Api.Services;

public sealed class ExtensionProvider : IExtensionRegistry
{
    private readonly Dictionary<int, Extension[]> _allExtensions = [];
    private readonly ExtensionConfig _config = GlobalConfiguration.Instance.ExtensionConfig;
    private readonly Dictionary<Type, Array> _cache = [];
    private readonly CoreList<Extension> _extensions = [];
    private readonly object _lock = new();
    private readonly object _mutationGate = new();
    private Extension[] _snapshot = [];

    public ExtensionProvider()
    {
    }

    public ICoreReadOnlyList<Extension> AllExtensions => _extensions;

    public TExtension[] GetExtensions<TExtension>()
        where TExtension : Extension
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(typeof(TExtension), out Array? result))
            {
                return (TExtension[])result;
            }
            else
            {
                TExtension[] exts = _snapshot.OfType<TExtension>().ToArray();
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
                foreach (Extension extension in _snapshot)
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

            foreach (Extension extension in _snapshot)
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
            foreach (Extension extension in _snapshot)
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
        ProjectItemExtension[] result;
        lock (_lock)
        {
            result = _snapshot
                .OfType<ProjectItemExtension>()
                .Where(extension => extension.IsSupported(file))
                .ToArray();
        }

        return result;
    }

    public void AddExtensions(int packageId, IReadOnlyList<Extension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        Extension[] ownedExtensions = extensions.ToArray();
        if (ownedExtensions.Any(extension => extension is null))
            throw new ArgumentException("Extensions cannot contain null.", nameof(extensions));

        lock (_mutationGate)
        {
            lock (_lock)
            {
                if (!_allExtensions.TryAdd(packageId, ownedExtensions))
                {
                    throw new InvalidOperationException(
                        $"Extensions for package (id: {packageId}) are already registered.");
                }

                _snapshot = [.. _snapshot, .. ownedExtensions];
                _cache.Clear();
            }

            // Collection observers may perform package-lifetime cleanup. Run them without the
            // provider lock so an in-flight extension operation can still query the provider.
            _extensions.AddRange(ownedExtensions);
        }
    }

    public IReadOnlyList<Extension> RemoveExtensions(int packageId)
    {
        lock (_mutationGate)
        {
            Extension[] extensions;
            lock (_lock)
            {
                if (!_allExtensions.Remove(packageId, out Extension[]? removed))
                    return Array.Empty<Extension>();

                extensions = removed;
                var removedSet = new HashSet<Extension>(extensions, ReferenceEqualityComparer.Instance);
                _snapshot = _snapshot.Where(extension => !removedSet.Contains(extension)).ToArray();
                _cache.Clear();
            }

            // Caption and other dynamic catalogs retire their package-owned registrations from
            // this synchronous notification before PackageManager invokes Extension.Unload().
            _extensions.RemoveAll(extensions);
            return extensions;
        }
    }
}
