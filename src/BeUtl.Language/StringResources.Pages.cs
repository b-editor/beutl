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
        //S.SettingsPage.Backup
        public static string Backup => StringResources.Common.Backup;

        //S.SettingsPage.View
        public static IObservable<string> ViewObservable => StringResources.Common.ViewObservable;
        //S.SettingsPage.Font
        public static IObservable<string> FontObservable => StringResources.Common.FontObservable;
        //S.SettingsPage.Extensions
        public static IObservable<string> ExtensionsObservable => StringResources.Common.ExtensionsObservable;
        //S.SettingsPage.Info
        public static IObservable<string> InfoObservable => StringResources.Common.InfoObservable;
        //S.SettingsPage.Backup
        public static IObservable<string> BackupObservable => StringResources.Common.BackupObservable;
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

    public static class AccountSettingsPage
    {
        //S.AccountSettingsPage.SignOut
        public static string SignOut => "S.AccountSettingsPage.SignOut".GetStringResource("Sign out");
        //S.AccountSettingsPage.ChangeDisplayName
        public static string ChangeDisplayName => "S.AccountSettingsPage.ChangeDisplayName".GetStringResource("Change display name");
        //S.AccountSettingsPage.Change
        public static string Change => "S.AccountSettingsPage.Change".GetStringResource("Change");
        //S.AccountSettingsPage.DeleteAccount
        public static string DeleteAccount => "S.AccountSettingsPage.DeleteAccount".GetStringResource("Delete account");
        //S.AccountSettingsPage.ChangePassword
        public static string ChangePassword => "S.AccountSettingsPage.ChangePassword".GetStringResource("Change password");
        //S.AccountSettingsPage.NewPassword
        public static string NewPassword => "S.AccountSettingsPage.NewPassword".GetStringResource("New password");
        //S.AccountSettingsPage.YourEmailAddressHasBeenVerified
        public static string YourEmailAddressHasBeenVerified => "S.AccountSettingsPage.YourEmailAddressHasBeenVerified".GetStringResource("Your email address has been verified.");
        //S.AccountSettingsPage.WithGoogle
        public static string WithGoogle => "S.AccountSettingsPage.WithGoogle".GetStringResource("With Google.");
        //S.AccountSettingsPage.WithPassword
        public static string WithPassword => "S.AccountSettingsPage.WithPassword".GetStringResource("With Password.");
        //S.AccountSettingsPage.UnlinkFromGoogle
        public static string UnlinkFromGoogle => "S.AccountSettingsPage.UnlinkFromGoogle".GetStringResource("Unlink from Google");
        //S.AccountSettingsPage.UnlinkFromGoogleBody
        public static string UnlinkFromGoogleBody => "S.AccountSettingsPage.UnlinkFromGoogleBody".GetStringResource("Unlink from Google.\nIf you perform this operation, you will not be able to sign in using your Google account.\nDo you want to remove the linkage?");
        //S.AccountSettingsPage.LinkWithGoogle
        public static string LinkWithGoogle => "S.AccountSettingsPage.LinkWithGoogle".GetStringResource("Link with Google");

        //S.AccountSettingsPage.SignOut
        private static IObservable<string>? s_signOut;
        public static IObservable<string> SignOutObservable => s_signOut ??= "S.AccountSettingsPage.SignOut".GetStringObservable(StringResources.AccountSettingsPage.SignOut);
        //S.AccountSettingsPage.ChangeDisplayName
        private static IObservable<string>? s_changeDisplayName;
        public static IObservable<string> ChangeDisplayNameObservable => s_changeDisplayName ??= "S.AccountSettingsPage.ChangeDisplayName".GetStringObservable(StringResources.AccountSettingsPage.ChangeDisplayName);
        //S.AccountSettingsPage.Change
        private static IObservable<string>? s_change;
        public static IObservable<string> ChangeObservable => s_change ??= "S.AccountSettingsPage.Change".GetStringObservable(StringResources.AccountSettingsPage.Change);
        //S.AccountSettingsPage.DeleteAccount
        private static IObservable<string>? s_deleteAccount;
        public static IObservable<string> DeleteAccountObservable => s_deleteAccount ??= "S.AccountSettingsPage.DeleteAccount".GetStringObservable(StringResources.AccountSettingsPage.DeleteAccount);
        //S.AccountSettingsPage.ChangePassword
        private static IObservable<string>? s_changePassword;
        public static IObservable<string> ChangePasswordObservable => s_changePassword ??= "S.AccountSettingsPage.ChangePassword".GetStringObservable(StringResources.AccountSettingsPage.ChangePassword);
        //S.AccountSettingsPage.NewPassword
        private static IObservable<string>? s_newPassword;
        public static IObservable<string> NewPasswordObservable => s_newPassword ??= "S.AccountSettingsPage.NewPassword".GetStringObservable(StringResources.AccountSettingsPage.NewPassword);
        //S.AccountSettingsPage.YourEmailAddressHasBeenVerified
        private static IObservable<string>? s_yourEmailAddressHasBeenVerified;
        public static IObservable<string> YourEmailAddressHasBeenVerifiedObservable => s_yourEmailAddressHasBeenVerified ??= "S.AccountSettingsPage.YourEmailAddressHasBeenVerified".GetStringObservable(StringResources.AccountSettingsPage.YourEmailAddressHasBeenVerified);
        //S.AccountSettingsPage.WithGoogle
        private static IObservable<string>? s_withGoogle;
        public static IObservable<string> WithGoogleObservable => s_withGoogle ??= "S.AccountSettingsPage.WithGoogle".GetStringObservable(StringResources.AccountSettingsPage.WithGoogle);
        //S.AccountSettingsPage.WithPassword
        private static IObservable<string>? s_withPassword;
        public static IObservable<string> WithPasswordObservable => s_withPassword ??= "S.AccountSettingsPage.WithPassword".GetStringObservable(StringResources.AccountSettingsPage.WithPassword);
        //S.AccountSettingsPage.UnlinkFromGoogle
        private static IObservable<string>? s_unlinkFromGoogle;
        public static IObservable<string> UnlinkFromGoogleObservable => s_unlinkFromGoogle ??= "S.AccountSettingsPage.UnlinkFromGoogle".GetStringObservable(StringResources.AccountSettingsPage.UnlinkFromGoogle);
        //S.AccountSettingsPage.UnlinkFromGoogleBody
        private static IObservable<string>? s_unlinkFromGoogleBody;
        public static IObservable<string> UnlinkFromGoogleBodyObservable => s_unlinkFromGoogleBody ??= "S.AccountSettingsPage.UnlinkFromGoogleBody".GetStringObservable(StringResources.AccountSettingsPage.UnlinkFromGoogleBody);
        //S.AccountSettingsPage.LinkWithGoogle
        private static IObservable<string>? s_linkWithGoogle;
        public static IObservable<string> LinkWithGoogleObservable => s_linkWithGoogle ??= "S.AccountSettingsPage.LinkWithGoogle".GetStringObservable(StringResources.AccountSettingsPage.LinkWithGoogle);

        public static class Dialog1
        {
            //S.AccountSettingsPage.Dialog1.Title
            public static string Title => "S.AccountSettingsPage.Dialog1.Title".GetStringResource("Do you want to delete your account?");
            //S.AccountSettingsPage.Dialog1.Content
            public static string Content => "S.AccountSettingsPage.Dialog1.Content".GetStringResource("Delete the '{0}' account.\nThis operation cannot be undone.\n");
            //S.AccountSettingsPage.Dialog1.Yes
            public static string Yes => StringResources.Common.Yes;
            //S.AccountSettingsPage.Dialog1.No
            public static string No => StringResources.Common.No;

            //S.AccountSettingsPage.Dialog1.Title
            private static IObservable<string>? s_title;
            public static IObservable<string> TitleObservable => s_title ??= "S.AccountSettingsPage.Dialog1.Title".GetStringObservable(StringResources.AccountSettingsPage.Dialog1.Title);
            //S.AccountSettingsPage.Dialog1.Content
            private static IObservable<string>? s_content;
            public static IObservable<string> ContentObservable => s_content ??= "S.AccountSettingsPage.Dialog1.Content".GetStringObservable(StringResources.AccountSettingsPage.Dialog1.Content);
            //S.AccountSettingsPage.Dialog1.Yes
            public static IObservable<string> YesObservable => StringResources.Common.YesObservable;
            //S.AccountSettingsPage.Dialog1.No
            public static IObservable<string> NoObservable => StringResources.Common.NoObservable;
        }
    }

    public static class BackupSettingsPage
    {
        //S.BackupSettingsPage.BackupSettings
        public static string BackupSettings => "S.BackupSettingsPage.BackupSettings".GetStringResource("Back up settings");

        //S.BackupSettingsPage.BackupSettings
        private static IObservable<string>? s_backupSettings;
        public static IObservable<string> BackupSettingsObservable => s_backupSettings ??= "S.BackupSettingsPage.BackupSettings".GetStringObservable(StringResources.BackupSettingsPage.BackupSettings);
    }
}
