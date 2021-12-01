namespace BEditorNext;

public static class ElementExtensions
{
    public static Element? Find(this Element self, Guid id)
    {
        foreach (var item in self.Children)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    public static Element? FindAllChildren(this Element self, Guid id)
    {
        foreach (var item in self.EnumerateAllChildren<Element>())
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

}
