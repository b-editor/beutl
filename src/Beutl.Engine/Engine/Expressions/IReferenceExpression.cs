namespace Beutl.Engine.Expressions;

// Non-generic view of a ReferenceExpression so a consumer that only has an IExpression (no static T)
// can read the referenced object id and property path without evaluating the expression.
public interface IReferenceExpression : IExpression
{
    Guid ObjectId { get; }

    string PropertyPath { get; }

    bool HasPropertyPath { get; }
}
