using System;
using System.Collections.Immutable;

namespace Beutl.UnitTests.Core;

public class RecordableCommandsTests
{
    private sealed class FlagCommand : IRecordableCommand
    {
        private readonly Action _do;
        private readonly Action _undo;
        private readonly Action _redo;
        private readonly ImmutableArray<IStorable?> _storables;

        public FlagCommand(Action @do, Action undo, Action? redo = null, ImmutableArray<IStorable?>? storables = null)
        {
            _do = @do;
            _undo = undo;
            _redo = redo ?? @do;
            _storables = storables ?? [];
        }

        public bool DidDo { get; private set; }
        public bool DidUndo { get; private set; }
        public bool DidRedo { get; private set; }

        public ImmutableArray<IStorable?> GetStorables() => _storables;
        public void Do() { _do(); DidDo = true; }
        public void Undo() { _undo(); DidUndo = true; }
        public void Redo() { _redo(); DidRedo = true; }
    }

    [Test]
    public void Create_DelegateCommand_RunsDoUndoRedo()
    {
        int x = 0;
        var cmd = RecordableCommands.Create(
            () => x = 1,
            () => x = 0,
            ImmutableArray<IStorable?>.Empty);

        cmd.Do();
        Assert.That(x, Is.EqualTo(1));
        cmd.Undo();
        Assert.That(x, Is.EqualTo(0));
        cmd.Redo();
        Assert.That(x, Is.EqualTo(1));
    }

    [Test]
    public void Append_And_ToCommand_Composition_OrderAndStorables()
    {
        var s1 = ImmutableArray<IStorable?>.Empty;
        var s2 = ImmutableArray.Create<IStorable?>(new IStorable?[] { null });
        int order = 0;
        int a=0,b=0;

        var c1 = new FlagCommand(() => { a = ++order; }, () => { a = -1; }, storables: s1);
        var c2 = new FlagCommand(() => { b = ++order; }, () => { b = -2; }, storables: s2);

        IRecordableCommand combo = c1.Append(c2);
        var rec = new CommandRecorder();
        combo.DoAndRecord(rec);

        Assert.That(a, Is.EqualTo(1));
        Assert.That(b, Is.EqualTo(2));
        Assert.That(rec.CanUndo, Is.True);

        rec.Undo();
        Assert.That(a, Is.EqualTo(-1));
        Assert.That(b, Is.EqualTo(-2));

        // convert to multiple and check storables concat
        var multi = new IRecordableCommand[] { c1, c2 }.ToCommand(ImmutableArray<IStorable?>.Empty);
        var storables = multi.GetStorables();
        Assert.That(storables.Length, Is.EqualTo(1));
    }

    [Test]
    public void WithStorables_OverwriteAndConcat()
    {
        var sBase = ImmutableArray.Create<IStorable?>(new IStorable?[] { null, null });
        var sOverwrite = ImmutableArray.Create<IStorable?>(new IStorable?[] { null });
        var sConcat = ImmutableArray.Create<IStorable?>(new IStorable?[] { null });
        var cmd = new FlagCommand(() => {}, () => {}, storables: sBase);

        var overwrote = cmd.WithStoables(sOverwrite, overwrite: true);
        Assert.That(overwrote.GetStorables().Length, Is.EqualTo(1));

        var concatenated = cmd.WithStoables(sConcat, overwrite: false);
        Assert.That(concatenated.GetStorables().Length, Is.EqualTo(3));
    }
}
