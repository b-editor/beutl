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
}
