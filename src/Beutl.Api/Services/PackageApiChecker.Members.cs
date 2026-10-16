using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Beutl.Api.Services;

// A referenced method or field must exist on its type or a base type, as the runtime binds it, with the signature that
// the package was compiled against, custom modifiers included.
internal sealed partial class PackageApiChecker
{
    private const int MaxBaseDepth = 32;

    private void CheckMembers(MetadataReader reader, string assembly, string publicKey)
    {
        foreach (MemberReferenceHandle handle in reader.MemberReferences)
        {
            MemberReference member = reader.GetMemberReference(handle);
            TypeLookup parent = member.Parent.Kind switch
            {
                HandleKind.TypeReference => Resolve(reader, (TypeReferenceHandle)member.Parent),
                HandleKind.TypeSpecification => GenericDefinition(null, reader, (TypeSpecificationHandle)member.Parent, null).Type,
                _ => default,
            };
            if (parent.State != LookupState.Found)
            {
                continue;
            }

            string name = reader.GetString(member.Name);
            if (member.GetKind() == MemberReferenceKind.Method)
            {
                MethodSignature<SigType> signature = member.DecodeMethodSignature(_provider, null);
                // A vararg call site lists its extra arguments, so it never matches the definition.
                if (signature.Header.CallingConvention != SignatureCallingConvention.VarArgs
                    && !HasMember(parent, name, Key(signature), isMethod: true, assembly, publicKey))
                {
                    _problems.Add(new(PackageApiProblemKind.MissingMethod, parent.Assembly, $"{parent.FullName}.{name}{Display(signature)}"));
                }
            }
            else if (!HasMember(parent, name, member.DecodeFieldSignature(_provider, null).Key, isMethod: false, assembly, publicKey))
            {
                _problems.Add(new(PackageApiProblemKind.MissingField, parent.Assembly, $"{parent.FullName}.{name}"));
            }
        }
    }

    // Searches the type and then its base types, as the runtime does when it binds a member reference.
    private bool HasMember(TypeLookup type, string name, string key, bool isMethod, string assembly, string publicKey)
    {
        SigType[]? context = null;
        for (int depth = 0; depth < MaxBaseDepth && type.State == LookupState.Found; depth++)
        {
            HostModule module = type.Module!;
            TypeDefinition definition = type.Definition;
            if (FindMember(module.Reader, definition, name, key, isMethod, context) is { } access)
            {
                return access switch
                {
                    MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem => true,
                    MethodAttributes.Assembly or MethodAttributes.FamANDAssem => module.IsFriend(assembly, publicKey),
                    _ => false,
                };
            }

            // Constructors are not inherited, so only the referenced type can declare one.
            if (definition.BaseType.IsNil || name is ".ctor" or ".cctor")
            {
                return false;
            }

            // A generic base type's signatures use its own type parameters, so they are decoded with its arguments.
            (type, context) = definition.BaseType.Kind == HandleKind.TypeSpecification
                ? GenericDefinition(module, module.Reader, (TypeSpecificationHandle)definition.BaseType, context)
                : (Lookup(module, module.Reader, definition.BaseType), null);
        }

        // The base types lead out of the host, where the member cannot be checked.
        return true;
    }

    // The access of the member that the type declares with this name and signature, or null if there is none.
    private MethodAttributes? FindMember(
        MetadataReader reader, TypeDefinition type, string name, string key, bool isMethod, SigType[]? context)
    {
        if (isMethod)
        {
            foreach (MethodDefinitionHandle handle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                if (reader.StringComparer.Equals(method.Name, name) && Key(method.DecodeSignature(_provider, context)) == key)
                {
                    return method.Attributes & MethodAttributes.MemberAccessMask;
                }
            }
        }
        else
        {
            foreach (FieldDefinitionHandle handle in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(handle);
                if (reader.StringComparer.Equals(field.Name, name) && field.DecodeSignature(_provider, context).Key == key)
                {
                    return (MethodAttributes)(field.Attributes & FieldAttributes.FieldAccessMask);
                }
            }
        }

        return null;
    }

    // The generic type that an instantiation names, with its type arguments decoded in context.
    private (TypeLookup Type, SigType[]? Arguments) GenericDefinition(
        HostModule? module, MetadataReader reader, TypeSpecificationHandle handle, SigType[]? context)
    {
        BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
        {
            return default;
        }

        blob.ReadSignatureTypeCode();
        EntityHandle genericType = blob.ReadTypeHandle();
        var decoder = new SignatureDecoder<SigType, SigType[]?>(_provider, reader, context);
        var arguments = new SigType[blob.ReadCompressedInteger()];
        for (int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = decoder.DecodeType(ref blob);
        }

        return (Lookup(module, reader, genericType), arguments);
    }

    private TypeLookup Lookup(HostModule? module, MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeReference => Resolve(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeDefinition when module != null => new(
                LookupState.Found, module.Name, FullName(reader, (TypeDefinitionHandle)handle), module, (TypeDefinitionHandle)handle),
            _ => default,
        };
    }

    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeDefinitionHandle declaringType = type.GetDeclaringType();
        return declaringType.IsNil
            ? FullName(reader, type.Namespace, type.Name)
            : $"{FullName(reader, declaringType)}+{reader.GetString(type.Name)}";
    }

    private static string Key(MethodSignature<SigType> signature)
    {
        return $"{signature.Header.RawValue}`{signature.GenericParameterCount} {signature.ReturnType.Key}"
               + $"({string.Join(",", signature.ParameterTypes.Select(type => type.Key))})";
    }

    private static string Display(MethodSignature<SigType> signature)
    {
        string arity = signature.GenericParameterCount > 0 ? $"<{new string(',', signature.GenericParameterCount - 1)}>" : "";
        return $"{arity}({string.Join(", ", signature.ParameterTypes.Select(type => type.Display))})";
    }

    // Key names a type by the assembly that defines it, after type forwarders, so that both sides compare equal.
    private readonly record struct SigType(string Key, string Display);

    private sealed class SignatureProvider(PackageApiChecker checker) : ISignatureTypeProvider<SigType, SigType[]?>
    {
        public SigType GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return new(typeCode.ToString(), typeCode switch
            {
                PrimitiveTypeCode.Boolean => "bool",
                PrimitiveTypeCode.Int32 => "int",
                PrimitiveTypeCode.Int64 => "long",
                PrimitiveTypeCode.Single => "float",
                PrimitiveTypeCode.Double => "double",
                PrimitiveTypeCode.Object => "object",
                PrimitiveTypeCode.String => "string",
                PrimitiveTypeCode.Void => "void",
                _ => $"System.{typeCode}",
            });
        }

        public SigType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            return Named(rawTypeKind, reader.GetString(reader.GetAssemblyDefinition().Name), FullName(reader, handle));
        }

        public SigType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            // Outside the host the assembly is only the name each side was compiled against, which may differ.
            TypeLookup type = checker.Resolve(reader, handle);
            return Named(rawTypeKind, type.State == LookupState.NotHost ? null : type.Assembly, type.FullName);
        }

        public SigType GetTypeFromSpecification(
            MetadataReader reader, SigType[]? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        public SigType GetSZArrayType(SigType elementType) => new($"{elementType.Key}[]", $"{elementType.Display}[]");

        public SigType GetArrayType(SigType elementType, ArrayShape shape)
        {
            return new($"{elementType.Key}[{shape.Rank}]", $"{elementType.Display}[{new string(',', shape.Rank - 1)}]");
        }

        public SigType GetByReferenceType(SigType elementType) => new($"{elementType.Key}&", $"ref {elementType.Display}");

        public SigType GetPointerType(SigType elementType) => new($"{elementType.Key}*", $"{elementType.Display}*");

        public SigType GetPinnedType(SigType elementType) => elementType;

        public SigType GetGenericInstantiation(SigType genericType, ImmutableArray<SigType> typeArguments)
        {
            return new(
                $"{genericType.Key}<{string.Join(",", typeArguments.Select(type => type.Key))}>",
                $"{genericType.Display}<{string.Join(", ", typeArguments.Select(type => type.Display))}>");
        }

        public SigType GetGenericTypeParameter(SigType[]? genericContext, int index)
        {
            return genericContext is { } arguments && index < arguments.Length ? arguments[index] : new($"!{index}", $"!{index}");
        }

        public SigType GetGenericMethodParameter(SigType[]? genericContext, int index) => new($"!!{index}", $"!!{index}");

        public SigType GetFunctionPointerType(MethodSignature<SigType> signature)
        {
            return new($"method {Key(signature)}", $"delegate*{Display(signature)}");
        }

        // The runtime compares custom modifiers too, so an `in` parameter or an `init` accessor has another signature.
        public SigType GetModifiedType(SigType modifier, SigType unmodifiedType, bool isRequired)
        {
            return new($"{unmodifiedType.Key} {(isRequired ? "modreq" : "modopt")}({modifier.Key})", unmodifiedType.Display);
        }

        private static SigType Named(byte rawTypeKind, string? assembly, string fullName)
        {
            string kind = rawTypeKind == (byte)SignatureTypeKind.ValueType ? "valuetype " : "";
            return new($"{kind}{(assembly is null ? "" : $"[{assembly}]")}{fullName}", fullName);
        }
    }
}
