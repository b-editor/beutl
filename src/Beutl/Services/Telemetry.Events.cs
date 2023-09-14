using Beutl.Configuration;

namespace Beutl.Services;

internal static partial class Telemetry
{
    public static void Exception(Exception exception, bool unhandled = false)
    {
        try
        {
            TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
            if (config.Beutl_All_Errors == true)
            {
                s_client.TrackException(exception, new Dictionary<string, string>
                {
                    ["unhandled"] = unhandled.ToString()
                });
            }
        }
        catch
        {
        }
    }

    public static void NavigateExtensionsPage(string pageName)
    {
        TrackPageView($"/ExtensionsPage/{pageName}");
    }

    public static void NavigateSettingsPage(string pageName)
    {
        TrackPageView($"/SettingsPage/{pageName}");
    }

    public static void NavigateMainPage(string pageName)
    {
        TrackPageView($"/{pageName}");
    }

    public static void ToolTabSelected(string toolName)
    {
        TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
        if (config.Beutl_ViewTracking == true)
        {
            s_client.TrackEvent(nameof(ToolTabSelected), new Dictionary<string, string>
            {
                ["toolName"] = toolName
            });
        }
    }

    public static void ToolTabOpened(string toolName, string id)
    {
        TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
        if (config.Beutl_ViewTracking == true)
        {
            s_client.TrackEvent(nameof(ToolTabOpened), new Dictionary<string, string>
            {
                ["editorId"] = id,
                ["toolName"] = toolName
            });
        }
    }

    private static void TrackPageView(string name)
    {
        TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
        if (config.Beutl_ViewTracking == true)
        {
            s_client.TrackPageView(name);
        }
    }

    public static void Started()
    {
        TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
        if (config.Beutl_Application == true)
        {
            s_client.TrackEvent(nameof(Started));
        }
    }

    public static void WindowOpened()
    {
        TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
        if (config.Beutl_Application == true)
        {
            s_client.TrackEvent(nameof(WindowOpened));
        }
    }
}
