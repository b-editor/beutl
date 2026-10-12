using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

internal sealed class TestCapabilitySchemaExtension(
    params AiOperationCapabilitySchemaRegistration[] registrations)
    : AiOperationCapabilitySchemaExtension
{
    public override IReadOnlyCollection<AiOperationCapabilitySchemaRegistration> Registrations
    { get; } = registrations;
}
