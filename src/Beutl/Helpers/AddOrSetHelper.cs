using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;

namespace Beutl.Helpers;

public static class AddOrSetHelper
{
    public static void AddOrSet(ref FilterEffect? fe, FilterEffect toBeAdded, ImmutableArray<IStorable?> storables, CommandRecorder recorder)
    {
        if (fe is FilterEffectGroup feGroup)
        {
            feGroup.Children.BeginRecord<FilterEffect>()
                .Add(toBeAdded)
                .ToCommand(storables)
                .DoAndRecord(recorder);
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

    public static void AddOrSet(ref Transform? tra, Transform toBeAdded, ImmutableArray<IStorable?> storables, CommandRecorder recorder)
    {
        if (tra is TransformGroup group)
        {
            group.Children.BeginRecord<Transform>()
                .Add(toBeAdded)
                .ToCommand(storables)
                .DoAndRecord(recorder);
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
