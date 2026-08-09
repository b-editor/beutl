using System.Reflection;

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
    /// implementation cannot be rebuilt (the original expression is then left in place). The
    /// default tries a public <c>(Guid, string)</c> constructor; implementations that cannot be
    /// rebuilt that way must override this method.
    /// </summary>
    IReferenceExpression? Rebind(Guid objectId)
    {
        try
        {
            return (IReferenceExpression?)Activator.CreateInstance(
                GetType(),
                objectId,
                PropertyPath);
        }
        catch (Exception ex) when (ex is MissingMethodException
                                   or TargetInvocationException
                                   or ArgumentException
                                   or InvalidCastException)
        {
            return null;
        }
    }
}
