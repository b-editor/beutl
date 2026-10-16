using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Beutl.Api.Services;

internal enum PackageApiProblemKind
{
    MissingAssembly,
    MissingType,
    MissingMethod,
    MissingField,
    UnimplementedMethod,
}

// An API that a package references and the host does not offer. Assembly is the host assembly that should have it.
internal sealed record PackageApiProblem(PackageApiProblemKind Kind, string Assembly, string Api);

// Lists the APIs that a package's assemblies reference and the host lacks, read with System.Reflection.Metadata so
// nothing is loaded. A type the package may not access counts as missing; InternalsVisibleTo grants access.
internal sealed partial class PackageApiChecker : IDisposable
{
    private const int MaxForwarderDepth = 32;

    private readonly Dictionary<string, string?> _hostAssemblies;
    private readonly Dictionary<string, HostModule?> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(MetadataReader, TypeReferenceHandle), TypeLookup> _lookups = [];
    private readonly List<PEReader> _peReaders = [];
    private readonly HashSet<PackageApiProblem> _problems = [];
    private readonly SignatureProvider _provider;

    private PackageApiChecker(IReadOnlyDictionary<string, string?> hostAssemblies)
    {
        // The runtime binds assembly names case-insensitively.
        _hostAssemblies = new(hostAssemblies, StringComparer.OrdinalIgnoreCase);
        _provider = new SignatureProvider(this);
    }

    private enum LookupState
    {
        NotHost,
        Found,
        MissingType,
        MissingAssembly,
    }

    // hostAssemblies maps a host assembly's simple name to its file, or to null if missing; other names go unchecked.
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
        AssemblyDefinition assembly = reader.GetAssemblyDefinition();
        string name = reader.GetString(assembly.Name);
        string publicKey = Convert.ToHexString(reader.GetBlobBytes(assembly.PublicKey));
        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            TypeLookup type = Resolve(reader, handle);
            if (type.State == LookupState.MissingAssembly)
            {
                _problems.Add(new(PackageApiProblemKind.MissingAssembly, type.Assembly, type.Assembly));
            }
            else if (type.State == LookupState.MissingType
                     || (type.State == LookupState.Found && !CanAccess(type, name, publicKey)))
            {
                _problems.Add(new(PackageApiProblemKind.MissingType, type.Assembly, type.FullName));
            }
        }

        CheckMembers(reader, name, publicKey);
        CheckOverrides(reader, name, publicKey);
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

    // Signature modifiers may name a library's internal polyfill, such as IsExternalInit; the runtime does not check them.
    private static bool CanAccess(TypeLookup type, string assembly, string publicKey)
    {
        return (type.Definition.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic or TypeAttributes.NestedFamily
                or TypeAttributes.NestedFamORAssem => true,
            TypeAttributes.NotPublic or TypeAttributes.NestedAssembly or TypeAttributes.NestedFamANDAssem
                => type.FullName.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal)
                   || type.Module!.IsFriend(assembly, publicKey),
            _ => false,
        };
    }

    // The assembly names that InternalsVisibleTo grants, each with its public key in hex, or "" if it gives none.
    private static List<(string Name, string PublicKey)> ReadFriends(MetadataReader reader)
    {
        var friends = new List<(string Name, string PublicKey)>();
        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            EntityHandle type = attribute.Constructor.Kind == HandleKind.MemberReference
                ? reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
                : reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
            string? typeName = type.Kind switch
            {
                HandleKind.TypeReference when reader.GetTypeReference((TypeReferenceHandle)type) is var reference
                    => FullName(reader, reference.Namespace, reference.Name),
                HandleKind.TypeDefinition when reader.GetTypeDefinition((TypeDefinitionHandle)type) is var definition
                    => FullName(reader, definition.Namespace, definition.Name),
                _ => null,
            };
            if (typeName != "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
            {
                continue;
            }

            BlobReader value = reader.GetBlobReader(attribute.Value);
            value.ReadUInt16(); // The prolog.
            if (value.ReadSerializedString()?.Split(',', StringSplitOptions.TrimEntries) is [string friend, .. string[] parts])
            {
                const string Key = "PublicKey=";
                string? key = parts.FirstOrDefault(part => part.StartsWith(Key, StringComparison.OrdinalIgnoreCase));
                friends.Add((friend, key?[Key.Length..] ?? ""));
            }
        }

        return friends;
    }

    private static string FullName(MetadataReader reader, StringHandle @namespace, StringHandle name)
    {
        return @namespace.IsNil || reader.GetString(@namespace).Length == 0
            ? reader.GetString(name)
            : $"{reader.GetString(@namespace)}.{reader.GetString(name)}";
    }

    private sealed class HostModule(string name, MetadataReader reader)
    {
        private List<(string Name, string PublicKey)>? _friends;

        public string Name { get; } = name;

        public MetadataReader Reader { get; } = reader;

        // Top-level type definitions, and the assembly each type forwarder names, by full type name.
        public Dictionary<string, TypeDefinitionHandle> Types { get; } = [];

        public Dictionary<string, string> Forwarders { get; } = [];

        // A grant that gives no public key matches the name alone.
        public bool IsFriend(string assembly, string publicKey)
        {
            _friends ??= ReadFriends(Reader);
            return _friends.Exists(friend => StringComparer.OrdinalIgnoreCase.Equals(friend.Name, assembly)
                && (friend.PublicKey.Length == 0 || StringComparer.OrdinalIgnoreCase.Equals(friend.PublicKey, publicKey)));
        }
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
