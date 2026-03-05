using Beutl.Composition;

namespace Beutl.Engine;

public static class ResourceReconciler
{
    public static void ReconcileListFromFlow<TItem, TResource>(
        CompositionContext context, IListProperty<TItem> property,
        IList<TResource> consumed, List<TResource> field,
        IList<int> versions, ref bool changed)
        where TItem : EngineObject where TResource : EngineObject.Resource
    {
        // consumedを先頭から追加していき、後ろにpropertyの残りを追加する
        if (consumed.Count != versions.Count)
        {
            field.RemoveRange(0, versions.Count);

            versions.Clear();
            for (int i = 0; i < consumed.Count; i++)
            {
                field.Insert(i, consumed[i]);
                versions.Add(consumed[i].Version);
            }

            // 引数のpropertyを追加
            changed = true;
        }
        else
        {
            // 少なくとも個数は変わっていないので個別に見ていく
            // field[0..consumed.Count]の要素のVersionとversionListを比較
            for (int i = 0; i < consumed.Count; i++)
            {
                var resource = consumed[i];
                if (i < field.Count)
                {
                    if (field[i] != resource)
                    {
                        field[i] = resource;
                        changed = true;
                    }
                    else
                    {
                        var oldVersion = versions[i]; // consumed.Count == versionList.Countなのでiは常に有効
                        changed |= oldVersion != resource.Version;
                    }

                    versions[i] = resource.Version;
                }
                else
                {
                    // Addではないのは後ろにプロパティによって追加されたリソースがいる可能性があるため
                    field.Insert(i, resource);
                    // versionListはAddで良い
                    versions.Add(resource.Version);
                    changed = true;
                }
            }
        }

        // propertyの残りを追加
        ReconcileListFromProperty(context, property, consumed.Count, field, ref changed);
    }

    public static void ReconcileListFromProperty<TItem, TResource>(
        CompositionContext context, IListProperty<TItem> prop,
        int offsetIndex, IList<TResource> field, ref bool changed)
        where TItem : EngineObject where TResource : EngineObject.Resource
    {
        for (int i = 0; i < prop.Count; i++)
        {
            var child = prop[i];
            if (i + offsetIndex < field.Count)
            {
                var item = field[i + offsetIndex];
                if (item.GetOriginal() != child)
                {
                    var oldItem = item;
                    item = (TResource)child.ToResource(context);
                    field[i + offsetIndex] = item;
                    changed = true;
                    oldItem.Dispose();
                }
                else
                {
                    var oldVersion = item.Version;
                    var _ = false;
                    item.Update(child, context, ref _);
                    if (!changed && oldVersion != item.Version)
                    {
                        changed = true;
                    }
                }
            }
            else
            {
                var item = (TResource)child.ToResource(context);
                field.Add(item);
                changed = true;
            }
        }

        // Propertyから削除されたときに対応するため
        if (!changed && field.Count != prop.Count + offsetIndex)
        {
            changed = true;
        }

        while (field.Count > prop.Count + offsetIndex)
        {
            var oldItem = field[^1];
            field.RemoveAt(field.Count - 1);
            oldItem.Dispose();
        }
    }

    public static void ReconcileResource<TObject, TResource>(
        CompositionContext context, TObject value,
        ref TResource? field, ref bool changed)
        where TObject : EngineObject? where TResource : EngineObject.Resource
    {
        if (value is null)
        {
            if (field is not null)
            {
                field.Dispose();
                field = null;
                changed = true;
            }
        }
        else
        {
            if (field is null)
            {
                field = (TResource)value.ToResource(context);
                changed = true;
            }
            else
            {
                if (field.GetOriginal() != value)
                {
                    var oldField = field;
                    field = (TResource)value.ToResource(context);
                    changed = true;
                    oldField.Dispose();
                }
                else
                {
                    var oldVersion = field.Version;
                    var _ = false;
                    field.Update(value, context, ref _);
                    if (oldVersion != field.Version)
                    {
                        changed = true;
                    }
                }
            }
        }
    }

}
