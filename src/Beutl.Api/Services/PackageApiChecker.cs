using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Beutl.Api.Services;

internal enum PackageApiProblemKind
{
    MissingAssembly,
    MissingType,
}

// An API that a package references and the host does not offer. Assembly is the host assembly that should have it.
internal sealed record PackageApiProblem(PackageApiProblemKind Kind, string Assembly, string Api);

// Lists the APIs that a package's assemblies reference and the host lacks. Both sides are read with
// System.Reflection.Metadata, so nothing is loaded. A referenced type must exist, directly or through a type
// forwarder, and be public or protected, because the package cannot use the others.
internal sealed class PackageApiChecker : IDisposable
{
    private const int MaxForwarderDepth = 32;

    private readonly IReadOnlyDictionary<string, string?> _hostAssemblies;
    private readonly Dictionary<string, HostModule?> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(MetadataReader, TypeReferenceHandle), TypeLookup> _lookups = [];
    private readonly List<PEReader> _peReaders = [];
    private readonly HashSet<PackageApiProblem> _problems = [];

    private PackageApiChecker(IReadOnlyDictionary<string, string?> hostAssemblies)
    {
        _hostAssemblies = hostAssemblies;
    }

    private enum LookupState
    {
        NotHost,
        Found,
        MissingType,
        MissingAssembly,
    }

    // packageAssemblies are the files the package loads. hostAssemblies maps the simple name of each assembly that a
    // reference binds to in the host to its file, or to null when the host should provide it but has none. A reference
    // to any other assembly binds to the package or its dependencies, and is not checked.
    public static IReadOnlyList<PackageApiProblem> Check(
        IEnumerable<string> packageAssemblies,
        IReadOnlyDictionary<string, string?> hostAssemblies)
    {
        using var checker = new PackageApiChecker(hostAssemblies);
        foreach (string path in packageAssemblies)
        {
            if (checker.Open(path) is { IsAssembly: true } reader)
            {
                checker.CheckReferences(reader);
            }
        }

        return
        [
            .. checker._problems
                .OrderBy(problem => problem.Assembly, StringComparer.Ordinal)
                .ThenBy(problem => problem.Kind)
                .ThenBy(problem => problem.Api, StringComparer.Ordinal)
        ];
    }

    public void Dispose()
    {
        foreach (PEReader peReader in _peReaders)
        {
            peReader.Dispose();
        }
    }

    private MetadataReader? Open(string path)
    {
        var peReader = new PEReader(File.OpenRead(path));
        _peReaders.Add(peReader);
        return peReader.HasMetadata ? peReader.GetMetadataReader() : null;
    }

    private void CheckReferences(MetadataReader reader)
    {
        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            TypeLookup type = Resolve(reader, handle);
            if (type.State == LookupState.MissingAssembly)
            {
                _problems.Add(new(PackageApiProblemKind.MissingAssembly, type.Assembly, type.Assembly));
            }
            else if (type.State == LookupState.MissingType
                     || (type.State == LookupState.Found && !IsVisible(type.Definition.Attributes)))
            {
                _problems.Add(new(PackageApiProblemKind.MissingType, type.Assembly, type.FullName));
            }
        }
    }

    private TypeLookup Resolve(MetadataReader reader, TypeReferenceHandle handle)
    {
        if (_lookups.TryGetValue((reader, handle), out TypeLookup cached))
        {
            return cached;
        }

        TypeReference reference = reader.GetTypeReference(handle);
        EntityHandle scope = reference.ResolutionScope;
        TypeLookup lookup;
        if (scope.Kind == HandleKind.TypeReference)
        {
            TypeLookup outer = Resolve(reader, (TypeReferenceHandle)scope);
            string name = reader.GetString(reference.Name);
            lookup = outer with { FullName = $"{outer.FullName}+{name}" };
            if (outer.State == LookupState.Found)
            {
                MetadataReader hostReader = outer.Module!.Reader;
                TypeDefinitionHandle nested = outer.Definition.GetNestedTypes()
                    .FirstOrDefault(candidate => hostReader.StringComparer.Equals(hostReader.GetTypeDefinition(candidate).Name, name));
                lookup = nested.IsNil ? lookup with { State = LookupState.MissingType } : lookup with { Handle = nested };
            }
        }
        else
        {
            string fullName = FullName(reader, reference.Namespace, reference.Name);
            lookup = scope.Kind switch
            {
                HandleKind.AssemblyReference => Find(
                    reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name), fullName, 0),
                HandleKind.ModuleDefinition => Find(reader.GetString(reader.GetAssemblyDefinition().Name), fullName, 0),
                _ => new(LookupState.NotHost, "", fullName),
            };
        }

        _lookups[(reader, handle)] = lookup;
        return lookup;
    }

    private TypeLookup Find(string assembly, string fullName, int depth)
    {
        if (!_hostAssemblies.ContainsKey(assembly))
        {
            return new(LookupState.NotHost, assembly, fullName);
        }

        if (GetModule(assembly) is not { } module)
        {
            return new(LookupState.MissingAssembly, assembly, fullName);
        }

        if (module.Types.TryGetValue(fullName, out TypeDefinitionHandle handle))
        {
            return new(LookupState.Found, module.Name, fullName, module, handle);
        }

        return depth < MaxForwarderDepth && module.Forwarders.TryGetValue(fullName, out string? target)
            ? Find(target, fullName, depth + 1)
            : new(LookupState.MissingType, module.Name, fullName);
    }

    private HostModule? GetModule(string assembly)
    {
        if (_modules.TryGetValue(assembly, out HostModule? module))
        {
            return module;
        }

        if (_hostAssemblies[assembly] is { } path && File.Exists(path) && Open(path) is { IsAssembly: true } reader)
        {
            module = new HostModule(reader.GetString(reader.GetAssemblyDefinition().Name), reader);
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                if (type.GetDeclaringType().IsNil)
                {
                    module.Types[FullName(reader, type.Namespace, type.Name)] = handle;
                }
            }

            foreach (ExportedTypeHandle handle in reader.ExportedTypes)
            {
                ExportedType type = reader.GetExportedType(handle);
                if (type.IsForwarder && type.Implementation.Kind == HandleKind.AssemblyReference)
                {
                    AssemblyReference target = reader.GetAssemblyReference((AssemblyReferenceHandle)type.Implementation);
                    module.Forwarders[FullName(reader, type.Namespace, type.Name)] = reader.GetString(target.Name);
                }
            }
        }

        _modules[assembly] = module;
        return module;
    }

    private static bool IsVisible(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask)
            is TypeAttributes.Public or TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem;
    }

    private static string FullName(MetadataReader reader, StringHandle @namespace, StringHandle name)
    {
        return @namespace.IsNil || reader.GetString(@namespace).Length == 0
            ? reader.GetString(name)
            : $"{reader.GetString(@namespace)}.{reader.GetString(name)}";
    }

    private sealed class HostModule(string name, MetadataReader reader)
    {
        public string Name { get; } = name;

        public MetadataReader Reader { get; } = reader;

        // Top-level type definitions, and the assembly each type forwarder names, by full type name.
        public Dictionary<string, TypeDefinitionHandle> Types { get; } = [];

        public Dictionary<string, string> Forwarders { get; } = [];
    }

    // Where a type reference binds. Module and Handle are set when it binds to a host type definition.
    private readonly record struct TypeLookup(
        LookupState State,
        string Assembly,
        string FullName,
        HostModule? Module = null,
        TypeDefinitionHandle Handle = default)
    {
        public TypeDefinition Definition => Module!.Reader.GetTypeDefinition(Handle);
    }
}
