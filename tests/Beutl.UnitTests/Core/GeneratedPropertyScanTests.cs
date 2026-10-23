using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Validation;

namespace Beutl.UnitTests.Core;

// Each generated ScanPropertiesCore registers the names, order, attributes and validators the reflection scan in
// CoreObject would, for every type of the assemblies that reference the generator.
[TestFixture]
public class GeneratedPropertyScanTests
{
    private static readonly Assembly[] s_assemblies =
    [
        typeof(CoreObject).Assembly,
        typeof(EditorConfig).Assembly,
        typeof(EngineObject).Assembly,
        typeof(Element).Assembly,
        typeof(GraphModel).Assembly,
    ];

    private static readonly Type[] s_typeArguments = [typeof(float), typeof(object), typeof(string)];

    private static readonly MethodInfo s_scanPropertiesCore = typeof(CoreObject)
        .GetMethod("ScanPropertiesCore", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly Lazy<Type[]> s_concreteTypes = new(() =>
    [
        .. s_assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && typeof(CoreObject).IsAssignableFrom(type))
            .SelectMany(Close)
            .Where(type => type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) != null),
    ]);

    private static IEnumerable<TestCaseData> GeneratedTypes()
        => s_assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type != typeof(CoreObject) && typeof(CoreObject).IsAssignableFrom(type)
                && type.GetMethod("ScanPropertiesCore", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new TestCaseData(type).SetArgDisplayNames(type.FullName!));

    [Test]
    public void EveryCoreObjectWithPropertiesOfTheseAssemblies_HasAGeneratedScan()
    {
        // A type without the generated scan falls back to reflection; only the nested ones are left there.
        Type[] missing = s_assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(CoreObject).IsAssignableFrom(type) && type != typeof(CoreObject) && !type.IsNested)
            .Where(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(property => typeof(IProperty).IsAssignableFrom(property.PropertyType)))
            .Where(type => type.GetMethod("ScanPropertiesCore", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null)
            .Where(type => !IsSuppressed(type))
            .ToArray();

        Assert.That(missing, Is.Empty);
    }

    [TestCaseSource(nameof(GeneratedTypes))]
    public void TheGeneratedScan_RegistersWhatReflectionWould(Type declaringType)
    {
        Type concrete = s_concreteTypes.Value.FirstOrDefault(type => DeclaringTypeIn(type, declaringType) != null)
            ?? throw new AssertionException($"No concrete type with a parameterless constructor has {declaringType} as a base.");
        Type closed = DeclaringTypeIn(concrete, declaringType)!;

        string[] generated = Describe(Scan((CoreObject)Activator.CreateInstance(concrete, nonPublic: true)!, closed, generated: true));
        string[] reflected = Describe(Scan((CoreObject)Activator.CreateInstance(concrete, nonPublic: true)!, closed, generated: false));

        Assert.That(generated, Is.EqualTo(reflected));
    }

    // An engine type opted out with the attribute, on itself or a base, keeps the reflection scan.
    private static bool IsSuppressed(Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            if (current.GetCustomAttribute<SuppressResourceClassGenerationAttribute>(inherit: false) != null)
                return true;
        }

        return false;
    }

    // The type itself, or each generic argument tried in turn, so that KeyFrame<T> is checked as KeyFrame<float>
    // and FactoryNode<T> as a FactoryNode of the first type that meets its constraints.
    private static IEnumerable<Type> Close(Type type)
    {
        if (!type.IsGenericTypeDefinition)
            return [type];

        IEnumerable<Type> constrained = type.GetGenericArguments()[0].GetGenericParameterConstraints()
            .Where(constraint => constraint.IsClass)
            .SelectMany(constraint => s_assemblies.SelectMany(assembly => assembly.GetTypes())
                .Where(candidate => !candidate.IsAbstract && !candidate.IsGenericTypeDefinition && constraint.IsAssignableFrom(candidate)
                    && candidate.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(candidate => candidate.FullName, StringComparer.Ordinal)
                .Take(1));

        var closed = new List<Type>();
        foreach (Type argument in s_typeArguments.Concat(constrained))
        {
            try
            {
                closed.Add(type.MakeGenericType(Enumerable.Repeat(argument, type.GetGenericArguments().Length).ToArray()));
                break;
            }
            catch (ArgumentException)
            {
            }
        }

        return closed;
    }

    private static Type? DeclaringTypeIn(Type concrete, Type declaringType)
    {
        for (Type? type = concrete; type != null; type = type.BaseType)
        {
            if (type == declaringType || (type.IsGenericType && type.GetGenericTypeDefinition() == declaringType))
                return type;
        }

        return null;
    }

    // The generated override is reached through the virtual call; the reflection scan through a call to CoreObject's own body.
    private static IProperty[] Scan(CoreObject owner, Type declaringType, bool generated)
    {
        MethodInfo method = s_scanPropertiesCore.MakeGenericMethod(declaringType);
        if (generated)
            return [.. (IEnumerable<IProperty>)method.Invoke(owner, null)!];

        var body = new DynamicMethod("ScanByReflection", typeof(IEnumerable<IProperty>), [typeof(CoreObject)], typeof(CoreObject), skipVisibility: true);
        ILGenerator il = body.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return [.. ((Func<CoreObject, IEnumerable<IProperty>>)body.CreateDelegate(typeof(Func<CoreObject, IEnumerable<IProperty>>)))(owner)];
    }

    // The compiler's own attributes, like NullableAttribute, are on the member but not in its source.
    private static string[] Describe(IProperty[] properties)
        => [.. properties.Select(property =>
            $"{property.Name} | {string.Join(", ", (property.GetAttributes() ?? [])
                .Where(attribute => attribute.GetType().Namespace != "System.Runtime.CompilerServices")
                .Select(DescribeObject)
                .Order(StringComparer.Ordinal))} | {DescribeObject(property.GetValidator())}")];

    private static string DescribeObject(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return text;
            case Type type:
                return type.FullName ?? type.Name;
            case IEnumerable items:
                return $"[{string.Join(", ", items.Cast<object?>().Select(DescribeObject))}]";
            case Attribute or IValidator:
                var sb = new StringBuilder(value.GetType().FullName);
                foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property => property.GetIndexParameters().Length == 0 && property.Name != nameof(Attribute.TypeId))
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    string described;
                    try
                    {
                        described = DescribeObject(property.GetValue(value));
                    }
                    catch (TargetInvocationException)
                    {
                        described = "unset";
                    }

                    sb.Append(' ').Append(property.Name).Append('=').Append(described);
                }

                return sb.ToString();
            default:
                return value.ToString() ?? "";
        }
    }
}
