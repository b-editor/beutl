using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;

namespace Beutl.Helpers;

public static class AddOrSetHelper
{
    public static void AddOrSet(ref FilterEffect? fe, FilterEffect toBeAdded)
    {
        if (fe is FilterEffectGroup feGroup)
        {
            feGroup.Children.Add(fe);
        }
        else if (fe != null)
        {
            fe = new FilterEffectGroup
            {
                Children = { fe, toBeAdded }
            };
        }
        else
        {
            fe = toBeAdded;
        }
    }

    public static void AddOrSet(ref Transform? tra, Transform toBeAdded)
    {
        if (tra is TransformGroup group)
        {
            group.Children.Add(tra);
        }
        else if (tra != null)
        {
            tra = new TransformGroup
            {
                Children = { tra, toBeAdded }
            };
        }
        else
        {
            tra = toBeAdded;
        }
    }
}
