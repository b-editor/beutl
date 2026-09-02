using System.Collections.ObjectModel;
using System.Numerics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Shaders;

/// <summary>Declares uniform and child-shader bindings while a <see cref="ShaderDescription"/> is created.</summary>
/// <remarks>
/// The description invokes its builder callback synchronously and takes ownership of the declared bindings when the
/// callback returns; the builder is closed at that point and every later declaration throws. Registered execution
/// binders run later. Their writers, contexts, and callback-provided raw resources must not be retained, and binders
/// must not dispose raw resources. Disposal ownership continues to follow each resource's owned or borrowed
/// registration. Every binding name must be a unique SkSL identifier matching a declaration in the source.
/// </remarks>
public sealed class ShaderBindingBuilder
{
    private readonly List<ShaderUniformBinding> _uniforms = [];
    private readonly List<ShaderResourceBinding> _resources = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private bool _closed;

    internal ShaderBindingBuilder()
    {
    }

    /// <summary>Declares a direct uniform whose canonical value is written without an execution callback.</summary>
    /// <typeparam name="T">An unmanaged type in the supported canonical scalar, vector, or matrix allowlist.</typeparam>
    /// <param name="name">The unique non-null SkSL uniform declaration name.</param>
    /// <param name="value">The value copied into the immutable description for execution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, or <typeparamref name="T"/> is not a supported canonical
    /// uniform type.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">An unsigned value cannot be represented by its SkSL type.</exception>
    /// <exception cref="InvalidOperationException">
    /// The <see cref="ShaderDescription"/> that owns this builder was already created.
    /// </exception>
    public void Uniform<T>(string name, T value)
        where T : unmanaged
    {
        BeginBinding(name);
        ShaderCanonicalValue canonical = ShaderCanonicalValue.Create(value);
        CompleteBinding(new ShaderUniformBinding(
            name,
            new DirectUniformStructuralKey(typeof(T)),
            readsExecutionContext: false,
            (writer, _) => writer.Set(value),
            canonical.ThrowIfIncompatible));
    }

    /// <summary>Declares a direct floating-point uniform from a sequence copied during description creation.</summary>
    /// <param name="name">The unique non-null SkSL uniform declaration name.</param>
    /// <param name="values">A non-empty sequence whose contents are copied immediately and are never retained.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, or <paramref name="values"/> is empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The <see cref="ShaderDescription"/> that owns this builder was already created.
    /// </exception>
    public void Uniform(string name, ReadOnlySpan<float> values)
    {
        BeginBinding(name);
        float[] copy = values.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("A direct uniform span cannot be empty.", nameof(values));
        CompleteBinding(new ShaderUniformBinding(
            name,
            typeof(float[]),
            readsExecutionContext: false,
            (writer, _) => writer.Set(copy),
            declaration => ShaderCanonicalValue.ThrowIfFloatSequenceIncompatible(copy, declaration)));
    }

    /// <summary>Declares a uniform whose value is produced by an execution-time binder.</summary>
    /// <typeparam name="T">An unmanaged type in the supported canonical scalar, vector, or matrix allowlist.</typeparam>
    /// <param name="name">The unique non-null SkSL uniform declaration name.</param>
    /// <param name="value">
    /// The author value passed to <paramref name="bind"/> during execution.
    /// </param>
    /// <param name="bind">
    /// The non-null execution callback. It must call <see cref="ShaderUniformWriter.Set{T}(T)"/> or
    /// <see cref="ShaderUniformWriter.Set(ReadOnlySpan{float})"/> exactly once and must not retain the writer or
    /// context. The unmanaged <paramref name="value"/> is passed by value.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="bind"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, an identity is invalid, or <typeparamref name="T"/> is not
    /// a supported canonical uniform type.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">An unsigned value cannot be represented by its SkSL type.</exception>
    /// <exception cref="InvalidOperationException">
    /// The <see cref="ShaderDescription"/> that owns this builder was already created.
    /// </exception>
    public void Uniform<T>(
        string name,
        T value,
        Action<ShaderUniformWriter, T, ShaderExecutionContext> bind)
        where T : unmanaged
    {
        BeginBinding(name);
        ArgumentNullException.ThrowIfNull(bind);
        CompleteBinding(new ShaderUniformBinding(
            name,
            new CustomUniformStructuralKey(
                typeof(T),
                RenderDescriptionValidation.StructuralIdentityOfExecution(bind)),
            readsExecutionContext: true,
            (writer, context) => bind(writer, value, context),
            static _ => { }));
    }

    /// <summary>Declares a child-shader resource produced by an execution-time binder.</summary>
    /// <typeparam name="T">The raw request-scoped resource type.</typeparam>
    /// <param name="name">The unique non-null SkSL child-shader declaration name.</param>
    /// <param name="resource">A non-null resource token registered with the request family.</param>
    /// <param name="coordinateSpace">How the returned child shader interprets coordinates passed to its <c>eval</c>.</param>
    /// <param name="bind">
    /// The non-null execution callback. It must call <see cref="ShaderResourceWriter.Set"/> exactly once with a newly
    /// created shader. It must not retain the writer, context, or callback-provided resource and must not dispose the
    /// resource. A borrowed resource remains caller-owned and its pixel-affecting state must remain read-only
    /// throughout the executing request; an owned resource remains request-owned.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/>, <paramref name="resource"/>, or <paramref name="bind"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, or an identity is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="coordinateSpace"/> is not a defined <see cref="ShaderResourceCoordinateSpace"/> value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The <see cref="ShaderDescription"/> that owns this builder was already created.
    /// </exception>
    public void Resource<T>(
        string name,
        RenderResource<T> resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        Action<ShaderResourceWriter, T, ShaderExecutionContext> bind)
        where T : class
    {
        BeginBinding(name);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(bind);
        ThrowIfCoordinateSpaceUndefined(coordinateSpace);
        CompleteBinding(new ShaderResourceBinding(
            name,
            resource,
            coordinateSpace,
            new ResourceBindingStructuralKey(
                typeof(T),
                RenderDescriptionValidation.StructuralIdentityOfExecution(bind)),
            (writer, value, context) => bind(writer, (T)value, context),
            use => resource.Registry.Use(resource, value =>
            {
                use(value);
                return true;
            })));
    }

    /// <summary>Declares a child-shader resource whose binder also receives an author value.</summary>
    /// <typeparam name="T">The raw request-scoped resource type.</typeparam>
    /// <typeparam name="TValue">The author value type passed to <paramref name="bind"/>.</typeparam>
    /// <param name="name">The unique non-null SkSL child-shader declaration name.</param>
    /// <param name="resource">A non-null resource token registered with the request family.</param>
    /// <param name="coordinateSpace">How the returned child shader interprets coordinates passed to its <c>eval</c>.</param>
    /// <param name="value">The author value passed to <paramref name="bind"/> during execution.</param>
    /// <param name="bind">
    /// The non-null execution callback, under the same rules as
    /// <see cref="Resource{T}(string, RenderResource{T}, ShaderResourceCoordinateSpace, Action{ShaderResourceWriter, T, ShaderExecutionContext})"/>.
    /// </param>
    /// <remarks>
    /// <paramref name="value"/> is not part of the binding's structural identity, so a plan compiled for one value
    /// is replayed for another. The declaring caller is what keys the plan, exactly as it is for a custom uniform.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/>, <paramref name="resource"/>, or <paramref name="bind"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is invalid or duplicated, or an identity is invalid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="coordinateSpace"/> is not a defined <see cref="ShaderResourceCoordinateSpace"/> value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The <see cref="ShaderDescription"/> that owns this builder was already created.
    /// </exception>
    public void Resource<T, TValue>(
        string name,
        RenderResource<T> resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        TValue value,
        Action<ShaderResourceWriter, T, TValue, ShaderExecutionContext> bind)
        where T : class
    {
        BeginBinding(name);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(bind);
        ThrowIfCoordinateSpaceUndefined(coordinateSpace);
        CompleteBinding(new ShaderResourceBinding(
            name,
            resource,
            coordinateSpace,
            new ResourceBindingStructuralKey(
                typeof(T),
                RenderDescriptionValidation.StructuralIdentityOfExecution(bind)),
            (writer, raw, context) => bind(writer, (T)raw, value, context),
            use => resource.Registry.Use(resource, raw =>
            {
                use(raw);
                return true;
            })));
    }

    private static void ThrowIfCoordinateSpaceUndefined(ShaderResourceCoordinateSpace coordinateSpace)
    {
        if (!Enum.IsDefined(coordinateSpace))
            throw new ArgumentOutOfRangeException(nameof(coordinateSpace), coordinateSpace, "The coordinate space is invalid.");
    }

    /// <remarks>
    /// The list itself, handed to the description that closed this builder. Nothing can append to it afterwards,
    /// so the description keeps it rather than copying it.
    /// </remarks>
    internal List<ShaderUniformBinding> Uniforms => _uniforms;

    /// <inheritdoc cref="Uniforms"/>
    internal List<ShaderResourceBinding> Resources => _resources;

    /// <summary>Gets the names of the bindings the two lists carry.</summary>
    /// <remarks>
    /// A name enters this set only in <see cref="CompleteBinding(ShaderUniformBinding)"/> or
    /// <see cref="CompleteBinding(ShaderResourceBinding)"/>, alongside the binding that carries it, so the set is
    /// exactly the union of the two lists' names and the description does not have to rebuild it. Committing the
    /// name in <see cref="BeginBinding"/> instead would leave it behind when a declaration throws after its name
    /// is accepted and the callback swallows that exception, and the description would then take a uniform the
    /// shader declares but nothing writes.
    /// </remarks>
    internal HashSet<string> Names => _names;

    /// <summary>Closes the builder so that the description taking its lists can rely on them never growing.</summary>
    internal void Close() => _closed = true;

    private void CompleteBinding(ShaderUniformBinding binding)
    {
        _names.Add(binding.Name);
        _uniforms.Add(binding);
    }

    private void CompleteBinding(ShaderResourceBinding binding)
    {
        _names.Add(binding.Name);
        _resources.Add(binding);
    }

    private void BeginBinding(string name)
    {
        if (_closed)
        {
            throw new InvalidOperationException(
                $"Cannot declare shader binding '{name}': the ShaderDescription that owns this builder has already "
                + "been created. A binding callback must declare every binding before it returns and must not "
                + "retain the builder.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsIdentifier(name))
            throw new ArgumentException("A shader binding name must be a valid identifier.", nameof(name));
        if (_names.Contains(name))
            throw new ArgumentException($"Duplicate shader binding name '{name}'.", nameof(name));
    }

    private static bool IsIdentifier(string name)
    {
        if (!(char.IsLetter(name[0]) || name[0] == '_'))
            return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
                return false;
        }
        return true;
    }

}

internal sealed record DirectUniformStructuralKey(Type Type);

internal sealed record CustomUniformStructuralKey(Type Type, object Binder);

internal sealed record ResourceBindingStructuralKey(Type Type, object Binder);
