using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Services;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.Helpers;

// Opt-in for controls whose click handlers do not expose a reactive command.
// Values in XAML are static Tool.Feature identifiers; never bind this property.
public sealed class UsageTracking : AvaloniaObject
{
    public static readonly AttachedProperty<string?> FeatureProperty =
        AvaloniaProperty.RegisterAttached<UsageTracking, Interactive, string?>("Feature");

    static UsageTracking()
    {
        FeatureProperty.Changed.AddClassHandler<Interactive>((control, change) =>
        {
            if (change.OldValue is not null) SetHandler(control, false);
            if (change.NewValue is not null) SetHandler(control, true);
        });
    }

    public static string? GetFeature(Interactive control) => control.GetValue(FeatureProperty);
    public static void SetFeature(Interactive control, string? value) => control.SetValue(FeatureProperty, value);

    private static void SetHandler(Interactive control, bool attach)
    {
        RoutedEvent<RoutedEventArgs>? click = GetClickEvent(control);
        if (click is null) return;
        if (attach) control.AddHandler(click, OnClick, RoutingStrategies.Bubble, handledEventsToo: true);
        else control.RemoveHandler(click, OnClick);
    }

    private static RoutedEvent<RoutedEventArgs>? GetClickEvent(Interactive control) => control switch
    {
        Button => Button.ClickEvent,
        MenuItem => MenuItem.ClickEvent,
        FAMenuFlyoutItem => FAMenuFlyoutItem.ClickEvent,
        _ => null
    };

    private static void OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Interactive control) return;
        // Submenu clicks bubble to the annotated parent. Prefer an annotation
        // on the clicked item when present, so nested handlers never double count.
        for (StyledElement? element = args.Source as StyledElement; element is not null; element = element.Parent)
        {
            if (element is not Interactive candidate || GetClickEvent(candidate) != args.RoutedEvent) continue;
            string? id = GetFeature(candidate);
            if (id is null || id.Length > 128) continue;
            int dot = id.IndexOf('.');
            if (dot <= 0 || dot == id.Length - 1) continue;
            if (ReferenceEquals(candidate, control))
                UsageTelemetry.Current?.Record("tool.action", id[..dot], id[(dot + 1)..]);
            return;
        }
    }
}
