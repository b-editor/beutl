namespace Beutl.Engine.Expressions;

// Non-generic view of a ReferenceExpression so a consumer that only has an IExpression (no static T)
// can read the referenced object id and property path without evaluating the expression.
public interface IReferenceExpression : IExpression
{
    Guid ObjectId { get; }

    string PropertyPath { get; }

    bool HasPropertyPath { get; }

    /// <summary>
    /// Returns an equivalent expression targeting <paramref name="objectId"/>, preserving the
    /// concrete implementation and its property path, or <see langword="null"/> when the
    /// implementation cannot preserve all of its state while rebinding. Implementations that
    /// support rebinding must override this method explicitly.
    /// </summary>
    IReferenceExpression? Rebind(Guid objectId) => null;
}
