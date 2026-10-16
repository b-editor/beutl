using System.Reflection;
using System.Reflection.Metadata;

namespace Beutl.Api.Services;

// A package type must override base methods that still exist and implement every abstract method of its base types.
// The runtime reports neither for a type that GetExportedTypes skips, and an override that lost its base fails silently.
internal sealed partial class PackageApiChecker
{
    private void CheckOverrides(MetadataReader reader, string assembly, string publicKey)
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            var self = new ChainType(reader, handle, null, null);
            TypeDefinition type = self.Definition;
            (List<ChainType> bases, bool complete) = BaseTypes(self);
            if ((type.Attributes & TypeAttributes.Interface) != 0 || !bases.Exists(candidate => candidate.Module != null))
            {
                continue;
            }

            ChainType host = bases.Find(candidate => candidate.Module != null);
            if ((host.Definition.Attributes & TypeAttributes.Sealed) != 0)
            {
                _problems.Add(new(PackageApiProblemKind.SealedBaseType, host.Module!.Name, FullName(host.Reader, host.Handle)));
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                // An override is a virtual method that reuses a base slot instead of starting a new one.
                if ((method.Attributes & (MethodAttributes.Virtual | MethodAttributes.NewSlot)) != MethodAttributes.Virtual)
                {
                    continue;
                }

                string name = reader.GetString(method.Name);
                MethodSignature<SigType> signature = method.DecodeSignature(_provider, null);
                if (!HasOverridableBase(bases, complete, name, Key(signature), assembly, publicKey))
                {
                    _problems.Add(new(
                        PackageApiProblemKind.MissingMethod,
                        host.Module!.Name,
                        $"{FullName(host.Reader, host.Handle)}.{name}{Display(signature)}"));
                }
            }

            if ((type.Attributes & TypeAttributes.Abstract) == 0)
            {
                CheckAbstractMethods([self, .. bases]);
                // Base types outside the package and the host may implement interface methods that cannot be seen.
                if (complete)
                {
                    CheckInterfaces([self, .. bases]);
                }
            }
        }
    }

    private bool HasOverridableBase(List<ChainType> bases, bool complete, string name, string key, string assembly, string publicKey)
    {
        foreach (ChainType candidate in bases)
        {
            if (FindMethod(candidate, name, key) is { } method)
            {
                return (method.Attributes & (MethodAttributes.Virtual | MethodAttributes.Final)) == MethodAttributes.Virtual
                       && (candidate.Module is null
                           || CanAccessMember(method.Attributes & MethodAttributes.MemberAccessMask, candidate.Module, assembly, publicKey));
            }
        }

        return !complete;
    }

    // Walks from the package type up, so an abstract host method counts as implemented once a nearer type overrides it.
    private void CheckAbstractMethods(List<ChainType> types)
    {
        HashSet<MethodTarget> explicitOverrides = ExplicitTargets(types);
        var overridden = new HashSet<(string Name, string Key)>();
        foreach (ChainType type in types)
        {
            MetadataReader reader = type.Reader;
            foreach (MethodDefinitionHandle handle in type.Definition.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.Virtual) == 0)
                {
                    continue;
                }

                string name = reader.GetString(method.Name);
                MethodSignature<SigType> signature = method.DecodeSignature(_provider, type.Context);
                if ((method.Attributes & MethodAttributes.Abstract) == 0)
                {
                    overridden.Add((name, Key(signature)));
                }
                else if (type.Module != null
                         && !overridden.Contains((name, Key(signature)))
                         && !explicitOverrides.Contains(new(reader, type.Handle, name, Key(method.DecodeSignature(_provider, null)))))
                {
                    _problems.Add(new(
                        PackageApiProblemKind.UnimplementedMethod,
                        type.Module.Name,
                        $"{FullName(reader, type.Handle)}.{name}{Display(signature)}"));
                }
            }
        }
    }

    // The methods that the explicit overrides and implementations of these types name.
    private HashSet<MethodTarget> ExplicitTargets(IEnumerable<ChainType> types)
    {
        var targets = new HashSet<MethodTarget>();
        foreach (ChainType type in types)
        {
            foreach (MethodImplementationHandle handle in type.Definition.GetMethodImplementations())
            {
                if (Target(type.Reader, type.Reader.GetMethodImplementation(handle).MethodDeclaration) is { } target)
                {
                    targets.Add(target);
                }
            }
        }

        return targets;
    }

    // An explicit override names its base method; generic arguments are left out, so it compares to the definition.
    private MethodTarget? Target(MetadataReader reader, EntityHandle declaration)
    {
        if (declaration.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)declaration);
            return new(reader, method.GetDeclaringType(), reader.GetString(method.Name), Key(method.DecodeSignature(_provider, null)));
        }

        if (declaration.Kind != HandleKind.MemberReference)
        {
            return null;
        }

        MemberReference member = reader.GetMemberReference((MemberReferenceHandle)declaration);
        EntityHandle parent = member.Parent.Kind == HandleKind.TypeSpecification
            ? Instantiation(reader, (TypeSpecificationHandle)member.Parent, null).Type
            : member.Parent;
        (MetadataReader Reader, TypeDefinitionHandle Handle)? type = parent.Kind switch
        {
            HandleKind.TypeDefinition => (reader, (TypeDefinitionHandle)parent),
            HandleKind.TypeReference when Resolve(reader, (TypeReferenceHandle)parent) is { State: LookupState.Found or LookupState.Package } found
                => (found.Module!.Reader, found.Handle),
            _ => null,
        };
        return type is { } owner
            ? new(owner.Reader, owner.Handle, reader.GetString(member.Name), Key(member.DecodeMethodSignature(_provider, null)))
            : null;
    }

    // A method by its declaring type definition, name and signature key.
    private readonly record struct MethodTarget(MetadataReader Reader, TypeDefinitionHandle Type, string Name, string Key);
}
