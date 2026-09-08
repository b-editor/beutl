namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// A screen that offers a list of AI models and can be told to read it again.
/// </summary>
/// <remarks>
/// The list is cached with a freshness window, so asking again costs nothing
/// while it is fresh. A screen built once and kept — a workspace page returned
/// to, a window brought back to the front — would otherwise go on offering
/// models an operator has since withdrawn.
/// </remarks>
internal interface IAiModelListConsumer
{
    void RefreshModels();
}
