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
    public void Withdraw_KeepsARepeatThatMayNameSomethingAlreadyPaidFor()
    {
        var key = new AiRequestKey();
        key.NameFor("a prompt");
        AiRequestName repeat = key.NameFor("a prompt");
        Assert.That(repeat.IsRepeat, Is.True);

        key.Withdraw(repeat);

        Assert.Multiple(() =>
        {
            Assert.That(key.HasOutstandingName.Value, Is.True);
            Assert.That(key.NameFor("a prompt").Key, Is.EqualTo(repeat.Key));
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
        var resumed = new AiRequestKey("seed-of-the-run");

        Assert.Multiple(() =>
        {
            Assert.That(resumed.HasOutstandingName.Value, Is.True);
            Assert.That(resumed.NameFor(0, "a chunk").IsRepeat, Is.True);
            Assert.That(resumed.NameFor(1, "a chunk").IsRepeat, Is.False);
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
