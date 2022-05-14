namespace BeUtl.Language;

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

        //S.Message.ItemsSaved
        private static IObservable<string>? s_itemsSaved;
        public static IObservable<string> ItemsSavedObservable => s_itemsSaved ??= "S.Message.ItemsSaved".GetStringObservable("{0} items saved!");
        //S.Message.ItemSaved
        private static IObservable<string>? s_itemSaved;
        public static IObservable<string> ItemSavedObservable => s_itemSaved ??= "S.Message.ItemSaved".GetStringObservable("{0} was saved!");
        //S.Message.OperationCouldNotBeExecuted
        private static IObservable<string>? s_operationCouldNotBeExecuted;
        public static IObservable<string> OperationCouldNotBeExecutedObservable => s_operationCouldNotBeExecuted ??= "S.Message.OperationCouldNotBeExecuted".GetStringObservable("The operation could not be executed.");
        //S.Message.DoYouWantToDeleteThisDirectory
        private static IObservable<string>? s_doYouWantToDeleteThisDirectory;
        public static IObservable<string> DoYouWantToDeleteThisDirectoryObservable => s_doYouWantToDeleteThisDirectory ??= "S.Message.DoYouWantToDeleteThisDirectory".GetStringObservable("Do you want to delete this directory?");
        //S.Message.DoYouWantToDeleteThisFile
        private static IObservable<string>? s_doYouWantToDeleteThisFile;
        public static IObservable<string> DoYouWantToDeleteThisFileObservable => s_doYouWantToDeleteThisFile ??= "S.Message.DoYouWantToDeleteThisFile".GetStringObservable("Do you want to delete this file?");
        //S.Message.DoYouWantToExcludeThisSceneFromProject
        private static IObservable<string>? s_doYouWantToExcludeThisSceneFromProject;
        public static IObservable<string> DoYouWantToExcludeThisSceneFromProjectObservable => s_doYouWantToExcludeThisSceneFromProject ??= "S.Message.DoYouWantToExcludeThisSceneFromProject".GetStringObservable("Do you want to exclude this scene from the project?");
        //S.Message.DoYouWantToExcludeThisItemFromProject
        private static IObservable<string>? s_doYouWantToExcludeThisItemFromProject;
        public static IObservable<string> DoYouWantToExcludeThisItemFromProjectObservable => s_doYouWantToExcludeThisItemFromProject ??= "S.Message.DoYouWantToExcludeThisItemFromProject".GetStringObservable("Do you want to exclude this item from the project?");
    }
}
