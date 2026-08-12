using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class TelemetryConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool?> Beutl_LoggingProperty;
    public static readonly CoreProperty<bool?> Beutl_ApplicationProperty;
    public static readonly CoreProperty<bool?> Beutl_PackageManagementProperty;
    public static readonly CoreProperty<bool?> Beutl_Api_ClientProperty;
    public static readonly CoreProperty<bool?> UsageAnalyticsProperty;
    internal static readonly CoreProperty<bool> UsageAnalyticsMigratedFromLegacyProperty;
    internal static readonly CoreProperty<bool> UsageAnalyticsMigrationNoticeShownProperty;

    static TelemetryConfig()
    {
        Beutl_LoggingProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Logging))
            .DefaultValue(null)
            .Register();

        Beutl_ApplicationProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Application))
            .DefaultValue(null)
            .Register();

        Beutl_PackageManagementProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_PackageManagement))
            .DefaultValue(null)
            .Register();

        Beutl_Api_ClientProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Api_Client))
            .DefaultValue(null)
            .Register();

        UsageAnalyticsProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(UsageAnalytics))
            .DefaultValue(null)
            .Register();

        UsageAnalyticsMigratedFromLegacyProperty = new CorePropertyBuilder<bool, TelemetryConfig>(
            nameof(UsageAnalyticsMigratedFromLegacy),
            isAttached: true)
            .DefaultValue(false)
            .Register();

        UsageAnalyticsMigrationNoticeShownProperty = new CorePropertyBuilder<bool, TelemetryConfig>(
            nameof(UsageAnalyticsMigrationNoticeShown),
            isAttached: true)
            .DefaultValue(false)
            .Register();
    }

    public bool? Beutl_Logging
    {
        get => GetValue(Beutl_LoggingProperty);
        set => SetValue(Beutl_LoggingProperty, value);
    }

    public bool? Beutl_Application
    {
        get => GetValue(Beutl_ApplicationProperty);
        set => SetValue(Beutl_ApplicationProperty, value);
    }

    public bool? Beutl_PackageManagement
    {
        get => GetValue(Beutl_PackageManagementProperty);
        set => SetValue(Beutl_PackageManagementProperty, value);
    }

    public bool? Beutl_Api_Client
    {
        get => GetValue(Beutl_Api_ClientProperty);
        set => SetValue(Beutl_Api_ClientProperty, value);
    }

    /// <summary>
    /// Gets or sets consent for privacy-preserving product usage analytics.
    /// This consent is intentionally independent from operational tracing and diagnostic logging.
    /// </summary>
    public bool? UsageAnalytics
    {
        get => GetValue(UsageAnalyticsProperty);
        set => SetValue(UsageAnalyticsProperty, value);
    }

    internal bool UsageAnalyticsMigratedFromLegacy
    {
        get => GetValue(UsageAnalyticsMigratedFromLegacyProperty);
        set => SetValue(UsageAnalyticsMigratedFromLegacyProperty, value);
    }

    internal bool UsageAnalyticsMigrationNoticeShown
    {
        get => GetValue(UsageAnalyticsMigrationNoticeShownProperty);
        set => SetValue(UsageAnalyticsMigrationNoticeShownProperty, value);
    }

    /// <summary>
    /// Applies the one-time migration for configurations written before <see cref="UsageAnalytics"/>
    /// existed. The historical application choice was the user-facing product-usage decision,
    /// so it is copied even when the unrelated legacy operational toggles were not persisted.
    /// </summary>
    internal bool MigrateUsageAnalyticsFromLegacy()
    {
        if (UsageAnalytics.HasValue || !Beutl_Application.HasValue)
        {
            return false;
        }

        UsageAnalytics = Beutl_Application;
        UsageAnalyticsMigratedFromLegacy = true;
        return true;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
