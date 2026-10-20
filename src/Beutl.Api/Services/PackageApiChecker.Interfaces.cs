using System.Reflection;
using System.Reflection.Metadata;

namespace Beutl.Api.Services;

// A package type must implement every abstract method of the host interfaces that it or its base types implement,
// or the runtime refuses to load the type.
internal sealed partial class PackageApiChecker
{
    private void CheckInterfaces(List<ChainType> types)
    {
        List<ChainType> interfaces = Interfaces(types);
        HashSet<MethodTarget> explicitImplementations = ExplicitTargets(types.Concat(interfaces));
        foreach (ChainType @interface in interfaces)
        {
            if (@interface.Module is not { } module)
            {
                continue;
            }

            MetadataReader reader = @interface.Reader;
            foreach (MethodDefinitionHandle handle in @interface.Definition.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                string name = reader.GetString(method.Name);
                // A method with a default implementation needs none from the package.
                if ((method.Attributes & MethodAttributes.Abstract) == 0
                    || explicitImplementations.Contains(new(reader, @interface.Handle, name, Key(method.DecodeSignature(_provider, null)))))
                {
                    continue;
                }

                MethodSignature<SigType> signature = method.DecodeSignature(_provider, @interface.Context);
                string key = Key(signature);
                // An implicit implementation is a public method with the same name and signature, virtual unless static.
                if (!types.Exists(type => FindMethod(type, name, key) is { } candidate
                        && (candidate.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public
                        && (candidate.Attributes & (MethodAttributes.Virtual | MethodAttributes.Static)) != 0))
                {
                    _problems.Add(new(
                        PackageApiProblemKind.UnimplementedMethod,
                        module.Name,
                        $"{FullName(reader, @interface.Handle)}.{name}{Display(signature)}"));
                }
            }
        }
    }

    // The interfaces that the types implement and the ones that those extend, each with its generic arguments.
    private List<ChainType> Interfaces(List<ChainType> types)
    {
        var interfaces = new List<ChainType>();
        var seen = new HashSet<(MetadataReader, TypeDefinitionHandle, string)>();
        var pending = new Queue<ChainType>(types);
        while (pending.TryDequeue(out ChainType type))
        {
            foreach (InterfaceImplementationHandle handle in type.Definition.GetInterfaceImplementations())
            {
                if (Named(type, type.Reader.GetInterfaceImplementation(handle).Interface) is { } @interface
                    && seen.Add((@interface.Reader, @interface.Handle, string.Join(",", @interface.Context?.Select(argument => argument.Key) ?? []))))
                {
                    interfaces.Add(@interface);
                    pending.Enqueue(@interface);
                }
            }
        }

        return interfaces;
    }
}
