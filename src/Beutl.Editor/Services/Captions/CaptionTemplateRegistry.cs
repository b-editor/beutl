using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;

namespace Beutl.Editor.Services.Captions;

/// <summary>Describes one currently available caption template.</summary>
public sealed record CaptionTemplateDescriptor
{
    public CaptionTemplateDescriptor(
        CaptionTemplateId id,
        CaptionTemplateProviderId providerId,
        string name,
        int order = 0)
    {
        if (id.Value.Length == 0)
            throw new ArgumentException("A caption template identifier is required.", nameof(id));
        if (providerId.Value.Length == 0)
            throw new ArgumentException("A caption template provider identifier is required.", nameof(providerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        ProviderId = providerId;
        Name = name;
        Order = order;
    }

    public CaptionTemplateId Id { get; }

    public CaptionTemplateProviderId ProviderId { get; }

    public string Name { get; }

    public int Order { get; }
}

public interface ICaptionTemplateProvider
{
    ICoreReadOnlyList<CaptionTemplateDescriptor> Templates { get; }

    bool TryGet(
        CaptionTemplateId id,
        [NotNullWhen(true)] out CaptionTemplateDescriptor? template);

    CaptionTemplate GetTemplate(CaptionTemplateId id);
}

public sealed class CaptionTemplateRegistry : ICaptionTemplateProvider
{
    private readonly CoreList<CaptionTemplateDescriptor> _descriptors = [];
    private readonly object _replaceGate = new();
    private Dictionary<CaptionTemplateId, CaptionTemplate> _templates = [];

    internal CaptionTemplateRegistry(IEnumerable<CaptionTemplate> templates)
    {
        Replace(templates);
    }

    public ICoreReadOnlyList<CaptionTemplateDescriptor> Templates => _descriptors;

    // The user's object templates change while Beutl runs.
    internal void Replace(IEnumerable<CaptionTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var byId = new Dictionary<CaptionTemplateId, CaptionTemplate>();
        foreach (CaptionTemplate template in templates)
        {
            ArgumentNullException.ThrowIfNull(template, nameof(templates));
            if (!byId.TryAdd(template.Descriptor.Id, template))
            {
                throw new ArgumentException(
                    $"Caption template '{template.Descriptor.Id}' is registered more than once.",
                    nameof(templates));
            }
        }

        lock (_replaceGate)
        {
            Volatile.Write(ref _templates, byId);
            try
            {
                _descriptors.Replace(byId.Values
                    .Select(template => template.Descriptor)
                    .OrderBy(template => template.Order)
                    .ThenBy(template => template.Id.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            }
            catch
            {
                // An observer of the list cannot undo the change, which is already made.
            }
        }
    }

    public bool TryGet(
        CaptionTemplateId id,
        [NotNullWhen(true)] out CaptionTemplateDescriptor? template)
    {
        template = Volatile.Read(ref _templates).GetValueOrDefault(id)?.Descriptor;
        return template is not null;
    }

    public CaptionTemplate GetTemplate(CaptionTemplateId id)
        => Volatile.Read(ref _templates).GetValueOrDefault(id)
           ?? throw new KeyNotFoundException($"No caption template is registered with identifier '{id}'.");
}
