namespace Beutl.Protocol.Queries;

public class QuerySchema
{
    public QuerySchema(QueryField[] fields)
    {
        Fields = fields;
    }

    public QueryField[] Fields { get; }

    public static QuerySchema FromJson(Dictionary<string, object> jsonQuery)
    {
        if (!jsonQuery.TryGetValue("fields", out object? fieldsObj) || fieldsObj is not object[] fieldsArray)
        {
            throw new ArgumentException("Query must contain a 'fields' array.");
        }

        var fields = new List<QueryField>();
        foreach (object item in fieldsArray)
        {
            if (item is string simpleName)
            {
                fields.Add(new QueryField(simpleName));
            }
            else if (item is Dictionary<string, object> nested)
            {
                foreach (var kvp in nested)
                {
                    object[]? subFields = kvp.Value as object[];
                    fields.Add(QueryField.FromJson(kvp.Key, subFields));
                }
            }
        }

        return new QuerySchema(fields.ToArray());
    }
}
