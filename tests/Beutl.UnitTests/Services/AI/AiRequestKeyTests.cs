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
    public void DispatchedExactOwnerCanWithdrawAfterAuthoritativeNoReservation()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "video.generate");
        AiRequestName issued = key.NameFor("a prompt");
        AiRequestRecoveryLease claim = key.TryClaim(issued)!;
        key.MarkClaimDispatched(claim);
        AiPendingAttempt pending = store.PendingFor("account", "video.generate").Single();

        Assert.That(store.TryWithdraw(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint,
            pending.Key), Is.False,
            "A caller without the dispatch owner cannot clear a paid-job fence.");

        key.WithdrawAfterNoReservation(issued);
        Assert.Multiple(() =>
        {
            Assert.That(store.Find(
                pending.AccountId,
                pending.Operation,
                pending.Fingerprint), Is.Null);
            Assert.That(claim.IsReleased, Is.True);
        });

        AiRequestName retry = key.NameFor("a prompt");
        Assert.Multiple(() =>
        {
            Assert.That(retry.Key, Is.EqualTo(issued.Key));
            Assert.That(retry.IsRepeat, Is.False);
        });
    }

    [Test]
    public void OwnerAuthorizedWithdrawalRequiresTheLiveDispatchLease()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "video.generate");
        AiRequestName issued = key.NameFor("a prompt");
        AiRequestRecoveryLease claim = key.TryClaim(issued)!;
        key.MarkClaimDispatched(claim);
        AiPendingAttempt pending = store.PendingFor("account", "video.generate").Single();

        // Process loss releases the in-memory handle, but not the durable
        // dispatched fence. A later call without the live owner must stay
        // fail-closed until the provider outcome is known.
        claim.Dispose();
        key.WithdrawAfterNoReservation(issued);

        Assert.That(store.Find(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint), Is.Not.Null);
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
        var firstStore = new FileAiRequestRecoveryStore(_directory);
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(firstStore, () => "account"),
            operation: operation);
        string?[] parts = editTask is null ? ["prompt"] : [editTask, "prompt"];
        AiRequestName issued = first.NameFor(parts);

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
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
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => account),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("prompt");
        account = "account-b";
        AiRequestName issuedForB = key.NameFor("prompt");
        Assert.That(issuedForB.Key, Is.Not.EqualTo(issued.Key));

        key.Retire(issued);

        var accountA = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account-a"),
            operation: "image.generate");
        var accountB = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account-b"),
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
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => account),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("prompt");
        account = "account-b";

        Assert.Throws<AuthenticationRequiredException>(() =>
            key.EnterAuthenticatedScope(issued));

        var accountA = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account-a"),
            operation: "image.generate");
        Assert.That(accountA.NameFor("prompt").Key, Is.EqualTo(issued.Key));
    }

    [Test]
    public void StoreFailureDoesNotPublishAnInMemoryOutstandingName()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
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
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName first = key.NameFor("first");
        AiRequestName second = key.NameFor("second");
        key.Retire(second);

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
            operation: "image.generate");
        Assert.Multiple(() =>
        {
            Assert.That(restarted.NameFor("first").Key, Is.EqualTo(first.Key));
            Assert.That(restarted.NameFor("first").IsRepeat, Is.True);
            Assert.That(restarted.NameFor("second").Key, Is.Not.EqualTo(second.Key));
        });
    }

    [Test]
    public void RestartedKeySettlingOneDurableRequestKeepsGateAndOtherModel()
    {
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestRecoveryStore(_directory), () => "account"),
            operation: "image.generate");
        string?[] firstParts = ["p1", "", "", "", "model-a"];
        string?[] secondParts = ["p2", "", "", "", "model-b"];
        AiRequestName firstName = first.NameFor(firstParts);
        _ = first.NameFor(secondParts);

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(new FileAiRequestRecoveryStore(_directory), () => "account"),
            operation: "image.generate");
        AiRequestName materialized = restarted.NameFor(firstParts);
        restarted.Retire(materialized);

        Assert.Multiple(() =>
        {
            Assert.That(restarted.HasOutstandingName.Value, Is.True);
            Assert.That(restarted.PersistedModels(AiOperations.ImageGeneration),
                Does.Contain(new AiModelId("model-b")));
            Assert.That(materialized.Key, Is.EqualTo(firstName.Key));
        });
    }

    [Test]
    public void RetireAllRemovesEveryDurableIdentityBeforeResettingMemory()
    {
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
            operation: "image.generate");
        AiRequestName first = key.NameFor("first");
        AiRequestName second = key.NameFor("second");

        key.Retire();

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
            operation: "image.generate");
        Assert.Multiple(() =>
        {
            Assert.That(restarted.NameFor("first").Key, Is.Not.EqualTo(first.Key));
            Assert.That(restarted.NameFor("second").Key, Is.Not.EqualTo(second.Key));
        });
    }

    [Test]
    public void DurableRecoveryOpensCommandGateButOnlyExactFingerprintIsRepeat()
    {
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
            operation: "image.generate");
        AiRequestName paid = first.NameFor("paid prompt");

        var restarted = new AiRequestKey(
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
            operation: "image.generate");
        bool gateWasOpen = restarted.HasOutstandingName.Value;
        AiRequestName unrelated = restarted.NameFor("another prompt");

        Assert.Multiple(() =>
        {
            Assert.That(gateWasOpen, Is.True);
            Assert.That(unrelated.IsRepeat, Is.False);
            Assert.That(unrelated.Key, Is.Not.EqualTo(paid.Key));
        });
    }

    [Test]
    public void DispatchedRecoveryCanResumeWithTheSameLocalOwner()
    {
        var firstStore = new FileAiRequestRecoveryStore(_directory);
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(firstStore, () => "account"),
            operation: "video.generate");
        AiRequestName issued = first.NameFor("prompt");
        AiRequestRecoveryLease original = first.TryClaim(issued)!;
        first.MarkClaimDispatched(original);
        string owner = original.OwnerToken;
        original.Dispose();
        AiPendingAttempt pending = first.PendingAttempts(AiOperations.VideoGeneration).Single();

        var competingStore = new FileAiRequestRecoveryStore(_directory);
        AiRequestRecoveryLease? competing = competingStore.Claim(
            pending.AccountId, pending.Operation, pending.Fingerprint, issued.Key);
        Assert.That(competing, Is.Null);

        AiRequestName retry = first.NameFor("prompt");
        AiRequestRecoveryLease? resumed = first.TryClaim(retry);
        Assert.That(resumed, Is.Not.Null);
        Assert.That(resumed!.OwnerToken, Is.EqualTo(owner));

        first.Retire(retry);
        Assert.That(competingStore.Find(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint), Is.Null);
    }

    // A request went out without a known result, and its dialog was closed, or the AI tab reopened.
    // The new dialog's key adopts the dispatched fence at once instead of after its lifetime, while
    // another process still waits for the fence to expire.
    [Test]
    public void NewKeyInTheSameProcessTakesOverTheDispatchedClaimOfADisposedKey()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        AiRequestRecoveryContext context = RecoveryContext(store, () => "account");
        var first = new AiRequestKey(recoveryContext: context, operation: "image.generate");
        AiRequestName issued = first.NameFor("prompt");
        AiRequestRecoveryLease original = first.TryClaim(issued)!;
        first.MarkClaimDispatched(original);
        original.Dispose();
        first.Dispose();

        var second = new AiRequestKey(recoveryContext: context, operation: "image.generate");
        AiRequestName resumed = second.NameFor("prompt");
        AiRequestRecoveryLease? adopted = second.TryClaim(resumed);
        AiPendingAttempt pending = store.PendingFor("account", "image.generate").Single();

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Key, Is.EqualTo(issued.Key));
            Assert.That(resumed.IsRepeat, Is.True);
            Assert.That(adopted, Is.Not.Null);
            Assert.That(adopted?.IsDispatched, Is.True);
            Assert.That(adopted?.OwnerToken, Is.Not.EqualTo(original.OwnerToken));
            Assert.That(
                new FileAiRequestRecoveryStore(_directory).Claim(
                    pending.AccountId,
                    pending.Operation,
                    pending.Fingerprint,
                    pending.Key),
                Is.Null,
                "Another process still waits for the fence to expire.");
        });

        Assert.That(second.Retire(resumed), Is.True);
        Assert.That(store.Find(pending.AccountId, pending.Operation, pending.Fingerprint), Is.Null);
        second.Dispose();
    }

    // A reference image changed after the request went out without a known result, so the attempt
    // can never be sent again. Abandoning it gives up only the way back through the form, because the
    // job stays in the account's AI job list.
    [Test]
    public void DispatchedAttemptWhoseSourceChangedCanBeAbandoned()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        string inputs = Directory.CreateDirectory(Path.Combine(_directory, "inputs")).FullName;
        string referencePath = Path.Combine(inputs, "reference.png");
        File.WriteAllBytes(referencePath, [1, 2, 3]);
        byte[] reference = File.ReadAllBytes(referencePath);
        AiRequestRecoverySource source = FileAiRequestRecoveryStore.CreateExternalSource(
            "reference",
            referencePath,
            "reference.png",
            reference);
        AiRequestName issued = key.NameFor(
            ["prompt", AiRequestKey.FileStamp("reference.png", reference)],
            new AiRequestFormSnapshot(Prompt: "prompt"),
            [source]);
        AiRequestRecoveryLease claim = key.TryClaim(issued)!;
        key.MarkClaimDispatched(claim);
        claim.Dispose();
        AiPendingAttempt pending = store.PendingFor("account", "image.generate").Single();
        File.WriteAllBytes(referencePath, [4, 5, 6]);
        Assert.Throws<InvalidDataException>(() => key.ResolveSources(pending));

        key.Abandon(pending);

        Assert.Multiple(() =>
        {
            Assert.That(store.Find(pending.AccountId, pending.Operation, pending.Fingerprint), Is.Null);
            Assert.That(
                File.ReadAllText(Path.Combine(_directory, "ai-request-recovery-claims.json")),
                Does.Not.Contain(pending.Key));
            Assert.That(key.HasOutstandingName.Value, Is.False);
        });
        key.Dispose();
    }

    [Test]
    public void ExpiredDispatchedFenceRejectsCompetingClaimButExactOwnerCanSettle()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var firstStore = new FileAiRequestRecoveryStore(_directory, () => now);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(firstStore, () => "account"),
            operation: "video.generate");
        AiRequestName issued = key.NameFor("prompt");
        AiRequestRecoveryLease owner = key.TryClaim(issued)!;
        key.MarkClaimDispatched(owner);
        AiPendingAttempt pending = firstStore.PendingFor("account", "video.generate").Single();
        owner.Dispose();

        now = now.AddMinutes(16);
        var restartedStore = new FileAiRequestRecoveryStore(_directory, () => now);
        Assert.Throws<InvalidDataException>(() => restartedStore.Claim(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint,
            pending.Key + "-wrong"));
        AiRequestRecoveryLease adopted = restartedStore.Claim(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint,
            pending.Key)!;
        Assert.That(adopted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(adopted.IsDispatched, Is.True);
            Assert.That(adopted.OwnerToken, Is.Not.EqualTo(owner.OwnerToken));
            Assert.That(adopted.Generation, Is.EqualTo(owner.Generation));
            Assert.That(owner.Renew(), Is.False);
            Assert.That(restartedStore.TrySettle(
                pending.AccountId,
                pending.Operation,
                pending.Fingerprint,
                pending.Key,
                owner.OwnerToken,
                owner.Generation), Is.False);
            Assert.That(restartedStore.TrySettle(
                pending.AccountId,
                pending.Operation,
                pending.Fingerprint,
                pending.Key,
                adopted.OwnerToken,
                adopted.Generation + 1), Is.False);
            Assert.That(restartedStore.TrySettle(
                pending.AccountId,
                pending.Operation,
                pending.Fingerprint,
                pending.Key,
                adopted.OwnerToken,
                adopted.Generation), Is.True);
        });
        Assert.That(restartedStore.Find(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint), Is.Null);
        adopted.Dispose();
        owner.Dispose();
    }

    [Test]
    public void ReacquireFailureRollsBackStateSoDisposeTerminates()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("prompt");
        AiRequestRecoveryLease claim = key.TryClaim(issued)!;
        key.MarkClaimDispatched(claim);
        claim.Dispose();

        File.WriteAllText(Path.Combine(_directory, "ai-request-recovery-claims.json"), "not-json");
        Assert.Throws<InvalidDataException>(() => claim.Reacquire());

        Task dispose = Task.Run(claim.Dispose);
        Assert.That(dispose.Wait(TimeSpan.FromSeconds(2)), Is.True,
            "Dispose must not spin after a failed reacquire.");
    }

    [Test]
    public void DisposeWaitsUntilReacquirePublishesItsRenewalTimer()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            var store = new FileAiRequestRecoveryStore(_directory);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(store, () => "account"),
                operation: "image.generate");
            AiRequestName issued = key.NameFor("prompt");
            AiRequestRecoveryLease claim = key.TryClaim(issued)!;
            key.MarkClaimDispatched(claim);
            claim.Dispose();
            claim.BeforeReacquirePublish = () =>
            {
                entered.Set();
                release.Wait();
            };

            Task<bool> reacquire = Task.Run(claim.Reacquire);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);
            Task dispose = Task.Run(claim.Dispose);
            Assert.That(dispose.Wait(TimeSpan.FromMilliseconds(50)), Is.False,
                "Dispose must wait while timer and dispatched state are being published.");

            release.Set();
            Assert.That(reacquire.Wait(TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(reacquire.Result, Is.True);
            Assert.That(dispose.Wait(TimeSpan.FromSeconds(2)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(claim.IsReleased, Is.True);
                Assert.That(claim.Renew(), Is.False);
            });
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public void ReacquireLockFailureRollsBackStateSoDisposeTerminates()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("prompt");
        AiRequestRecoveryLease claim = key.TryClaim(issued)!;
        key.MarkClaimDispatched(claim);
        claim.Dispose();

        string lockPath = Path.Combine(_directory, "ai-request-recovery.json.lock");
        using var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<InvalidDataException>(() => claim.Reacquire());

        Task dispose = Task.Run(claim.Dispose);
        Assert.That(dispose.Wait(TimeSpan.FromSeconds(2)), Is.True,
            "Dispose must not spin after a failed lock acquisition.");
    }

    [Test]
    public void BulkSettlementCannotRemoveDispatchedRows()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "video.generate");
        AiRequestName first = key.NameFor("first");
        _ = key.NameFor("second");
        AiRequestRecoveryLease claim = key.TryClaim(first)!;
        key.MarkClaimDispatched(claim);
        AiPendingAttempt[] pending = store.PendingFor("account", "video.generate").ToArray();

        Assert.Throws<InvalidDataException>(() => store.SettleMany(
            "account",
            "video.generate",
            pending));
        Assert.That(store.PendingFor("account", "video.generate"), Has.Count.EqualTo(2));
        Assert.That(store.Find("account", "video.generate", pending.Single(item => item.Key == first.Key).Fingerprint), Is.Not.Null);
        claim.Dispose();
    }

    [Test]
    public void ExplicitAbandonAdvancesGenerationAndLeavesOtherAccountUntouched()
    {
        string? account = "account-a";
        var store = new FileAiRequestRecoveryStore(_directory);
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
        keyA.Abandon(pendingA);
        AiRequestName next = keyA.NameFor("same");

        Assert.Multiple(() =>
        {
            Assert.That(next.Key, Is.Not.EqualTo(abandoned.Key));
            Assert.That(next.IsRepeat, Is.False);
            Assert.That(store.Find("account-b", "image.generate", pendingA.Fingerprint)?.Key,
                Is.EqualTo(other.Key));
        });
    }

    [Test]
    public void AbandonTreatsAConcurrentSettlementAsIdempotentSuccess()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var otherProcess = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            seed: "stable-seed",
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("same");
        AiPendingAttempt pending = store.PendingFor("account", "image.generate").Single();
        key.BeforeAbandonPersistedRemoval = () =>
        {
            Assert.That(otherProcess.TrySettle(
                pending.AccountId,
                pending.Operation,
                pending.Fingerprint,
                pending.Key), Is.True);
        };

        Assert.DoesNotThrow(() => key.Abandon(pending));
        AiRequestName next = key.NameFor("same");
        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(next.Key, Is.Not.EqualTo(issued.Key));
            Assert.That(next.IsRepeat, Is.False);
        });
    }

    [Test]
    public void StaleRetireAndWithdrawCannotDeleteNewGeneration()
    {
        string? account = "account";
        var store = new FileAiRequestRecoveryStore(_directory);
        var first = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => account),
            operation: "image.generate");
        AiRequestName oldName = first.NameFor("same");
        var stale = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => account),
            operation: "image.generate");
        _ = stale.NameFor("same");
        AiPendingAttempt oldAttempt = store.PendingFor("account", "image.generate").Single();
        first.Abandon(oldAttempt);
        AiRequestName currentName = first.NameFor("same");

        AiRequestName materializedOld = new(oldName.Key, true);
        stale.Retire(materializedOld);
        stale.WithdrawAfterNoReservation(materializedOld);

        Assert.That(store.Find("account", "image.generate", oldAttempt.Fingerprint)?.Key,
            Is.EqualTo(currentName.Key));
    }

    [Test]
    public void RetireReportsCasFailureAndKeepsOutstandingName()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var other = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        AiRequestName issued = key.NameFor("same");
        AiPendingAttempt pending = store.PendingFor("account", "image.generate").Single();

        Assert.That(other.TrySettle(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint,
            pending.Key), Is.True);
        other.WriteOrGet(pending with { Key = "new-owner-key" });

        Assert.That(key.Retire(issued), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(store.Find("account", "image.generate", pending.Fingerprint)?.Key,
                Is.EqualTo("new-owner-key"));
        });

        AiRequestName current = key.NameFor("same");
        Assert.That(current.Key, Is.EqualTo("new-owner-key"));
        Assert.That(key.Retire(current), Is.True);
        Assert.That(key.HasOutstandingName.Value, Is.False);
    }

    [Test]
    public void WithdrawAfterNoReservationReportsCasFailureAndKeepsOutstandingName()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var other = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "video.generate");
        AiRequestName issued = key.NameFor("same");
        AiPendingAttempt pending = store.PendingFor("account", "video.generate").Single();

        Assert.That(other.TryWithdraw(
            pending.AccountId,
            pending.Operation,
            pending.Fingerprint,
            pending.Key), Is.True);
        other.WriteOrGet(pending with { Key = "new-owner-key" });

        Assert.That(key.WithdrawAfterNoReservation(issued), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(store.Find("account", "video.generate", pending.Fingerprint)?.Key,
                Is.EqualTo("new-owner-key"));
        });
    }

    [Test]
    public void BulkRetireReportsCasFailureWithoutClearingOtherOutstandingNames()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var other = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        _ = key.NameFor("first");
        _ = key.NameFor("second");
        AiPendingAttempt[] pending = store.PendingFor("account", "image.generate").ToArray();
        AiPendingAttempt replaced = pending[0];
        Assert.That(other.TrySettle(
            replaced.AccountId,
            replaced.Operation,
            replaced.Fingerprint,
            replaced.Key), Is.True);
        other.WriteOrGet(replaced with { Key = "new-owner-key" });

        Assert.That(key.Retire(), Is.False);
        Assert.That(key.HasOutstandingName.Value, Is.True);
        Assert.That(store.PendingFor("account", "image.generate"), Has.Count.EqualTo(2));
    }

    [Test]
    public void BulkRetireDoesNotDiscardUnmaterializedDurableRecovery()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        store.WriteOrGet(new AiPendingAttempt(
            "account", "image.generate", "same", "persisted-key", null,
            new AiRequestFormSnapshot(Prompt: "same")));
        var key = new AiRequestKey(
            seed: "stable-seed",
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");

        Assert.That(key.Retire(), Is.False);
        Assert.That(store.Find("account", "image.generate", "same"), Is.Not.Null);
        Assert.That(key.HasOutstandingName.Value, Is.True);
    }

    [Test]
    public void BulkRetireDoesNotSettleMaterializedSubsetOfDurableRun()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var key = new AiRequestKey(
            recoveryContext: RecoveryContext(store, () => "account"),
            operation: "image.generate");
        _ = key.NameFor("materialized");
        store.WriteOrGet(new AiPendingAttempt(
            "account", "image.generate", "not-materialized", "other-key", null,
            new AiRequestFormSnapshot(Prompt: "other")));
        AiPendingAttempt[] before = store.PendingFor("account", "image.generate").ToArray();

        Assert.That(key.Retire(), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(
                store.PendingFor("account", "image.generate")
                    .Select(static item => (item.AccountId, item.Operation, item.Fingerprint, item.Key)),
                Is.EquivalentTo(before.Select(static item =>
                    (item.AccountId, item.Operation, item.Fingerprint, item.Key))));
        });
    }

    [Test]
    public void AbandonGenerationSurvivesAProcessRestartWithTheSameSeed()
    {
        var first = new AiRequestKey(
            seed: "stable-seed",
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
            operation: "image.generate");
        AiRequestName oldName = first.NameFor("same");
        AiPendingAttempt pending = first.PendingAttempts(AiOperations.ImageGeneration).Single();
        first.Abandon(pending);

        var restarted = new AiRequestKey(
            seed: "stable-seed",
            recoveryContext: RecoveryContext(
                new FileAiRequestRecoveryStore(_directory),
                () => "account"),
            operation: "image.generate");
        AiRequestName next = restarted.NameFor("same");
        Assert.Multiple(() =>
        {
            Assert.That(next.Key, Is.Not.EqualTo(oldName.Key));
            Assert.That(next.IsRepeat, Is.False);
        });
    }

    private static AiRequestRecoveryContext RecoveryContext(
        FileAiRequestRecoveryStore store,
        Func<string?> accountProvider)
        => new(
            store,
            () => accountProvider() is { } account
                ? new AiAuthenticatedRequestIdentity(account, User: null)
                : null);
}
