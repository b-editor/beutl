namespace Beutl.Editor;

public static class BaseUriHelper
{
    public static Uri? FindBaseUri(this ICoreObject? obj)
    {
        return (obj as IHierarchical)?.EnumerateAncestors<CoreObject>().FirstOrDefault(o => o.Uri != null)?.Uri;
    }
}
