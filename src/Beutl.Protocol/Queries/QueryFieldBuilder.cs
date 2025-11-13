using System.Linq.Expressions;

namespace Beutl.Protocol.Queries;

public class QueryFieldBuilder
{
    private readonly List<QueryField> _fields = new();

    public QueryFieldBuilder Select(string fieldName)
    {
        _fields.Add(new QueryField(fieldName));
        return this;
    }

    public QueryFieldBuilder Select(string fieldName, Action<QueryFieldBuilder> configure)
    {
        var builder = new QueryFieldBuilder();
        configure(builder);
        _fields.Add(new QueryField(fieldName, builder.Build()));
        return this;
    }

    public QueryFieldBuilder Select<T>(Expression<Func<T, object>> expression)
    {
        string fieldName = GetPropertyName(expression);
        _fields.Add(new QueryField(fieldName));
        return this;
    }

    public QueryFieldBuilder Select<T, TProperty>(Expression<Func<T, TProperty>> expression, Action<QueryFieldBuilder> configure)
    {
        string fieldName = GetPropertyName(expression);
        var builder = new QueryFieldBuilder();
        configure(builder);
        _fields.Add(new QueryField(fieldName, builder.Build()));
        return this;
    }

    internal QueryField[] Build()
    {
        return _fields.ToArray();
    }

    private static string GetPropertyName<T, TProperty>(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (expression.Body is UnaryExpression { Operand: MemberExpression unaryMember })
        {
            return unaryMember.Member.Name;
        }

        throw new ArgumentException("Expression must be a property access.");
    }
}
