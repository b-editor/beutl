namespace BeUtl.Language;

#pragma warning disable IDE0002

public static partial class StringResources
{
    public static class Warning
    {
        //S.Warning.ItAlreadyExists
        public static string ItAlreadyExists => "S.Warning.ItAlreadyExists".GetStringResource("It already exists.");
        //S.Warning.ValueLessThanOrEqualToZero
        public static string ValueLessThanOrEqualToZero => "S.Warning.ValueLessThanOrEqualToZero".GetStringResource("Cannot specify a value less than or equal to 0.");
        //S.Warning.ValueLessThanZero
        public static string ValueLessThanZero => "S.Warning.ValueLessThanZero".GetStringResource("Cannot specify a value less than to 0.");
        //S.Warning.CannotRenameBecauseConflicts
        public static string CannotRenameBecauseConflicts => "S.Warning.CannotRenameBecauseConflicts".GetStringResource("Cannot rename from \"{0}\" to \"{1}\"\nbecause the new name conflicts with an existing folder.\n");
        //S.Warning.FileDoesNotExist
        public static string FileDoesNotExist => "S.Warning.FileDoesNotExist".GetStringResource("File does not exist.");
        //S.Warning.CouldNotOpenProject
        public static string CouldNotOpenProject => "S.Warning.CouldNotOpenProject".GetStringResource("Could not open project.");

        //S.Warning.ItAlreadyExists
        private static IObservable<string>? s_itAlreadyExists;
        public static IObservable<string> ItAlreadyExistsObservable => s_itAlreadyExists ??= "S.Warning.ItAlreadyExists".GetStringObservable(StringResources.Warning.ItAlreadyExists);
        //S.Warning.ValueLessThanOrEqualToZero
        private static IObservable<string>? s_valueLessThanOrEqualToZero;
        public static IObservable<string> ValueLessThanOrEqualToZeroObservable => s_valueLessThanOrEqualToZero ??= "S.Warning.ValueLessThanOrEqualToZero".GetStringObservable(StringResources.Warning.ValueLessThanOrEqualToZero);
        //S.Warning.ValueLessThanZero
        private static IObservable<string>? s_valueLessThanZero;
        public static IObservable<string> ValueLessThanZeroObservable => s_valueLessThanZero ??= "S.Warning.ValueLessThanZero".GetStringObservable(StringResources.Warning.ValueLessThanZero);
        //S.Warning.CannotRenameBecauseConflicts
        private static IObservable<string>? s_cannotRenameBecauseConflicts;
        public static IObservable<string> CannotRenameBecauseConflictsObservable => s_cannotRenameBecauseConflicts ??= "S.Warning.CannotRenameBecauseConflicts".GetStringObservable(StringResources.Warning.CannotRenameBecauseConflicts);
        //S.Warning.FileDoesNotExist
        private static IObservable<string>? s_fileDoesNotExist;
        public static IObservable<string> FileDoesNotExistObservable => s_fileDoesNotExist ??= "S.Warning.FileDoesNotExist".GetStringObservable(StringResources.Warning.FileDoesNotExist);
        //S.Warning.CouldNotOpenProject
        private static IObservable<string>? s_couldNotOpenProject;
        public static IObservable<string> CouldNotOpenProjectObservable => s_couldNotOpenProject ??= "S.Warning.CouldNotOpenProject".GetStringObservable(StringResources.Warning.CouldNotOpenProject);
    }
}
