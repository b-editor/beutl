namespace BeUtl.Language;

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
        public static string CannotRenameBecauseConflicts => "S.Warning.CannotRenameBecauseConflicts".GetStringResource(@"Cannot rename from ""{0}"" to ""{1}""
because the new name conflicts with an existing folder.");
        //S.Warning.FileDoesNotExist
        public static string FileDoesNotExist => "S.Warning.FileDoesNotExist".GetStringResource("File does not exist.");
        //S.Warning.CouldNotOpenProject
        public static string CouldNotOpenProject => "S.Warning.CouldNotOpenProject".GetStringResource("Could not open project.");

        //S.Warning.ItAlreadyExists
        private static IObservable<string>? s_itAlreadyExists;
        public static IObservable<string> ItAlreadyExistsObservable => s_itAlreadyExists ??= "S.Warning.ItAlreadyExists".GetStringObservable("It already exists.");
        //S.Warning.ValueLessThanOrEqualToZero
        private static IObservable<string>? s_valueLessThanOrEqualToZero;
        public static IObservable<string> ValueLessThanOrEqualToZeroObservable => s_valueLessThanOrEqualToZero ??= "S.Warning.ValueLessThanOrEqualToZero".GetStringObservable("Cannot specify a value less than or equal to 0.");
        //S.Warning.ValueLessThanZero
        private static IObservable<string>? s_valueLessThanZero;
        public static IObservable<string> ValueLessThanZeroObservable => s_valueLessThanZero ??= "S.Warning.ValueLessThanZero".GetStringObservable("Cannot specify a value less than to 0.");
        //S.Warning.CannotRenameBecauseConflicts
        private static IObservable<string>? s_cannotRenameBecauseConflicts;
        public static IObservable<string> CannotRenameBecauseConflictsObservable => s_cannotRenameBecauseConflicts ??= "S.Warning.CannotRenameBecauseConflicts".GetStringObservable(@"Cannot rename from ""{0}"" to ""{1}""
because the new name conflicts with an existing folder.");
        //S.Warning.FileDoesNotExist
        private static IObservable<string>? s_fileDoesNotExist;
        public static IObservable<string> FileDoesNotExistObservable => s_fileDoesNotExist ??= "S.Warning.FileDoesNotExist".GetStringObservable("File does not exist.");
        //S.Warning.CouldNotOpenProject
        private static IObservable<string>? s_couldNotOpenProject;
        public static IObservable<string> CouldNotOpenProjectObservable => s_couldNotOpenProject ??= "S.Warning.CouldNotOpenProject".GetStringObservable("Could not open project.");
    }
}
