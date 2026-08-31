using Beutl.Api.Services;
using Beutl.Services.AI;

namespace Beutl.UnitTests.Services.AI;

[TestFixture]
public sealed class AiRequestKeyTests
{
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
    public void Withdraw_ClosesANameTheServerNeverMadeAJobUnder()
    {
        // 名前はリクエストを出す前に配られる。契約や残高やモデルの可否で断られた
        // ときは、サーバーは名前を先に引いて「何も無い」と分かったうえで断って
        // いる——job は作られていない。その名前を抱えたままだと、実行はそれを
        // 名乗ったモデルに縛られ、作られてもいない job への道が開いたままになる。
        var key = new AiRequestKey();
        AiRequestName issued = key.NameFor("a prompt");
        Assert.That(key.HasOutstandingName.Value, Is.True);

        key.Withdraw(issued);

        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.False);
            // 取り下げたので、次は初めての依頼として扱われる——残高の確認が
            // もう一度前に立つ。
            Assert.That(key.NameFor("a prompt").IsRepeat, Is.False);
        });
    }

    [Test]
    public void Withdraw_LetsTheSameRequestGoOutUnderTheSameNameAgain()
    {
        // 予約されなかったのだから、その名前はまだ何も指していない。同じ依頼は
        // 同じ名前で出してよい——別の名前にすると、次に届いたときに新しい依頼
        // として課金される。
        var key = new AiRequestKey();
        AiRequestName issued = key.NameFor("a prompt");

        key.Withdraw(issued);

        Assert.That(key.NameFor("a prompt").Key, Is.EqualTo(issued.Key));
    }

    [Test]
    public void DispatchedExactOwnerCanWithdrawAfterAuthoritativeNoReservation()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
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
            // The ordinary withdrawal API is deliberately fail-closed once
            // dispatch has been persisted; only the owner-authorized path used
            // for an authoritative no-reservation response may clear it.
            key.Withdraw(issued);
            Assert.That(store.Find(
                pending.AccountId,
                pending.Operation,
                pending.Fingerprint), Is.Not.Null);

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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void OwnerAuthorizedWithdrawalRequiresTheLiveDispatchLease()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
    public void Withdraw_LeavesTheOtherNamesOfTheRunAlone()
    {
        var key = new AiRequestKey();
        AiRequestName first = key.NameFor(0, "a chunk");
        AiRequestName second = key.NameFor(1, "a chunk");

        key.Withdraw(second);

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
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Beutl.UnitTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string here = Path.Combine(directory, "picture.png");
            string moved = Path.Combine(directory, "moved");
            Directory.CreateDirectory(moved);
            string there = Path.Combine(moved, "picture.png");
            File.WriteAllBytes(here, [1, 2, 3]);
            string before = AiRequestKey.FileStamp(here);

            File.SetLastWriteTimeUtc(here, DateTime.UnixEpoch);
            Assert.That(AiRequestKey.FileStamp(here), Is.EqualTo(before),
                "Touching a file does not make it another request.");

            File.Copy(here, there);
            Assert.That(AiRequestKey.FileStamp(there), Is.EqualTo(before),
                "Nor does moving it.");

            File.WriteAllBytes(here, [3, 2, 1]);
            Assert.That(AiRequestKey.FileStamp(here), Is.Not.EqualTo(before),
                "Changing what is in it does.");

            string renamed = Path.Combine(directory, "another.png");
            File.WriteAllBytes(renamed, [1, 2, 3]);
            Assert.That(AiRequestKey.FileStamp(renamed), Is.Not.EqualTo(before),
                "So does the name it arrives under.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
        // seed が残っているだけでは、支払われたということにはならない——予約
        // されなかった依頼や、返金されて捨てられた依頼の seed も控えに残る。
        // それを再送として扱うと、誰も払っていない依頼が「支払い済みの回収」に
        // 化けて、残高の確認をすり抜ける。
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
        string directory = CreateTemporaryDirectory();
        try
        {
            var firstStore = new FileAiRequestRecoveryStore(directory);
            var first = new AiRequestKey(
                recoveryContext: RecoveryContext(firstStore, () => "account"),
                operation: operation);
            string?[] parts = editTask is null ? ["prompt"] : [editTask, "prompt"];
            AiRequestName issued = first.NameFor(parts);

            var restarted = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
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
                        File.ReadAllText(Path.Combine(directory, "ai-request-recovery.json")),
                        Does.Contain($"image.edit.{editTask}"));
                }
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void RetireRemovesTheIssuingAccountAfterAnAccountSwitch()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string? account = "account-a";
            var store = new FileAiRequestRecoveryStore(directory);
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
                    new FileAiRequestRecoveryStore(directory),
                    () => "account-a"),
                operation: "image.generate");
            var accountB = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account-b"),
                operation: "image.generate");
            Assert.Multiple(() =>
            {
                Assert.That(accountA.NameFor("prompt").Key, Is.Not.EqualTo(issued.Key));
                Assert.That(accountB.NameFor("prompt").Key, Is.EqualTo(issuedForB.Key));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void AccountSwitchBeforeSendFailsWithoutDeletingTheIssuedRecovery()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string? account = "account-a";
            var store = new FileAiRequestRecoveryStore(directory);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(store, () => account),
                operation: "image.generate");
            AiRequestName issued = key.NameFor("prompt");
            account = "account-b";

            Assert.Throws<AuthenticationRequiredException>(() =>
                key.EnterAuthenticatedScope(issued));

            var accountA = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account-a"),
                operation: "image.generate");
            Assert.That(accountA.NameFor("prompt").Key, Is.EqualTo(issued.Key));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void StoreFailureDoesNotPublishAnInMemoryOutstandingName()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(store, () => "account"),
                operation: "image.generate");
            string lockPath = Path.Combine(directory, "ai-request-recovery.json.lock");
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void RetireLeavesOtherDurableRequestNamesRecoverable()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(store, () => "account"),
                operation: "image.generate");
            AiRequestName first = key.NameFor("first");
            AiRequestName second = key.NameFor("second");
            key.Retire(second);

            var restarted = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account"),
                operation: "image.generate");
            Assert.Multiple(() =>
            {
                Assert.That(restarted.NameFor("first").Key, Is.EqualTo(first.Key));
                Assert.That(restarted.NameFor("first").IsRepeat, Is.True);
                Assert.That(restarted.NameFor("second").Key, Is.Not.EqualTo(second.Key));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void RestartedKeySettlingOneDurableRequestKeepsGateAndOtherModel()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var first = new AiRequestKey(
                recoveryContext: RecoveryContext(new FileAiRequestRecoveryStore(directory), () => "account"),
                operation: "image.generate");
            string?[] firstParts = ["p1", "", "", "", "model-a"];
            string?[] secondParts = ["p2", "", "", "", "model-b"];
            AiRequestName firstName = first.NameFor(firstParts);
            _ = first.NameFor(secondParts);

            var restarted = new AiRequestKey(
                recoveryContext: RecoveryContext(new FileAiRequestRecoveryStore(directory), () => "account"),
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void RetireAllRemovesEveryDurableIdentityBeforeResettingMemory()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account"),
                operation: "image.generate");
            AiRequestName first = key.NameFor("first");
            AiRequestName second = key.NameFor("second");

            key.Retire();

            var restarted = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account"),
                operation: "image.generate");
            Assert.Multiple(() =>
            {
                Assert.That(restarted.NameFor("first").Key, Is.Not.EqualTo(first.Key));
                Assert.That(restarted.NameFor("second").Key, Is.Not.EqualTo(second.Key));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DurableRecoveryOpensCommandGateButOnlyExactFingerprintIsRepeat()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var first = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account"),
                operation: "image.generate");
            AiRequestName paid = first.NameFor("paid prompt");

            var restarted = new AiRequestKey(
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DispatchedRecoveryCanResumeWithTheSameLocalOwner()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var firstStore = new FileAiRequestRecoveryStore(directory);
            var first = new AiRequestKey(
                recoveryContext: RecoveryContext(firstStore, () => "account"),
                operation: "video.generate");
            AiRequestName issued = first.NameFor("prompt");
            AiRequestRecoveryLease original = first.TryClaim(issued)!;
            first.MarkClaimDispatched(original);
            string owner = original.OwnerToken;
            original.Dispose();
            AiPendingAttempt pending = first.PendingAttempts(AiOperations.VideoGeneration).Single();

            var competingStore = new FileAiRequestRecoveryStore(directory);
            AiRequestRecoveryLease? competing = competingStore.Claim(
                pending.AccountId, pending.Operation, pending.Fingerprint, issued.Key);
            Assert.That(competing, Is.Null);
            Assert.That(competingStore.Abandon(pending), Is.False);

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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ExpiredDispatchedFenceRejectsAbandonAndCompetingClaimButExactOwnerCanSettle()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var firstStore = new FileAiRequestRecoveryStore(directory, () => now);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(firstStore, () => "account"),
                operation: "video.generate");
            AiRequestName issued = key.NameFor("prompt");
            AiRequestRecoveryLease owner = key.TryClaim(issued)!;
            key.MarkClaimDispatched(owner);
            AiPendingAttempt pending = firstStore.PendingFor("account", "video.generate").Single();
            owner.Dispose();

            now = now.AddMinutes(16);
            var restartedStore = new FileAiRequestRecoveryStore(directory, () => now);
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
                Assert.That(restartedStore.Abandon(pending), Is.False);
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ReacquireFailureRollsBackStateSoDisposeTerminates()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(store, () => "account"),
                operation: "image.generate");
            AiRequestName issued = key.NameFor("prompt");
            AiRequestRecoveryLease claim = key.TryClaim(issued)!;
            key.MarkClaimDispatched(claim);
            claim.Dispose();

            File.WriteAllText(Path.Combine(directory, "ai-request-recovery-claims.json"), "not-json");
            Assert.Throws<InvalidDataException>(() => claim.Reacquire());

            Task dispose = Task.Run(claim.Dispose);
            Assert.That(dispose.Wait(TimeSpan.FromSeconds(2)), Is.True,
                "Dispose must not spin after a failed reacquire.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DisposeWaitsUntilReacquirePublishesItsRenewalTimer()
    {
        string directory = CreateTemporaryDirectory();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
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
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ReacquireLockFailureRollsBackStateSoDisposeTerminates()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(store, () => "account"),
                operation: "image.generate");
            AiRequestName issued = key.NameFor("prompt");
            AiRequestRecoveryLease claim = key.TryClaim(issued)!;
            key.MarkClaimDispatched(claim);
            claim.Dispose();

            string lockPath = Path.Combine(directory, "ai-request-recovery.json.lock");
            using var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Assert.Throws<InvalidDataException>(() => claim.Reacquire());

            Task dispose = Task.Run(claim.Dispose);
            Assert.That(dispose.Wait(TimeSpan.FromSeconds(2)), Is.True,
                "Dispose must not spin after a failed lock acquisition.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void BulkSettlementCannotRemoveDispatchedRows()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
            var key = new AiRequestKey(
                recoveryContext: RecoveryContext(store, () => "account"),
                operation: "video.generate");
            AiRequestName first = key.NameFor("first");
            _ = key.NameFor("second");
            AiRequestRecoveryLease claim = key.TryClaim(first)!;
            key.MarkClaimDispatched(claim);
            AiPendingAttempt[] pending = store.PendingFor("account", "video.generate").ToArray();

            Assert.Throws<InvalidDataException>(() => store.SettleMany(pending));
            Assert.That(store.PendingFor("account", "video.generate"), Has.Count.EqualTo(2));
            Assert.Throws<InvalidDataException>(() => key.Abandon(pending.Single(item => item.Key == first.Key)));
            Assert.That(store.Find("account", "video.generate", pending.Single(item => item.Key == first.Key).Fingerprint), Is.Not.Null);
            claim.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ExplicitAbandonAdvancesGenerationAndLeavesOtherAccountUntouched()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string? account = "account-a";
            var store = new FileAiRequestRecoveryStore(directory);
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void AbandonTreatsAConcurrentSettlementAsIdempotentSuccess()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileAiRequestRecoveryStore(directory);
            var otherProcess = new FileAiRequestRecoveryStore(directory);
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void StaleRetireAndWithdrawCannotDeleteNewGeneration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string? account = "account";
            var store = new FileAiRequestRecoveryStore(directory);
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
            stale.Withdraw(materializedOld);

            Assert.That(store.Find("account", "image.generate", oldAttempt.Fingerprint)?.Key,
                Is.EqualTo(currentName.Key));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void AbandonGenerationSurvivesAProcessRestartWithTheSameSeed()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var first = new AiRequestKey(
                seed: "stable-seed",
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account"),
                operation: "image.generate");
            AiRequestName oldName = first.NameFor("same");
            AiPendingAttempt pending = first.PendingAttempts(AiOperations.ImageGeneration).Single();
            first.Abandon(pending);

            var restarted = new AiRequestKey(
                seed: "stable-seed",
                recoveryContext: RecoveryContext(
                    new FileAiRequestRecoveryStore(directory),
                    () => "account"),
                operation: "image.generate");
            AiRequestName next = restarted.NameFor("same");
            Assert.Multiple(() =>
            {
                Assert.That(next.Key, Is.Not.EqualTo(oldName.Key));
                Assert.That(next.IsRepeat, Is.False);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Beutl.UnitTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
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
