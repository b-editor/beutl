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
                CheckAbstractMethods(self, bases);
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
    private void CheckAbstractMethods(ChainType self, List<ChainType> bases)
    {
        var overridden = new HashSet<(string Name, string Key)>();
        foreach (ChainType type in bases.Prepend(self))
        {
            MetadataReader reader = type.Reader;
            foreach (MethodDefinitionHandle handle in type.Definition.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.Virtual) == 0)
                {
                    continue;
                }

                MethodSignature<SigType> signature = method.DecodeSignature(_provider, type.Context);
                (string Name, string Key) id = (reader.GetString(method.Name), Key(signature));
                if ((method.Attributes & MethodAttributes.Abstract) == 0)
                {
                    overridden.Add(id);
                }
                else if (type.Module != null && !overridden.Contains(id))
                {
                    _problems.Add(new(
                        PackageApiProblemKind.UnimplementedMethod,
                        type.Module.Name,
                        $"{FullName(reader, type.Handle)}.{id.Name}{Display(signature)}"));
                }
            }

            // An explicit override, such as one with a covariant return type, names the base method it replaces.
            foreach (MethodImplementationHandle handle in type.Definition.GetMethodImplementations())
            {
                if (Declaration(type, bases, reader.GetMethodImplementation(handle).MethodDeclaration) is { } id)
                {
                    overridden.Add(id);
                }
            }
        }
    }

    // The name and signature key of the base method that an explicit override names, in the base type's context.
    private (string Name, string Key)? Declaration(ChainType owner, List<ChainType> bases, EntityHandle declaration)
    {
        MetadataReader reader = owner.Reader;
        if (declaration.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)declaration);
            ChainType declaringType = bases.Find(candidate => candidate.Reader == reader && candidate.Handle == method.GetDeclaringType());
            return (reader.GetString(method.Name), Key(method.DecodeSignature(_provider, declaringType.Context)));
        }

        if (declaration.Kind != HandleKind.MemberReference)
        {
            return null;
        }

        MemberReference member = reader.GetMemberReference((MemberReferenceHandle)declaration);
        SigType[]? context = member.Parent.Kind == HandleKind.TypeSpecification
            ? Instantiation(reader, (TypeSpecificationHandle)member.Parent, owner.Context).Arguments
            : null;
        return (reader.GetString(member.Name), Key(member.DecodeMethodSignature(_provider, context)));
    }
}
