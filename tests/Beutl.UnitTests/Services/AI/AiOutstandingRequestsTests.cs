using Beutl.Services.AI;

namespace Beutl.UnitTests.Services.AI;

public class AiOutstandingRequestsTests
{
    [Test]
    public void TryFind_ReadsTheRequestThatWasSentLast()
    {
        // 同じ task の依頼が 2 つ未回収で残ることがある——1 枚目を X で、2 枚目を
        // Y で送って、どちらも答えが返らないまま戻ってきたとき。戻り先の一覧が
        // どちらのモデルに落ち着くかが呼び出しごとに変わると、画面が見せるものが
        // 定まらない。最後に送ったものが、画面が最後に見せていたもの。
        var requests = new AiOutstandingRequests();
        requests.Remember(new AiRequestName("upscale-first", IsRepeat: false), ["upscale", "model-x"]);
        requests.Remember(new AiRequestName("upscale-second", IsRepeat: false), ["upscale", "model-y"]);

        Assert.That(
            requests.TryFind(request => request[0] == "upscale", out string?[] found),
            Is.True);
        Assert.That(found[1], Is.EqualTo("model-y"));
    }

    [Test]
    public void TryFind_FallsBackToWhatIsLeftOnceTheNewestIsSettled()
    {
        var requests = new AiOutstandingRequests();
        var first = new AiRequestName("upscale-first", IsRepeat: false);
        var second = new AiRequestName("upscale-second", IsRepeat: false);
        requests.Remember(first, ["upscale", "model-x"]);
        requests.Remember(second, ["upscale", "model-y"]);

        requests.Forget(second);

        Assert.That(
            requests.TryFind(request => request[0] == "upscale", out string?[] found),
            Is.True);
        Assert.That(found[1], Is.EqualTo("model-x"));

        requests.Forget(first);
        Assert.Multiple(() =>
        {
            Assert.That(requests.TryFind(request => request[0] == "upscale", out _), Is.False);
            Assert.That(requests.Any(request => request[0] == "upscale"), Is.False);
            Assert.That(requests.All(), Is.Empty);
        });
    }

    [Test]
    public void Remember_SendingTheSameNameAgainLeavesOneRequestUnderIt()
    {
        // 同じ名前で送り直しても、抱えているのは 1 つの依頼。二重に数えると、
        // 決着させても片方が残り、その task はいつまでも未回収に見える。
        var requests = new AiOutstandingRequests();
        var name = new AiRequestName("upscale-first", IsRepeat: false);
        requests.Remember(name, ["upscale", "model-x"]);
        requests.Remember(name with { IsRepeat = true }, ["upscale", "model-x"]);

        Assert.That(requests.All().Count(), Is.EqualTo(1));

        requests.Forget(name);
        Assert.That(requests.All(), Is.Empty);
    }

    [Test]
    public void Remember_KeepsNothingForARequestThatWasNeverNamed()
    {
        // 名前の付かなかった依頼は、サーバーが何も作っていない。抱えると、その
        // task がいつまでも未回収に見え、一覧が取りに行けなくなる。
        var requests = new AiOutstandingRequests();

        requests.Remember(new AiRequestName(string.Empty, IsRepeat: false), ["upscale", "model-x"]);

        Assert.That(requests.All(), Is.Empty);
    }
}
