using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Api;
using Beutl.Api.Objects;

namespace BeUtl.ViewModels.ExtensionsPages;

public class LibraryPageViewModel : BasePageViewModel
{
    private readonly AuthorizedUser _user;

    public LibraryPageViewModel(AuthorizedUser user)
    {
        _user = user;
    }

    public override void Dispose()
    {

    }
}
