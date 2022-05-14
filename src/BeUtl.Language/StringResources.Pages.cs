namespace BeUtl.Language;

#pragma warning disable IDE0002

public static partial class StringResources
{
    public static class EditPage
    {
        public static string Open => StringResources.Common.Open;
        public static string CreateNew => StringResources.Common.CreateNew;
        public static string SceneFile => StringResources.Common.SceneFile;

        public static IObservable<string> OpenObservable => StringResources.Common.OpenObservable;
        public static IObservable<string> CreateNewObservable => StringResources.Common.CreateNewObservable;
        public static IObservable<string> SceneFileObservable => StringResources.Common.SceneFileObservable;
    }

    public static class SettingsPage
    {
        //S.SettingsPage.View
        public static string View => StringResources.Common.View;
        //S.SettingsPage.Font
        public static string Font => StringResources.Common.Font;
        //S.SettingsPage.Extensions
        public static string Extensions => StringResources.Common.Extensions;
        //S.SettingsPage.Info
        public static string Info => StringResources.Common.Info;

        //S.SettingsPage.View
        public static IObservable<string> ViewObservable => StringResources.Common.ViewObservable;
        //S.SettingsPage.Font
        public static IObservable<string> FontObservable => StringResources.Common.FontObservable;
        //S.SettingsPage.Extensions
        public static IObservable<string> ExtensionsObservable => StringResources.Common.ExtensionsObservable;
        //S.SettingsPage.Info
        public static IObservable<string> InfoObservable => StringResources.Common.InfoObservable;
    }

    public static class FontSettingsPage
    {
        //S.FontSettingsPage.Add
        public static string Add => StringResources.Common.Add;
        //S.FontSettingsPage.Remove
        public static string Remove => StringResources.Common.Remove;

        //S.FontSettingsPage.Add
        public static IObservable<string> AddObservable => StringResources.Common.AddObservable;
        //S.FontSettingsPage.Remove
        public static IObservable<string> RemoveObservable => StringResources.Common.RemoveObservable;
    }

    public static class InfomationPage
    {
        //S.InfomationPage.Links
        public static string Links => StringResources.Common.Links;
        //S.InfomationPage.SourceCode
        public static string SourceCode => StringResources.Common.SourceCode;
        //S.InfomationPage.License
        public static string License => StringResources.Common.License;
        //S.InfomationPage.ThirdPartyLicenses
        public static string ThirdPartyLicenses => StringResources.Common.ThirdPartyLicenses;

        //S.InfomationPage.Links
        public static IObservable<string> LinksObservable => StringResources.Common.LinksObservable;
        //S.InfomationPage.SourceCode
        public static IObservable<string> SourceCodeObservable => StringResources.Common.SourceCodeObservable;
        //S.InfomationPage.License
        public static IObservable<string> LicenseObservable => StringResources.Common.LicenseObservable;
        //S.InfomationPage.ThirdPartyLicenses
        public static IObservable<string> ThirdPartyLicensesObservable => StringResources.Common.ThirdPartyLicensesObservable;
    }

    public static class ViewSettingsPage
    {
        //S.ViewSettingsPage.Theme
        public static string Theme => StringResources.Common.Theme;
        //S.ViewSettingsPage.ThemeDescription
        public static string ThemeDescription => "S.ViewSettingsPage.ThemeDescription".GetStringResource("Select the theme of the app you want to display");
        //S.ViewSettingsPage.Theme.Light
        public static string Light => StringResources.Common.Light;
        //S.ViewSettingsPage.Theme.Dark
        public static string Dark => StringResources.Common.Dark;
        //S.ViewSettingsPage.Theme.HighContrast
        public static string HighContrast => StringResources.Common.HighContrast;
        //S.ViewSettingsPage.Theme.System
        public static string System => StringResources.Common.FollowSystem;
        //S.ViewSettingsPage.Language
        public static string Language => StringResources.Common.Language;

        //S.ViewSettingsPage.Theme
        public static IObservable<string> ThemeObservable => StringResources.Common.ThemeObservable;
        //S.ViewSettingsPage.ThemeDescription
        private static IObservable<string>? s_themeDescription;
        public static IObservable<string> ThemeDescriptionObservable => s_themeDescription ??= "S.ViewSettingsPage.ThemeDescription".GetStringObservable("Select the theme of the app you want to display");
        //S.ViewSettingsPage.Theme.Light
        public static IObservable<string> LightObservable => StringResources.Common.LightObservable;
        //S.ViewSettingsPage.Theme.Dark
        public static IObservable<string> DarkObservable => StringResources.Common.DarkObservable;
        //S.ViewSettingsPage.Theme.HighContrast
        public static IObservable<string> HighContrastObservable => StringResources.Common.HighContrastObservable;
        //S.ViewSettingsPage.Theme.System
        public static IObservable<string> SystemObservable => StringResources.Common.FollowSystemObservable;
        //S.ViewSettingsPage.Language
        public static IObservable<string> LanguageObservable => StringResources.Common.LanguageObservable;
    }

    public static class ExtensionsSettingsPage
    {
        public static class EditorExtensionPriority
        {
            //S.ExtensionsSettingsPage.EditorExtensionPriority
            public static string EditorExtensionPriority_ => "S.ExtensionsSettingsPage.EditorExtensionPriority".GetStringResource("Editor Extension Priority");
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Higher
            public static string Higher => "S.ExtensionsSettingsPage.EditorExtensionPriority.Higher".GetStringResource("Higher");
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Lower
            public static string Lower => "S.ExtensionsSettingsPage.EditorExtensionPriority.Lower".GetStringResource("Lower");
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Remove
            public static string Remove => StringResources.Common.Remove;
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Add
            public static string Add => StringResources.Common.Add;
            //S.ExtensionsSettingsPage.EditorExtensionPriority.All
            public static string All => "S.ExtensionsSettingsPage.EditorExtensionPriority.All".GetStringResource("All Editor Extensions");

            //S.ExtensionsSettingsPage.EditorExtensionPriority
            private static IObservable<string>? s_editorExtensionPriority;
            public static IObservable<string> EditorExtensionPriorityObservable => s_editorExtensionPriority ??= "S.ExtensionsSettingsPage.EditorExtensionPriority".GetStringObservable("Editor Extension Priority");
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Higher
            private static IObservable<string>? s_higher;
            public static IObservable<string> HigherObservable => s_higher ??= "S.ExtensionsSettingsPage.EditorExtensionPriority.Higher".GetStringObservable("Higher");
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Lower
            private static IObservable<string>? s_lower;
            public static IObservable<string> LowerObservable => s_lower ??= "S.ExtensionsSettingsPage.EditorExtensionPriority.Lower".GetStringObservable("Lower");
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Remove
            public static IObservable<string> RemoveObservable => StringResources.Common.RemoveObservable;
            //S.ExtensionsSettingsPage.EditorExtensionPriority.Add
            public static IObservable<string> AddObservable => StringResources.Common.AddObservable;
            //S.ExtensionsSettingsPage.EditorExtensionPriority.All
            private static IObservable<string>? s_all;
            public static IObservable<string> AllObservable => s_all ??= "S.ExtensionsSettingsPage.EditorExtensionPriority.All".GetStringObservable("All Editor Extensions");

            public static class FileExtension
            {
                //S.ExtensionsSettingsPage.EditorExtensionPriority.FileExtension.Add
                public static string Add => StringResources.Common.Add;
                //S.ExtensionsSettingsPage.EditorExtensionPriority.FileExtension.Remove
                public static string Remove => StringResources.Common.Remove;

                //S.ExtensionsSettingsPage.EditorExtensionPriority.FileExtension.Add
                public static IObservable<string> AddObservable => StringResources.Common.AddObservable;
                //S.ExtensionsSettingsPage.EditorExtensionPriority.FileExtension.Remove
                public static IObservable<string> RemoveObservable => StringResources.Common.RemoveObservable;
            }

            public static class Dialog1
            {
                //S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Title
                public static string Title => "S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Title".GetStringResource("Add file extension");
                //S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Add
                public static string Add => StringResources.Common.Add;
                //S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Cancel
                public static string Cancel => StringResources.Common.Cancel;

                //S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Title
                private static IObservable<string>? s_title;
                public static IObservable<string> TitleObservable => s_title ??= "S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Title".GetStringObservable("Add file extension");
                //S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Add
                public static IObservable<string> AddObservable => StringResources.Common.AddObservable;
                //S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Cancel
                public static IObservable<string> CancelObservable => StringResources.Common.CancelObservable;
            }
        }
    }
}
