using Beutl.Api.Services;
using Beutl.Services.AI;

namespace Beutl.UnitTests.Services.AI;

[TestFixture]
public sealed class AiRequestKeyTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Beutl.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void CanWithdraw_AuthenticationRequiresKnownAbsentFirstAttempt()
    {
        var first = new AiRequestName("first", IsRepeat: false);
        var repeat = new AiRequestName("repeat", IsRepeat: true);
        var knownAbsent = new AuthenticationRequiredException();
        var unknown = new AuthenticationRequiredException(
            currentAttemptReservationIsKnownAbsent: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AiRequestOutcome.CanWithdraw(first, knownAbsent), Is.True);
            Assert.That(AiRequestOutcome.CanWithdraw(first, unknown), Is.False);
            Assert.That(AiRequestOutcome.CanWithdraw(repeat, knownAbsent), Is.False);
            Assert.That(
                AiRequestOutcome.CanWithdraw(first, new AiFileTooLargeException()),
                Is.True);
            Assert.That(
                AiRequestOutcome.CanWithdraw(repeat, new AiFileTooLargeException()),
                Is.False);
            Assert.That(
                AiRequestOutcome.CanWithdraw(repeat, new AiPlanRequiredException()),
                Is.True);
        }
    }

    [Test]
    public void NameFor_TellsTheSameRequestFromADifferentOne()
    {
        var key = new AiRequestKey();

        AiRequestName first = key.NameFor("a prompt", "16:9");
        AiRequestName again = key.NameFor("a prompt", "16:9");
        AiRequestName other = key.NameFor("a prompt", "9:16");

        Assert.Multiple(() =>
        {
            Assert.That(first.IsRepeat, Is.False);
            Assert.That(again.Key, Is.EqualTo(first.Key));
            Assert.That(again.IsRepeat, Is.True);
            Assert.That(other.Key, Is.Not.EqualTo(first.Key));
        });
    }

    [Test]
    public void WithdrawAfterNoReservation_ClosesANameTheServerNeverMadeAJobUnder()
    {
        // A refusal over the plan, the balance or the model made no job, so the name is let go.
        var key = new AiRequestKey();
        AiRequestName issued = key.NameFor("a prompt");
        Assert.That(key.HasOutstandingName.Value, Is.True);

        key.WithdrawAfterNoReservation(issued);

        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.False);
            // 取り下げたので、次は初めての依頼として扱われる——残高の確認が
            // もう一度前に立つ。
            Assert.That(key.NameFor("a prompt").IsRepeat, Is.False);
        });
    }

    [Test]
    public void WithdrawAfterNoReservation_LetsTheSameRequestGoOutUnderTheSameNameAgain()
    {
        // 予約されなかったのだから、その名前はまだ何も指していない。同じ依頼は
        // 同じ名前で出してよい——別の名前にすると、次に届いたときに新しい依頼
        // として課金される。
        var key = new AiRequestKey();
        AiRequestName issued = key.NameFor("a prompt");

        key.WithdrawAfterNoReservation(issued);

        Assert.That(key.NameFor("a prompt").Key, Is.EqualTo(issued.Key));
    }

    [Test]
    public void Retire_SettlesOneRequestAndLeavesTheOthersOutstanding()
    {
        // A が課金されたまま応答を落とし、入力を変えた B が成功する。B の決着で
        // A の名前まで捨てると、A に戻ったとき新しい名前になり、支払い済みの
        // ものをもう一度買うことになる。
        var key = new AiRequestKey();
        AiRequestName a = key.NameFor("prompt a");
        AiRequestName b = key.NameFor("prompt b");

        key.Retire(b);

        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(key.NameFor("prompt a").Key, Is.EqualTo(a.Key));
            Assert.That(key.NameFor("prompt a").IsRepeat, Is.True);
            // 決着した依頼をもう一度出すなら、それは新しい依頼。
            Assert.That(key.NameFor("prompt b").Key, Is.Not.EqualTo(b.Key));
        });
    }

    [Test]
    public void WithdrawAfterNoReservation_LeavesTheOtherNamesOfTheRunAlone()
    {
        var key = new AiRequestKey();
        AiRequestName first = key.NameFor(0, "a chunk");
        AiRequestName second = key.NameFor(1, "a chunk");

        key.WithdrawAfterNoReservation(second);

        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(key.NameFor(0, "a chunk").Key, Is.EqualTo(first.Key));
            Assert.That(key.NameFor(0, "a chunk").IsRepeat, Is.True);
        });
    }

    [Test]
    public void FileStamp_NamesAFileTheWayTheServerDoes()
    {
        // サーバーは、届いた名前と中身でその依頼を見分ける。場所や更新時刻で
        // 見分けると、移しただけ・触っただけの絵が別の依頼になり、同じ仕事を
        // 二度買うことになる。
        string here = Path.Combine(_directory, "picture.png");
        string moved = Path.Combine(_directory, "moved");
        Directory.CreateDirectory(moved);
        string there = Path.Combine(moved, "picture.png");
        File.WriteAllBytes(here, [1, 2, 3]);
        string before = Stamp(here);

        File.SetLastWriteTimeUtc(here, DateTime.UnixEpoch);
        Assert.That(Stamp(here), Is.EqualTo(before),
            "Touching a file does not make it another request.");

        File.Copy(here, there);
        Assert.That(Stamp(there), Is.EqualTo(before),
            "Nor does moving it.");

        File.WriteAllBytes(here, [3, 2, 1]);
        Assert.That(Stamp(here), Is.Not.EqualTo(before),
            "Changing what is in it does.");

        string renamed = Path.Combine(_directory, "another.png");
        File.WriteAllBytes(renamed, [1, 2, 3]);
        Assert.That(Stamp(renamed), Is.Not.EqualTo(before),
            "So does the name it arrives under.");

        static string Stamp(string path)
            => AiRequestKey.FileStamp(Path.GetFileName(path), File.ReadAllBytes(path));
    }

    [Test]
    public void Retire_StartsTheNextRequestUnderAFreshName()
    {
        var key = new AiRequestKey();
        AiRequestName settled = key.NameFor("a prompt");

        key.Retire();

        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.False);
            Assert.That(key.NameFor("a prompt").Key, Is.Not.EqualTo(settled.Key));
        });
    }

    [Test]
    public void ResumedRun_AsksForItsFirstPieceAsARepeat()
    {
        // 控えから拾い直した実行は、名前は持っていても「どれを送ったか」は
        // 持っていない。最初に配り直す名前は、前のセッションで支払われた job を
        // 指しているかもしれないので、一度だけ再送として扱う。
        var resumed = new AiRequestKey("seed-of-the-run", namePending: true);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.HasOutstandingName.Value, Is.True);
            Assert.That(resumed.NameFor(0, "a chunk").IsRepeat, Is.True);
            Assert.That(resumed.NameFor(1, "a chunk").IsRepeat, Is.False);
        });
    }

    [Test]
    public void ResumedRun_WithNothingInFlight_StartsAsANewRequest()
    {
        // A kept seed does not mean the run was paid for: runs that were never reserved keep theirs too.
        var resumed = new AiRequestKey("seed-of-the-run", namePending: false);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.HasOutstandingName.Value, Is.False);
            Assert.That(resumed.NameFor(0, "a chunk").IsRepeat, Is.False);
        });
    }

    [Test]
    public void Seed_SurvivesIntoTheNamesOfAResumedRun()
    {
        var key = new AiRequestKey();
        AiRequestName original = key.NameFor(2, "a chunk");

        var resumed = new AiRequestKey(key.Seed);

        Assert.That(resumed.NameFor(2, "a chunk").Key, Is.EqualTo(original.Key));
    }

    [TestCase("image.generate", null)]
    [TestCase("video.generate", null)]
    [TestCase("image.edit", "upscale")]
    public void DurableNameSurvivesARecreatedRequestKey(
        string operation,
        string? editTask)
    {
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: operation);
        string?[] parts = editTask is null ? ["prompt"] : [editTask, "prompt"];
        AiRequestName issued = first.NameFor(parts);

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: operation);
        AiRequestName recovered = restarted.NameFor(parts);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Key, Is.EqualTo(issued.Key));
            Assert.That(recovered.IsRepeat, Is.True);
            if (editTask is not null)
            {
                Assert.That(
                    File.ReadAllText(Path.Combine(_directory, "ai-request-recovery.json")),
                    Does.Contain($"image.edit.{editTask}"));
            }
        });
    }

    [Test]
    public void RetireRemovesTheIssuingAccountAfterAnAccountSwitch()
    {
        string? account = "account-a";
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => account),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("prompt");
        account = "account-b";
        AiRequestName issuedForB = key.NameFor("prompt");
        Assert.That(issuedForB.Key, Is.Not.EqualTo(issued.Key));

        key.Retire(issued);

        var accountA = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account-a"),
            operation: "image.generate");
        var accountB = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account-b"),
            operation: "image.generate");
        Assert.Multiple(() =>
        {
            Assert.That(accountA.NameFor("prompt").Key, Is.Not.EqualTo(issued.Key));
            Assert.That(accountB.NameFor("prompt").Key, Is.EqualTo(issuedForB.Key));
        });
    }

    [Test]
    public void AccountSwitchBeforeSendFailsWithoutDeletingTheIssuedRecovery()
    {
        string? account = "account-a";
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => account),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("prompt");
        account = "account-b";

        Assert.Throws<AuthenticationRequiredException>(() =>
            key.EnterAuthenticatedScope(issued));

        var accountA = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account-a"),
            operation: "image.generate");
        Assert.That(accountA.NameFor("prompt").Key, Is.EqualTo(issued.Key));
    }

    [Test]
    public void StoreFailureDoesNotPublishAnInMemoryOutstandingName()
    {
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestKeyMap(_directory) { LockTimeout = TimeSpan.Zero },
                () => "account"),
            operation: "image.generate");
        string lockPath = Path.Combine(_directory, "ai-request-recovery.json.lock");
        using (var lease = new FileStream(
                   lockPath,
                   FileMode.OpenOrCreate,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            Assert.Throws<InvalidDataException>(() => key.NameFor("prompt"));
            Assert.That(key.HasOutstandingName.Value, Is.False);
        }

        AiRequestName issued = key.NameFor("prompt");
        Assert.Multiple(() =>
        {
            Assert.That(issued.IsRepeat, Is.False);
            Assert.That(key.HasOutstandingName.Value, Is.True);
        });
    }

    [Test]
    public void RetireLeavesOtherDurableRequestNamesRecoverable()
    {
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: "image.generate");
        AiRequestName first = key.NameFor("first");
        AiRequestName second = key.NameFor("second");
        key.Retire(second);

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: "image.generate");
        Assert.Multiple(() =>
        {
            Assert.That(restarted.NameFor("first").Key, Is.EqualTo(first.Key));
            Assert.That(restarted.NameFor("first").IsRepeat, Is.True);
            Assert.That(restarted.NameFor("second").Key, Is.Not.EqualTo(second.Key));
        });
    }

    [Test]
    public void RetireAllRemovesEveryDurableIdentityBeforeResettingMemory()
    {
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: "image.generate");
        AiRequestName first = key.NameFor("first");
        AiRequestName second = key.NameFor("second");

        key.Retire();

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: "image.generate");
        Assert.Multiple(() =>
        {
            Assert.That(restarted.NameFor("first").Key, Is.Not.EqualTo(first.Key));
            Assert.That(restarted.NameFor("second").Key, Is.Not.EqualTo(second.Key));
        });
    }

    [Test]
    public void PendingRowsFromAnEarlierRunAreListedButHoldNothingOpen()
    {
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: "image.generate");
        AiRequestName paid = first.NameFor("paid prompt");

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestKeyMap(_directory), () => "account"),
            operation: "image.generate");
        bool gateWasOpen = restarted.HasOutstandingName.Value;
        AiRequestName unrelated = restarted.NameFor("another prompt");

        Assert.Multiple(() =>
        {
            Assert.That(gateWasOpen, Is.False, "the earlier request is collected through the lookup, not by sending again");
            Assert.That(restarted.PendingAttempts(AiOperations.ImageGeneration).Select(row => row.Key), Does.Contain(paid.Key));
            Assert.That(unrelated.IsRepeat, Is.False);
            Assert.That(unrelated.Key, Is.Not.EqualTo(paid.Key));
        });
    }

    [Test]
    public void SettleAdvancesTheNameAndLeavesOtherAccountUntouched()
    {
        string? account = "account-a";
        var store = new FileAiRequestKeyMap(_directory);
        var keyA = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => account),
            operation: "image.generate");
        AiRequestName abandoned = keyA.NameFor("same");
        account = "account-b";
        var keyB = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => account),
            operation: "image.generate");
        AiRequestName other = keyB.NameFor("same");

        account = "account-a";
        AiPendingAttempt pendingA = store.PendingFor("account-a", "image.generate").Single();
        keyA.Settle(pendingA);
        AiRequestName next = keyA.NameFor("same");

        Assert.Multiple(() =>
        {
            Assert.That(next.Key, Is.Not.EqualTo(abandoned.Key));
            Assert.That(next.IsRepeat, Is.False);
            Assert.That(store.PendingFor("account-b", "image.generate").Single().Key, Is.EqualTo(other.Key));
        });
    }

    [Test]
    public void SettleAfterAnotherProcessSettledTheRowStillStartsAFreshName()
    {
        var store = new FileAiRequestKeyMap(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("same");
        AiPendingAttempt pending = store.PendingFor("account", "image.generate").Single();
        Assert.That(new FileAiRequestKeyMap(_directory).Remove(pending), Is.True);

        Assert.DoesNotThrow(() => key.Settle(pending));
        AiRequestName next = key.NameFor("same");
        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(next.Key, Is.Not.EqualTo(issued.Key));
            Assert.That(next.IsRepeat, Is.False);
        });
    }

    [Test]
    public void ANameCollectedElsewhereIsSpent()
    {
        var store = new FileAiRequestKeyMap(_directory);
        var sender = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName lost = sender.NameFor("same");
        // Another tab collects the job through the lookup and lets its row go.
        Assert.That(store.Remove(store.PendingFor("account", "image.generate").Single()), Is.True);

        AiRequestName next = sender.NameFor("same");

        Assert.Multiple(() =>
        {
            Assert.That(next.Key, Is.Not.EqualTo(lost.Key), "the collected job would answer under the old name");
            Assert.That(next.IsRepeat, Is.False);
        });
    }

    [Test]
    public void StaleRetireAndWithdrawCannotDeleteANewerRow()
    {
        var store = new FileAiRequestKeyMap(_directory);
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName oldName = first.NameFor("same");
        var stale = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        _ = stale.NameFor("same");
        AiPendingAttempt oldAttempt = store.PendingFor("account", "image.generate").Single();
        first.Settle(oldAttempt);
        AiRequestName currentName = first.NameFor("same");

        AiRequestName materializedOld = new(oldName.Key, true);
        stale.Retire(materializedOld);
        stale.WithdrawAfterNoReservation(materializedOld);

        Assert.That(store.PendingFor("account", "image.generate").Single().Key, Is.EqualTo(currentName.Key));
    }

    private static AiRequestRecoveryContext RecoveryContext(
        FileAiRequestKeyMap store,
        Func<string?> accountProvider)
        => new(
            store,
            () => accountProvider() is { } account
                ? new AiAuthenticatedRequestIdentity(account, User: null)
                : null);
}
