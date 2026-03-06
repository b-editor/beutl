using System.Diagnostics.CodeAnalysis;

namespace Beutl.Engine.Expressions;

public sealed class ReferenceExpression<T> : IExpression<T>
{
    private ICoreObject? _cachedObject;

    public ReferenceExpression(Guid objectId)
    {
        ObjectId = objectId;
        PropertyPath = string.Empty;
    }

    public ReferenceExpression(Guid objectId, string? propertyPath)
    {
        ObjectId = objectId;
        PropertyPath = propertyPath ?? string.Empty;
    }

    public Guid ObjectId { get; }

    public string PropertyPath { get; }

    public bool HasPropertyPath => !string.IsNullOrEmpty(PropertyPath);

    public string ExpressionString =>
        HasPropertyPath ? $"{ObjectId}.{PropertyPath}" : ObjectId.ToString();

    public T Evaluate(ExpressionContext context)
    {
        // キャッシュの有効性チェック
        // HierarchicalRootがnullになった場合（オブジェクトがシーンから削除された場合など）は再検索
        if (_cachedObject is IHierarchical hierarchical && hierarchical.HierarchicalRoot == null)
        {
            _cachedObject = null;
        }

        // キャッシュがない場合は検索
        if (_cachedObject == null)
        {
            _cachedObject = context.PropertyLookup.FindById(ObjectId);
        }

        if (_cachedObject == null)
            return default!;

        if (HasPropertyPath)
        {
            // PropertyLookupでネストしたプロパティを解決
            if (context.PropertyLookup.TryGetPropertyValue(ObjectId, PropertyPath, context, out T? value))
            {
                return value!;
            }
            return default!;
        }
        else
        {
            // オブジェクト全体を返す
            if (_cachedObject is T typedObject)
            {
                return typedObject;
            }
            return default!;
        }
    }

    public bool Validate([NotNullWhen(false)] out string? error)
    {
        // 参照式は常に有効（実行時にオブジェクトが見つからない可能性はあるが、式自体は有効）
        error = null;
        return true;
    }

    public override string ToString() => ExpressionString;
}
