using System.Collections.Generic;

using BEditor.PackageInstaller.Models;

namespace BEditor.PackageInstaller.ViewModels
{
    public sealed class CompletePageViewModel
    {
        public CompletePageViewModel(IEnumerable<PackageChange> failedChanges, IEnumerable<PackageChange> successfulChanges)
        {
            FailedChanges = failedChanges;
            SuccessfulChanges = successfulChanges;
        }

        public IEnumerable<PackageChange> FailedChanges { get; }

        public IEnumerable<PackageChange> SuccessfulChanges { get; }
    }
}