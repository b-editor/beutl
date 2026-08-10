namespace Beutl.Engine;

/// <summary>
/// Marks the parameterless static factory that supplies the owner whose declared property defaults initialize a
/// generated detached <see cref="EngineObject.Resource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use this extension point when the owner uses a primary constructor, initializes a generated
/// <see cref="IProperty"/> from an ordinary constructor, or otherwise cannot expose its defaults through the
/// generator's declaration-time storage rules.
/// </para>
/// <para>
/// Exactly one method on the declaring owner may carry this attribute. The method may be non-public, but it must
/// be static, parameterless, non-generic, and return the declaring owner type. It must return a non-null owner
/// whose generated properties expose the intended detached defaults. The generated public resource constructor
/// invokes the method once for that construction; the attached <see cref="EngineObject.ToResource"/> path does
/// not invoke it.
/// </para>
/// <para>
/// A generated owner derived from a provider-backed owner declares its own provider that constructs the
/// most-derived type. The direct concrete generated resource constructor invokes that provider once and passes
/// the returned owner through the complete base-resource constructor chain; base providers are not invoked
/// separately. Providers are not inherited as defaults factories because doing so would omit the derived owner's
/// defaults and could bypass the base owner's explicit construction contract.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ResourceDefaultValuesProviderAttribute : Attribute;
