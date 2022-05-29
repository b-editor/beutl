namespace BeUtl.Language;

#pragma warning disable IDE0002

public static partial class StringResources
{
    public static class Message
    {
        //S.Message.ItemsSaved
        public static string ItemsSaved => "S.Message.ItemsSaved".GetStringResource("{0} items saved!");
        //S.Message.ItemSaved
        public static string ItemSaved => "S.Message.ItemSaved".GetStringResource("{0} was saved!");
        //S.Message.OperationCouldNotBeExecuted
        public static string OperationCouldNotBeExecuted => "S.Message.OperationCouldNotBeExecuted".GetStringResource("The operation could not be executed.");
        //S.Message.DoYouWantToDeleteThisDirectory
        public static string DoYouWantToDeleteThisDirectory => "S.Message.DoYouWantToDeleteThisDirectory".GetStringResource("Do you want to delete this directory?");
        //S.Message.DoYouWantToDeleteThisFile
        public static string DoYouWantToDeleteThisFile => "S.Message.DoYouWantToDeleteThisFile".GetStringResource("Do you want to delete this file?");
        //S.Message.DoYouWantToExcludeThisSceneFromProject
        public static string DoYouWantToExcludeThisSceneFromProject => "S.Message.DoYouWantToExcludeThisSceneFromProject".GetStringResource("Do you want to exclude this scene from the project?");
        //S.Message.DoYouWantToExcludeThisItemFromProject
        public static string DoYouWantToExcludeThisItemFromProject => "S.Message.DoYouWantToExcludeThisItemFromProject".GetStringResource("Do you want to exclude this item from the project?");
        //S.Message.CouldNotOpenFollowingFileWithExtension
        public static string CouldNotOpenFollowingFileWithExtension => "S.Message.CouldNotOpenFollowingFileWithExtension".GetStringResource("Could not open the following file with '{0}'\n'{1}'\n");
        //S.Message.CannotDisplayThisContext
        public static string CannotDisplayThisContext => "S.Message.CannotDisplayThisContext".GetStringResource("Cannot display this context.");
        //S.Message.CouldNotCreateInstanceOfView
        public static string CouldNotCreateInstanceOfView => "S.Message.CouldNotCreateInstanceOfView".GetStringResource("Could not create an instance of View.");
        //S.Message.ContextNotCreated
        public static string ContextNotCreated => "S.Message.ContextNotCreated".GetStringResource("Context not created.");
        //S.Message.EditorContextHasAlreadyBeenCreated
        public static string EditorContextHasAlreadyBeenCreated => "S.Message.EditorContextHasAlreadyBeenCreated".GetStringResource("The editor context has already been created.");
        //S.Message.NullWasSpecifiedForEditorContext
        public static string NullWasSpecifiedForEditorContext => "S.Message.NullWasSpecifiedForEditorContext".GetStringResource("Null was specified for the editor context.");
        //S.Message.DoYouWantToAddThisItemToCurrentProject
        public static string DoYouWantToAddThisItemToCurrentProject => "S.Message.DoYouWantToAddThisItemToCurrentProject".GetStringResource("Do you want to add this item to current project?");
        //S.Message.RememberThisChoice
        public static string RememberThisChoice => "S.Message.RememberThisChoice".GetStringResource("Remember this choice");
        //S.Message.YourAccountHasBeenDeleted
        public static string YourAccountHasBeenDeleted => "S.Message.YourAccountHasBeenDeleted".GetStringResource("Your account has been deleted.");
        //S.Message.PasswordHasBeenChanged
        public static string PasswordHasBeenChanged => "S.Message.PasswordHasBeenChanged".GetStringResource("Password has been changed.");

        //S.Message.ItemsSaved
        private static IObservable<string>? s_itemsSaved;
        public static IObservable<string> ItemsSavedObservable => s_itemsSaved ??= "S.Message.ItemsSaved".GetStringObservable(StringResources.Message.ItemsSaved);
        //S.Message.ItemSaved
        private static IObservable<string>? s_itemSaved;
        public static IObservable<string> ItemSavedObservable => s_itemSaved ??= "S.Message.ItemSaved".GetStringObservable(StringResources.Message.ItemSaved);
        //S.Message.OperationCouldNotBeExecuted
        private static IObservable<string>? s_operationCouldNotBeExecuted;
        public static IObservable<string> OperationCouldNotBeExecutedObservable => s_operationCouldNotBeExecuted ??= "S.Message.OperationCouldNotBeExecuted".GetStringObservable(StringResources.Message.OperationCouldNotBeExecuted);
        //S.Message.DoYouWantToDeleteThisDirectory
        private static IObservable<string>? s_doYouWantToDeleteThisDirectory;
        public static IObservable<string> DoYouWantToDeleteThisDirectoryObservable => s_doYouWantToDeleteThisDirectory ??= "S.Message.DoYouWantToDeleteThisDirectory".GetStringObservable(StringResources.Message.DoYouWantToDeleteThisDirectory);
        //S.Message.DoYouWantToDeleteThisFile
        private static IObservable<string>? s_doYouWantToDeleteThisFile;
        public static IObservable<string> DoYouWantToDeleteThisFileObservable => s_doYouWantToDeleteThisFile ??= "S.Message.DoYouWantToDeleteThisFile".GetStringObservable(StringResources.Message.DoYouWantToDeleteThisFile);
        //S.Message.DoYouWantToExcludeThisSceneFromProject
        private static IObservable<string>? s_doYouWantToExcludeThisSceneFromProject;
        public static IObservable<string> DoYouWantToExcludeThisSceneFromProjectObservable => s_doYouWantToExcludeThisSceneFromProject ??= "S.Message.DoYouWantToExcludeThisSceneFromProject".GetStringObservable(StringResources.Message.DoYouWantToExcludeThisSceneFromProject);
        //S.Message.DoYouWantToExcludeThisItemFromProject
        private static IObservable<string>? s_doYouWantToExcludeThisItemFromProject;
        public static IObservable<string> DoYouWantToExcludeThisItemFromProjectObservable => s_doYouWantToExcludeThisItemFromProject ??= "S.Message.DoYouWantToExcludeThisItemFromProject".GetStringObservable(StringResources.Message.DoYouWantToExcludeThisItemFromProject);
        //S.Message.CouldNotOpenFollowingFileWithExtension
        private static IObservable<string>? s_couldNotOpenFollowingFileWithExtension;
        public static IObservable<string> CouldNotOpenFollowingFileWithExtensionObservable => s_couldNotOpenFollowingFileWithExtension ??= "S.Message.CouldNotOpenFollowingFileWithExtension".GetStringObservable(StringResources.Message.CouldNotOpenFollowingFileWithExtension);
        //S.Message.CannotDisplayThisContext
        private static IObservable<string>? s_cannotDisplayThisContext;
        public static IObservable<string> CannotDisplayThisContextObservable => s_cannotDisplayThisContext ??= "S.Message.CannotDisplayThisContext".GetStringObservable(StringResources.Message.CannotDisplayThisContext);
        //S.Message.CouldNotCreateInstanceOfView
        private static IObservable<string>? s_couldNotCreateInstanceOfView;
        public static IObservable<string> CouldNotCreateInstanceOfViewObservable => s_couldNotCreateInstanceOfView ??= "S.Message.CouldNotCreateInstanceOfView".GetStringObservable(StringResources.Message.CouldNotCreateInstanceOfView);
        //S.Message.ContextNotCreated
        private static IObservable<string>? s_contextNotCreated;
        public static IObservable<string> ContextNotCreatedObservable => s_contextNotCreated ??= "S.Message.ContextNotCreated".GetStringObservable(StringResources.Message.ContextNotCreated);
        //S.Message.EditorContextHasAlreadyBeenCreated
        private static IObservable<string>? s_editorContextHasAlreadyBeenCreated;
        public static IObservable<string> EditorContextHasAlreadyBeenCreatedObservable => s_editorContextHasAlreadyBeenCreated ??= "S.Message.EditorContextHasAlreadyBeenCreated".GetStringObservable(StringResources.Message.EditorContextHasAlreadyBeenCreated);
        //S.Message.NullWasSpecifiedForEditorContext
        private static IObservable<string>? s_nullWasSpecifiedForEditorContext;
        public static IObservable<string> NullWasSpecifiedForEditorContextObservable => s_nullWasSpecifiedForEditorContext ??= "S.Message.NullWasSpecifiedForEditorContext".GetStringObservable(StringResources.Message.NullWasSpecifiedForEditorContext);
        //S.Message.DoYouWantToAddThisItemToCurrentProject
        private static IObservable<string>? s_doYouWantToAddThisItemToCurrentProject;
        public static IObservable<string> DoYouWantToAddThisItemToCurrentProjectObservable => s_doYouWantToAddThisItemToCurrentProject ??= "S.Message.DoYouWantToAddThisItemToCurrentProject".GetStringObservable(StringResources.Message.DoYouWantToAddThisItemToCurrentProject);
        //S.Message.RememberThisChoice
        private static IObservable<string>? s_rememberThisChoice;
        public static IObservable<string> RememberThisChoiceObservable => s_rememberThisChoice ??= "S.Message.RememberThisChoice".GetStringObservable(StringResources.Message.RememberThisChoice);
        //S.Message.YourAccountHasBeenDeleted
        private static IObservable<string>? s_yourAccountHasBeenDeleted;
        public static IObservable<string> YourAccountHasBeenDeletedObservable => s_yourAccountHasBeenDeleted ??= "S.Message.YourAccountHasBeenDeleted".GetStringObservable(StringResources.Message.YourAccountHasBeenDeleted);
        //S.Message.PasswordHasBeenChanged
        private static IObservable<string>? s_passwordHasBeenChanged;
        public static IObservable<string> PasswordHasBeenChangedObservable => s_passwordHasBeenChanged ??= "S.Message.PasswordHasBeenChanged".GetStringObservable(StringResources.Message.PasswordHasBeenChanged);
    }
}
