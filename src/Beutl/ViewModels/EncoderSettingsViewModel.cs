﻿using System.ComponentModel.DataAnnotations;
using Beutl.Media.Encoding;
using Beutl.Operation;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using DynamicData;

namespace Beutl.ViewModels;

public sealed class EncoderSettingsViewModel : IPropertyEditorContextVisitor, IServiceProvider, IDisposable
{
    private readonly CommandRecorder _recorder = new();

    public EncoderSettingsViewModel(MediaEncoderSettings settings)
    {
        Settings = settings;
        InitializeCoreObject(settings, (_, m) => m.Browsable);
    }

    public MediaEncoderSettings Settings { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(CommandRecorder))
        {
            return _recorder;
        }

        return null;
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    private void InitializeCoreObject(MediaEncoderSettings obj,
        Func<CoreProperty, CorePropertyMetadata, bool>? predicate = null)
    {
        Type objType = obj.GetType();
        Type adapterType = typeof(CorePropertyAdapter<>);

        List<CoreProperty> cprops = [.. PropertyRegistry.GetRegistered(objType)];
        cprops.RemoveAll(x => !(predicate?.Invoke(x, x.GetMetadata<CorePropertyMetadata>(objType)) ?? true));
        List<IPropertyAdapter> props = cprops.ConvertAll(x =>
        {
            CorePropertyMetadata metadata = x.GetMetadata<CorePropertyMetadata>(objType);
            Type adapterGType = adapterType.MakeGenericType(x.PropertyType);
            return (IPropertyAdapter)Activator.CreateInstance(adapterGType, x, obj)!;
        });

        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        IPropertyAdapter[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
            if (foundItems != null && extension != null)
            {
                if (extension.TryCreateContext(foundItems, out IPropertyEditorContext? context))
                {
                    tempItems.Add(context);
                    context.Accept(this);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);

        tempItems.Sort((x, y) =>
        {
            int xx = GetDisplayAttribute(x)?.GetOrder() ?? 0;
            int yy = GetDisplayAttribute(y)?.GetOrder() ?? 0;
            return xx - yy;
        });

        foreach ((string? Key, IPropertyEditorContext?[] Value) group in tempItems
                     .GroupBy(x => GetDisplayAttribute(x)?.GetGroupName())
                     .Select(x => (x.Key, x.ToArray()))
                     .ToArray())
        {
            if (group.Key != null)
            {
                IPropertyEditorContext?[] array = group.Value;
                if (array.Length >= 1)
                {
                    int index = tempItems.IndexOf(array[0]);
                    tempItems.RemoveMany(array);
                    tempItems.Insert(index, new PropertyEditorGroupContext(array, group.Key, index == 0));
                }
            }
        }

        Properties.AddRange(tempItems);
    }

    private static DisplayAttribute? GetDisplayAttribute(IPropertyEditorContext? context)
    {
        if (context is BaseEditorViewModel { PropertyAdapter: { } adapter }
            && adapter.GetCoreProperty() is { } coreProperty
            && coreProperty.TryGetMetadata(adapter.ImplementedType, out CorePropertyMetadata? metadata))
        {
            return metadata.DisplayAttribute;
        }
        else
        {
            return null;
        }
    }

    public void Dispose()
    {
        for (int i = Properties.Count - 1; i >= 0; i--)
        {
            var item = Properties[i];
            Properties.RemoveAt(i);
            item?.Dispose();
        }
    }
}
