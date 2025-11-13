namespace Beutl.Protocol.Queries;

public class QueryField
{
    public QueryField(string name, QueryField[]? subFields = null)
    {
        Name = name;
        SubFields = subFields ?? [];
    }

    public string Name { get; }

    public QueryField[] SubFields { get; }

    public bool HasSubFields => SubFields.Length > 0;

    public static QueryField FromJson(string fieldName, object[]? subFieldNames = null)
    {
        if (subFieldNames == null || subFieldNames.Length == 0)
        {
            return new QueryField(fieldName);
        }

        var subFields = new List<QueryField>();
        foreach (object item in subFieldNames)
        {
            if (item is string simpleName)
            {
                subFields.Add(new QueryField(simpleName));
            }
            else if (item is Dictionary<string, object> nested)
            {
                foreach (var kvp in nested)
                {
                    object[]? nestedSubs = kvp.Value as object[];
                    subFields.Add(FromJson(kvp.Key, nestedSubs));
                }
            }
        }

        return new QueryField(fieldName, subFields.ToArray());
    }
}
