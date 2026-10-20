using Beutl.Editor;

namespace Beutl.UnitTests.Editor;

public class HistoryManagerEditSessionTests
{
    private HistoryManager _history = null!;
    private int _value;
    private int _other;

    [SetUp]
    public void SetUp()
    {
        _history = new HistoryManager(new OperationSequenceGenerator());
        _value = 0;
        _other = 0;
    }

    [TearDown]
    public void TearDown() => _history.Dispose();

    private void SetValue(int value)
    {
        int old = _value;
        _value = value;
        _history.Record(() => _value = value, () => _value = old);
    }

    private void SetOther(int value)
    {
        int old = _other;
        _other = value;
        _history.Record(() => _other = value, () => _other = old);
    }

    [Test]
    public void Complete_commits_only_the_changes_made_inside_the_session()
    {
        EditSession session = _history.BeginEdit("Drag");
        using (session.Enter())
            SetValue(1);
        SetOther(5);
        using (session.Enter())
            SetValue(2);
        session.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(_history.UndoCount, Is.EqualTo(1));
            Assert.That(_history.PeekUndo()!.DisplayName, Is.EqualTo("Drag"));
            Assert.That(_history.HasPendingOperations, Is.True, "The write outside the session stays pending.");
        });

        _history.Rollback();
        Assert.That(_history.Undo(), Is.True);
        Assert.That((_value, _other), Is.EqualTo((0, 0)));
    }

    [Test]
    public void Cancel_reverts_only_the_session()
    {
        EditSession session = _history.BeginEdit("Drag");
        SetOther(5);
        using (session.Enter())
            SetValue(3);
        session.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That((_value, _other), Is.EqualTo((0, 5)));
            Assert.That(_history.UndoCount, Is.Zero);
            Assert.That(_history.HasPendingOperations, Is.True);
            Assert.That(session.IsOpen, Is.False);
        });
    }

    [Test]
    public void Beginning_a_session_commits_earlier_pending_changes_as_their_own_entry()
    {
        SetOther(5);
        EditSession session = _history.BeginEdit("Drag");
        using (session.Enter())
            SetValue(1);
        session.Complete();

        Assert.That(_history.UndoCount, Is.EqualTo(2));
        Assert.That(_history.Undo(), Is.True);
        Assert.That((_value, _other), Is.EqualTo((0, 5)), "The session's entry is undone first.");
        Assert.That(_history.Undo(), Is.True);
        Assert.That((_value, _other), Is.EqualTo((0, 0)));
    }

    [Test]
    public void Only_one_session_is_open_at_a_time()
    {
        using EditSession session = _history.BeginEdit("Drag");

        Assert.Throws<InvalidOperationException>(() => _history.BeginEdit("Other"));
        session.Complete();
        Assert.DoesNotThrow(() => _history.BeginEdit("Other").Dispose());
    }

    [Test]
    public void History_moves_wait_for_the_open_session()
    {
        SetValue(1);
        _history.Commit("First");
        EditSession session = _history.BeginEdit("Drag");
        using (session.Enter())
            SetValue(2);

        Assert.Multiple(() =>
        {
            Assert.That(_history.Undo(), Is.False);
            Assert.That(_history.JumpTo(0), Is.False);
            Assert.That(_value, Is.EqualTo(2));
        });

        session.Complete();
        Assert.That(_history.Undo(), Is.True);
        Assert.That(_value, Is.EqualTo(1));
    }

    [Test]
    public void History_control_inside_the_session_scope_throws()
    {
        EditSession session = _history.BeginEdit("Drag");
        using (session.Enter())
        {
            Assert.Throws<InvalidOperationException>(() => _history.Commit());
            Assert.Throws<InvalidOperationException>(session.Complete);
        }

        session.Complete();
    }

    [Test]
    public void Disposing_an_open_session_cancels_it()
    {
        using (EditSession session = _history.BeginEdit("Drag"))
        using (session.Enter())
            SetValue(4);

        Assert.That(_value, Is.Zero);
        Assert.That(_history.IsEditing, Is.False);
    }

    [Test]
    public async Task Queued_edits_run_after_the_session_in_call_order()
    {
        var order = new List<string>();
        Task now = _history.RunAfterEditAsync(() => order.Add("idle"));
        EditSession session = _history.BeginEdit("Drag");
        Task first = _history.RunAfterEditAsync(() => order.Add("first"));
        using var cancel = new CancellationTokenSource();
        Task dropped = _history.RunAfterEditAsync(() => order.Add("dropped"), cancel.Token);
        Task second = _history.RunAfterEditAsync(() => order.Add("second"));
        cancel.Cancel();

        Assert.That(order, Is.EqualTo(new[] { "idle" }));
        session.Complete();
        await Task.WhenAll(now, first, second);

        Assert.That(order, Is.EqualTo(new[] { "idle", "first", "second" }));
        Assert.That(dropped.IsCanceled, Is.True);
    }

    [Test]
    public void A_queued_edit_that_throws_faults_only_its_own_task()
    {
        EditSession session = _history.BeginEdit("Drag");
        Task failing = _history.RunAfterEditAsync(() => throw new InvalidOperationException("boom"));
        Task next = _history.RunAfterEditAsync(() => SetValue(7));
        session.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(failing.IsFaulted, Is.True);
            Assert.That(next.IsCompletedSuccessfully, Is.True);
            Assert.That(_value, Is.EqualTo(7));
        });
    }
}
