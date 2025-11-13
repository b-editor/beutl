namespace Beutl.Protocol.Queries;

public abstract class Mutation
{
    public abstract MutationResult Execute(MutationContext context);
}
