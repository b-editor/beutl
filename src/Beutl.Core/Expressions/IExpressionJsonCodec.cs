using System.Text.Json.Nodes;

namespace Beutl;

// Reads and writes expressions as JSON. EngineObject supplies it to its properties, as Beutl.Engine holds the types.
internal interface IExpressionJsonCodec
{
    IExpression<T>? Read<T>(JsonNode node);

    JsonNode Write<T>(IExpression<T> expression);
}
