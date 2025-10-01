using System.Linq.Expressions;
using System.Reflection;

namespace Beutl.Engine;

public class EngineObject : Hierarchical
{
    private readonly List<IProperty> _properties = new();

    public virtual IReadOnlyList<IProperty> Properties => _properties;

    protected void ScanProperties<T>() where T : EngineObject
    {
        var type = typeof(T);
        var propertyInfos = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        // リフレクションによるIProperty型プロパティ検出
        foreach (var propertyInfo in propertyInfos)
        {
            if (!typeof(IProperty).IsAssignableFrom(propertyInfo.PropertyType)) continue;

            // (object o) => ((T)o).Property
            // をExpressionで生成してキャッシュする
            var func = ReflectionCache<T>.Properties.FirstOrDefault(x => x.Item1 == propertyInfo).Item2;
            if (func == null)
            {
                var param = Expression.Parameter(typeof(object), "o");
                var cast = Expression.Convert(param, type);
                var propertyAccess = Expression.Property(cast, propertyInfo);
                var convertResult = Expression.Convert(propertyAccess, typeof(IProperty));
                var lambda = Expression.Lambda<Func<object, IProperty?>>(convertResult, param);
                func = lambda.Compile();
                ReflectionCache<T>.Properties.Add((propertyInfo, func));
            }

            var property = func(this);
            if (property != null)
            {
                _properties.Add(property);
            }
        }
    }

    protected void RegisterProperty(IProperty property)
    {
        if (!_properties.Contains(property))
        {
            _properties.Add(property);
        }
    }

    private static class ReflectionCache<T>
    {
        public static readonly List<(PropertyInfo, Func<object, IProperty?>)> Properties = new();
    }
}
