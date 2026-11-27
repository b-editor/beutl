using System.Collections.ObjectModel;
using Avalonia.Threading;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab;

internal sealed partial class AgentThreadController
{
    private readonly ObservableCollection<AgentPermissionItem> _permissions = [];
    private readonly ObservableCollection<AgentQuestionItem> _questions = [];
    private readonly Dictionary<AgentInteractionItem, int> _interactionOrder = [];
    private readonly Dictionary<string, int> _requestOrder = new(StringComparer.Ordinal);

    public ReadOnlyObservableCollection<AgentPermissionItem> PendingPermissions { get; }
    public int ActivePermissionIndex { get; private set; }
    public AgentPermissionItem? ActivePermission => _permissions.ElementAtOrDefault(ActivePermissionIndex);
    public AgentQuestionItem? ActiveQuestion => _questions.FirstOrDefault();
    public bool NeedsAttention => _permissions.Count > 0 || _questions.Count > 0;

    public void NavigatePermission(int direction)
    {
        int index = ActivePermissionIndex + direction;
        if (index < 0 || index >= _permissions.Count)
            return;
        ActivePermissionIndex = index;
        PublishState();
    }

    private Task<AgentPermissionDecision> ShowPermissionAsync(AgentPermissionRequest request, CancellationToken ct)
    {
        var card = new AgentPermissionItem(request);
        return AwaitInteractionAsync(request.ThreadId, request.RequestId, _permissions, card, card.Answer, ct);
    }

    private Task<IReadOnlyDictionary<string, string[]>> ShowQuestionAsync(AgentUserQuestionRequest request, CancellationToken ct)
    {
        if (request.Questions.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<string, string[]>>(new Dictionary<string, string[]>());
        var card = new AgentQuestionItem(request);
        return AwaitInteractionAsync(request.ThreadId, request.RequestId, _questions, card, card.Answer, ct);
    }

    private async Task<T> AwaitInteractionAsync<T, TCard>(
        Guid threadId, string requestId, ObservableCollection<TCard> queue, TCard card, Task<T> answer, CancellationToken ct)
        where TCard : AgentInteractionItem
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (threadId != ThreadId || _active is not { } turn || card.TurnId != turn.Id || _closed)
                    throw new OperationCanceledException(ct);
                int order = _requestOrder.GetValueOrDefault(requestId, int.MaxValue);
                _interactionOrder.Add(card, order);
                int index = 0;
                while (index < queue.Count && _interactionOrder[queue[index]] <= order)
                    index++;
                queue.Insert(index, card);
                if (card is AgentPermissionItem && ActivePermissionIndex > 0 && index <= ActivePermissionIndex)
                    ActivePermissionIndex++;
                PublishState();
                var kind = card is AgentPermissionItem ? AgentThreadNotificationKind.Approval : AgentThreadNotificationKind.Question;
                if (turn.NotifiedRequests.Add((kind, requestId)))
                    NotifyActivity(turn, DateTimeOffset.UtcNow, kind);
            }, DispatcherPriority.Normal, ct);
            return await answer.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => FinishInteraction(queue, card), DispatcherPriority.Normal);
        }
    }

    private void RememberRequestOrder(AgentTimelineEvent entry)
    {
        string? requestId = entry switch
        {
            AgentPermissionRequested permission => permission.Request.RequestId,
            AgentUserQuestionRequested question => question.Request.RequestId,
            _ => null
        };
        if (requestId is not null)
            _requestOrder.TryAdd(requestId, _requestOrder.Count);
    }

    private void FinishInteraction<TCard>(ObservableCollection<TCard> queue, TCard card) where TCard : AgentInteractionItem
    {
        card.Close();
        int index = queue.IndexOf(card);
        if (index < 0)
            return;
        queue.RemoveAt(index);
        _interactionOrder.Remove(card);
        if (card is AgentPermissionItem)
        {
            if (index < ActivePermissionIndex)
                ActivePermissionIndex--;
            ActivePermissionIndex = Math.Clamp(ActivePermissionIndex, 0, Math.Max(0, _permissions.Count - 1));
        }
        _transcript.AddInteraction(card);
        PublishState();
    }

    private void FinishInteractions()
    {
        foreach (var permission in _permissions.ToArray())
            FinishInteraction(_permissions, permission);
        foreach (var question in _questions.ToArray())
            FinishInteraction(_questions, question);
        _requestOrder.Clear();
    }
}
