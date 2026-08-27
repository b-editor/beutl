using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiDialogWorkflowTests
{
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [AvaloniaTest]
    public void ImageSaveLease_SurvivesResultReplacement()
    {
        var owner = Ref<Bitmap>.Create(new Bitmap(2, 2));
        using Ref<Bitmap>? saveLease = AiResultImageLease.Acquire(owner);

        owner.Dispose();

        using var output = new MemoryStream();
        Assert.DoesNotThrow(() => saveLease!.Value.Save(output, EncodedImageFormat.Png));
        Assert.That(output.Length, Is.GreaterThan(0));
    }

    [AvaloniaTest]
    public async Task ImageGeneration_GeneratesAndImportsResult()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-image-dialog");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/images" => JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file")),
            "/api/contents/image-file" => ByteResponse(s_png, "image/png"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        await viewModel.AddToScene.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        string persistedElement = CoreSerializer.SerializeToJsonObject(editor.Scene.Children.Single()).ToJsonString();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
            Assert.That(persistedElement, Does.Not.Contain("image-job"));
            Assert.That(persistedElement, Does.Not.Contain("image-file"));
            Assert.That(persistedElement, Does.Not.Contain("A calm blue sky"));
        });
    }

    [AvaloniaTest]
    public async Task ImageGeneration_ARefusalBeforeAnythingIsReservedFreesTheModelAgain()
    {
        // 名前はリクエストを出す前に配られる。残高が足りずに送る前で止まった実行が
        // その名前を抱えたままだと、モデルを選び直しても最初に名乗ったモデルで
        // 送られる——画面の表示と、課金される中身が食い違う。
        await TestReset.ResetShellAsync();
        bool affordable = false;
        var sentModels = new List<string?>();
        using var handler = new StubHandler(async (request, token) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/ai-availability":
                    return JsonResponse(
                        HttpStatusCode.OK,
                        affordable ? "{ \"available\": true }" : "{ \"available\": false }");
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/capabilities":
                    return JsonResponse(HttpStatusCode.OK, ImageCapabilitiesJson());
                case "/api/v3/ai/images":
                    string body = await request.Content!.ReadAsStringAsync(token);
                    using (JsonDocument sent = JsonDocument.Parse(body))
                    {
                        sentModels.Add(
                            sent.RootElement.TryGetProperty("model", out JsonElement model)
                                ? model.GetString()
                                : null);
                    }
                    return JsonResponse(
                        HttpStatusCode.OK,
                        ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        }, handleAvailability: false);
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.ModelPicker.Options.Count == 3);
        viewModel.Prompt.Value = "A calm blue sky";
        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-1");

        await viewModel.Generate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.Error.Value,
                Is.EqualTo(Beutl.Language.Strings.AiUsageLimitExceeded));
            Assert.That(sentModels, Is.Empty, "Nothing was sent, so nothing was reserved.");
        });

        affordable = true;
        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-2");
        await viewModel.Generate.ExecuteAsync();

        Assert.That(
            sentModels,
            Is.EqualTo(new[] { "openai/gpt-image-2" }),
            "The model on screen is the model that is charged for.");
    }

    [AvaloniaTest]
    public async Task ImageGeneration_AFailureAfterTheChargeKeepsTheName()
    {
        // 課金されたあとに失敗するのは、結果を取りに行くところ。そこでの認証
        // 切れを「予約されなかった」と読んで名前を捨てると、支払い済みの絵へ
        // 戻る道が閉じる。
        await TestReset.ResetShellAsync();
        var sentKeys = new List<string?>();
        bool contentIsReachable = false;
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    sentKeys.Add(IdempotencyKeyOf(request));
                    return JsonResponse(
                        HttpStatusCode.OK,
                        ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return contentIsReachable
                        ? ByteResponse(s_png, "image/png")
                        : JsonResponse(HttpStatusCode.Unauthorized, """
                            {
                              "error_code": "authenticationIsRequired",
                              "message": "Sign in again.",
                              "documentation_url": null
                            }
                            """);
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        contentIsReachable = true;
        await viewModel.Generate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sentKeys, Has.Count.EqualTo(2));
            Assert.That(
                sentKeys[1],
                Is.EqualTo(sentKeys[0]),
                "The second attempt collects what the first one paid for.");
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
        });
    }

    [AvaloniaTest]
    public async Task ImageGeneration_FinishingAnotherRequestLeavesTheFirstNameAlone()
    {
        // A が課金されたまま応答を落とし、入力を変えた B が成功する。B の決着で
        // 実行ごと名前を捨てると、A に戻ったとき新しい名前になり、支払い済みの
        // ものをもう一度買うことになる。
        await TestReset.ResetShellAsync();
        var sentKeys = new List<(string prompt, string? key)>();
        using var handler = new StubHandler(async (request, token) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    string body = await request.Content!.ReadAsStringAsync(token);
                    using (JsonDocument sent = JsonDocument.Parse(body))
                    {
                        string prompt = sent.RootElement.GetProperty("prompt").GetString()!;
                        sentKeys.Add((prompt, IdempotencyKeyOf(request)));
                        return prompt.Contains("second", StringComparison.Ordinal)
                            ? JsonResponse(
                                HttpStatusCode.OK,
                                ImageResponseJson("image-job", "image-file"))
                            : JsonResponse(HttpStatusCode.Conflict, """
                                {
                                  "error_code": "aiRequestInProgress",
                                  "message": "The first attempt is still running.",
                                  "documentation_url": null
                                }
                                """);
                    }
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);

        viewModel.Prompt.Value = "the first picture";
        await viewModel.Generate.ExecuteAsync();
        viewModel.Prompt.Value = "the second picture";
        await viewModel.Generate.ExecuteAsync();
        viewModel.Prompt.Value = "the first picture";
        await viewModel.Generate.ExecuteAsync();

        Assert.That(sentKeys, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(
                sentKeys[2].key,
                Is.EqualTo(sentKeys[0].key),
                "Coming back to the unfinished request asks for it under its own name.");
            Assert.That(sentKeys[1].key, Is.Not.EqualTo(sentKeys[0].key));
        });
    }

    [AvaloniaTest]
    public async Task ImageGeneration_NamesARequestByTheModelOnScreen()
    {
        // モデルもその依頼の一部。X で出した A に戻るには、prompt だけでなく
        // モデルも X に戻す——戻せば同じ名前になり、支払い済みの A を取りに
        // 行ける。Y のまま出せば、それは Y への依頼として新しく課金される。
        await TestReset.ResetShellAsync();
        var sent = new List<(string Prompt, string? Model, string? Key)>();
        using var handler = new StubHandler(async (request, token) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/capabilities":
                    return JsonResponse(HttpStatusCode.OK, ImageCapabilitiesJson());
                case "/api/v3/ai/images":
                    string body = await request.Content!.ReadAsStringAsync(token);
                    using (JsonDocument document = JsonDocument.Parse(body))
                    {
                        string prompt = document.RootElement.GetProperty("prompt").GetString()!;
                        sent.Add((
                            prompt,
                            document.RootElement.TryGetProperty("model", out JsonElement model)
                                ? model.GetString()
                                : null,
                            IdempotencyKeyOf(request)));
                        return prompt.Contains("second", StringComparison.Ordinal)
                            ? JsonResponse(
                                HttpStatusCode.OK,
                                ImageResponseJson("image-job", "image-file"))
                            : JsonResponse(HttpStatusCode.Conflict, """
                                {
                                  "error_code": "aiRequestInProgress",
                                  "message": "The first attempt is still running.",
                                  "documentation_url": null
                                }
                                """);
                    }
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.ModelPicker.Options.Count == 3);

        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-1");
        viewModel.Prompt.Value = "the first picture";
        await viewModel.Generate.ExecuteAsync();

        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-2");
        viewModel.Prompt.Value = "the second picture";
        await viewModel.Generate.ExecuteAsync();

        // prompt だけ戻してモデルは Y のまま——これは Y への新しい依頼。
        viewModel.Prompt.Value = "the first picture";
        await viewModel.Generate.ExecuteAsync();

        // モデルも X に戻す。ここで初めて A と同じ依頼になる。
        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-1");
        await viewModel.Generate.ExecuteAsync();

        Assert.That(sent, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(sent[0].Model, Is.EqualTo("openai/gpt-image-1"));
            Assert.That(sent[1].Model, Is.EqualTo("openai/gpt-image-2"));
            Assert.That(
                sent[2].Model,
                Is.EqualTo("openai/gpt-image-2"),
                "The screen said this model, so this is what is asked for and charged.");
            Assert.That(sent[2].Key, Is.Not.EqualTo(sent[0].Key));
            Assert.That(
                sent[3].Key,
                Is.EqualTo(sent[0].Key),
                "Put back as it was, it asks for what that name already paid for.");
        });
    }

    [AvaloniaTest]
    public async Task ImageGeneration_LookingAtAnotherModelDoesNotRewriteTheRequest()
    {
        // モデルを見比べるあいだ、画面はそのモデルが取れる範囲へ寄せられる。
        // 寄せた先を利用者の選択として覚えてしまうと、元のモデルへ戻したときに
        // 別の依頼になり、出してある名前が指す支払い済みのものへ届かない。
        await TestReset.ResetShellAsync();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/capabilities" => JsonResponse(
                HttpStatusCode.OK,
                ImageCapabilitiesJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.ModelPicker.Options.Count == 3);

        // GPT Image-1 takes 3:2 and a transparent background; GPT Image-2 takes
        // neither.
        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-1");
        viewModel.SelectedAspectRatio.Value = viewModel.AspectRatioOptions
            .First(option => option.Value == "3:2");
        viewModel.SelectedBackground.Value = viewModel.BackgroundOptions
            .First(option => option.Value == "transparent");

        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.SelectedAspectRatio.Value.Value, Is.Not.EqualTo("3:2"),
                "This model does not take it, so it is not on offer.");
            Assert.That(
                viewModel.BackgroundOptions.Select(option => option.Value),
                Does.Not.Contain("transparent"));
        }

        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                viewModel.SelectedAspectRatio.Value.Value,
                Is.EqualTo("3:2"),
                "Back on a model that takes it, the request is the one it was.");
            Assert.That(
                viewModel.SelectedBackground.Value.Value,
                Is.EqualTo("transparent"));
        }
    }

    [AvaloniaTest]
    public async Task ImageEdit_NamesARequestByTheModelOnScreen()
    {
        // モデルもその依頼の一部。画面が X を見せているなら、送るのも課金される
        // のも X——未回収の依頼に合わせて黙って別のものを名乗ると、画面にある
        // ものとは違うモデルで課金される。
        await TestReset.ResetShellAsync();
        string sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, s_png);
        var sent = new List<(string? Model, string? Key)>();
        try
        {
            using var handler = new StubHandler(async (request, cancellationToken) =>
            {
                switch (request.RequestUri?.AbsolutePath)
                {
                    case "/api/v3/user/entitlements":
                        return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                    case "/api/v3/ai/capabilities":
                        return JsonResponse(HttpStatusCode.OK, ImageEditCapabilitiesJson());
                    case "/api/v3/ai/images/edit":
                        string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                        sent.Add((ModelOfMultipart(body), IdempotencyKeyOf(request)));
                        // 最初の 1 回は答えが返らない。名前は残り、その依頼は
                        // 未回収のまま次の送信を迎える。
                        return sent.Count == 1
                            ? throw new HttpRequestException("The connection was reset.")
                            : JsonResponse(
                                HttpStatusCode.OK,
                                ImageResponseJson("edit-job", "edit-file"));
                    case "/api/contents/edit-file":
                        return ByteResponse(s_png, "image/png");
                    default:
                        return JsonResponse(HttpStatusCode.NotFound, "{}");
                }
            });
            using var httpClient = new HttpClient(handler);
            await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
            SetAuthenticatedUser(clients, httpClient);
            using var viewModel = CreateImageEditDialog(clients);
            viewModel.SelectedTask.Value = viewModel.Tasks
                .First(option => option.Value == "upscale");
            await WaitUntilAsync(() => viewModel.ModelPicker.Options.Count == 2);
            viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
                .First(option => option.Id.Value == "topaz/gigapixel-2");
            viewModel.SourceFilePath.Value = sourcePath;

            await viewModel.Edit.ExecuteAsync();
            await viewModel.Edit.ExecuteAsync();

            Assert.That(sent, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    sent[0].Model,
                    Is.EqualTo("topaz/gigapixel-2"),
                    "The screen said this model, so this is what is asked for and charged.");
                Assert.That(
                    sent[1].Model,
                    Is.EqualTo("topaz/gigapixel-2"),
                    "Asking again does not quietly move to another model.");
                Assert.That(
                    sent[1].Key,
                    Is.EqualTo(sent[0].Key),
                    "The same request under the same name reaches what it paid for.");
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task ImageEdit_ComingBackToAnUncollectedTaskCanStillSendIt()
    {
        // 未回収の依頼を抱えたまま別の task を見て戻ってくる。戻り先の一覧を
        // 取りに行けないと、画面は何も選べないまま送信も閉じ、支払い済みの依頼が
        // 取り残される。
        await TestReset.ResetShellAsync();
        string sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, s_png);
        try
        {
            using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
            {
                "/api/v3/user/entitlements" => JsonResponse(
                    HttpStatusCode.OK,
                    EntitlementsJson()),
                "/api/v3/ai/images/edit" => JsonResponse(HttpStatusCode.Conflict, """
                    {
                      "error_code": "aiRequestInProgress",
                      "message": "The first attempt is still running.",
                      "documentation_url": null
                    }
                    """),
                _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
            });
            using var httpClient = new HttpClient(handler);
            await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
            SetAuthenticatedUser(clients, httpClient);
            using var viewModel = CreateImageEditDialog(clients);
            viewModel.SelectedTask.Value = viewModel.Tasks
                .Single(task => task.Value == "upscale");
            viewModel.SourceFilePath.Value = sourcePath;
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
                && viewModel.CanEdit.Value);

            await viewModel.Edit.ExecuteAsync();
            Assert.That(
                viewModel.Error.Value,
                Is.EqualTo(Beutl.Language.Strings.AiRequestInProgress));

            viewModel.SelectedTask.Value = viewModel.Tasks
                .Single(task => task.Value == "remove_background");
            await WaitUntilAsync(() => viewModel.ModelPicker.IsLoaded.Value);
            viewModel.SelectedTask.Value = viewModel.Tasks
                .Single(task => task.Value == "upscale");

            await WaitUntilAsync(() => viewModel.CanEdit.Value);
            Assert.That(
                viewModel.ModelPicker.IsLoaded.Value,
                Is.True,
                "The task holding an uncollected request has its own list again.");
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task Transcription_AnUncollectedRunIsNotOverwrittenWithoutAsking()
    {
        // 最初のひと切れを送ったきり何も返ってきていない実行は、見た目には何も
        // 無い——部分結果も cue も無い。それでも支払い済みかもしれない名前を
        // 抱えているので、履歴からの取り込みで断りなく消してはいけない。
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-history-guard");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/transcriptions" => JsonResponse(HttpStatusCode.Conflict, """
                {
                  "error_code": "aiRequestInProgress",
                  "message": "The first attempt is still running.",
                  "documentation_url": null
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);

        await viewModel.Transcribe.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.HasPartialResult.Value, Is.False,
                "Nothing has come back, so there is nothing to apply.");
            Assert.That(viewModel.HasOutstandingTranscriptionRequest.Value, Is.True);
        }

        viewModel.LoadHistoryResult(new AiCaptionHistoryResult(
            new AiJobId("history-job"),
            [new AiTranscriptionSegment { Start = 0, End = 1, Text = "from history" }],
            "en"));
        HeadlessTestHelpers.Settle();

        Assert.That(
            viewModel.HasPendingHistoryResult.Value,
            Is.True,
            "A run that may already have been charged for is worth asking about.");

        viewModel.ConfirmPendingHistoryResult();
        HeadlessTestHelpers.Settle();

        Assert.That(
            viewModel.HasOutstandingTranscriptionRequest.Value,
            Is.False,
            "Once it is overwritten the run is gone, and so is what it was holding.");
    }

    [AvaloniaTest]
    public async Task ImageGeneration_ShowsTheRoughPictureWhileTheModelWorks()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-image-streaming");
        // A reply that is still arriving, which is the whole point: the rough
        // version has to be on screen while the run is still going on.
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(
            "event: partial\ndata: {\"index\":0,\"image\":\""
            + Convert.ToBase64String(s_png)
            + "\"}\n\n"));
        await pipe.Writer.FlushAsync();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/contents/image-file" => ByteResponse(s_png, "image/png"),
            "/api/v3/ai/images" => EventStreamResponse(pipe.Reader.AsStream()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        Task generating = viewModel.Generate.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.PreviewImage.Value is not null);
        Assert.That(viewModel.ResultImage.Value, Is.Null,
            "A rough version is not the picture the run produced.");

        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(
            "event: result\ndata: "
            + ImageResponseJson("image-job", "image-file").ReplaceLineEndings(string.Empty)
            + "\n\n"));
        await pipe.Writer.CompleteAsync();
        await generating;
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
            // Cleared when the run ends: what stays on screen is the result.
            Assert.That(viewModel.PreviewImage.Value, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_TakesItsRangeFromTheSceneItself()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-range");
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.Zero;
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);

        Assert.That(viewModel.CanTranscribeInput.Value, Is.False,
            "A scene with no duration has nothing to transcribe.");

        editor.Scene.Duration = TimeSpan.FromSeconds(3);
        HeadlessTestHelpers.Settle();

        Assert.That(viewModel.CanTranscribeInput.Value, Is.True,
            "Retiming the scene reaches the tab without it asking for a range of its own.");
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_ComposesTheSceneInSlicesRatherThanOneLongCall()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-slices");
        var requestedDurations = new List<TimeSpan>();
        byte[]? uploaded = null;
        int transcriptionRequests = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/transcriptions":
                    Interlocked.Increment(ref transcriptionRequests);
                    uploaded = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                    return CreateTranscriptionResponse("transcription-slices");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromSeconds(12);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestedDurations.Add(duration);
            int sampleCount = Math.Max(1, (int)Math.Round(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromSeconds(12);
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);

        await viewModel.Transcribe.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(transcriptionRequests, Is.EqualTo(1),
                "Slicing is about how the scene is composed, not how much is uploaded at a time.");
            Assert.That(requestedDurations, Has.Count.GreaterThan(1));
            Assert.That(
                requestedDurations,
                Is.All.LessThanOrEqualTo(AiSubtitleDialogViewModel.SceneMixComposeSlice),
                "Asking for a whole chunk in one call is what left the editor unresponsive.");
            Assert.That(
                requestedDurations.Aggregate(TimeSpan.Zero, (total, slice) => total + slice),
                Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(ReadWaveDataLength(uploaded), Is.EqualTo(12 * 16_000 * sizeof(short)),
                "Every slice reaches the wave, so no audio is lost at a boundary.");
            Assert.That(viewModel.Error.Value, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_SendsTheSeedAsJsonAndTheReferenceAsAnUpload()
    {
        await TestReset.ResetShellAsync();
        string referencePath = Path.Combine(
            Path.GetTempPath(),
            $"beutl-ai-reference-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(referencePath, s_png);
        string? jsonBody = null;
        string? uploadBody = null;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    if (request.Content is MultipartFormDataContent)
                    {
                        uploadBody = body;
                    }
                    else
                    {
                        jsonBody = body;
                    }

                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        try
        {
            using var viewModel = CreateImageGenerationDialog(clients);
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
            viewModel.Prompt.Value = "A calm blue sky";
            viewModel.Seed.Value = 4242;

            await viewModel.Generate.ExecuteAsync();

            viewModel.AddReferenceImages([referencePath]);
            await viewModel.Generate.ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(jsonBody, Does.Contain("\"seed\":4242"),
                    "A seed is what makes a result reproducible, so it has to reach the server.");
                Assert.That(viewModel.ReferenceImages, Has.Count.EqualTo(1),
                    "The chosen picture is shown back before it is paid for.");
                Assert.That(uploadBody, Is.Not.Null,
                    "A reference turns the request into an upload.");
                Assert.That(uploadBody, Does.Contain("reference[]"));
                Assert.That(uploadBody, Does.Contain(Path.GetFileName(referencePath)));
                Assert.That(uploadBody, Does.Contain("4242"));
                Assert.That(viewModel.Error.Value, Is.Null);
            }
        }
        finally
        {
            File.Delete(referencePath);
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_ClearingTheReferenceReturnsToTheJsonRequest()
    {
        await TestReset.ResetShellAsync();
        string referencePath = Path.Combine(
            Path.GetTempPath(),
            $"beutl-ai-reference-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(referencePath, s_png);
        bool lastRequestWasUpload = true;
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    lastRequestWasUpload = request.Content is MultipartFormDataContent;
                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        try
        {
            using var viewModel = CreateImageGenerationDialog(clients);
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
            viewModel.Prompt.Value = "A calm blue sky";
            viewModel.AddReferenceImages([referencePath]);
            Assert.That(viewModel.HasReferenceImages.Value, Is.True);

            viewModel.ClearReferenceImages.Execute();
            await viewModel.Generate.ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.HasReferenceImages.Value, Is.False);
                Assert.That(viewModel.ReferenceImages, Is.Empty,
                    "The preview is released with the reference it belonged to.");
                Assert.That(lastRequestWasUpload, Is.False,
                    "With no reference the cheaper JSON request is used again.");
            }
        }
        finally
        {
            File.Delete(referencePath);
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_SendsTheSeedItWasGiven()
    {
        await TestReset.ResetShellAsync();
        string? requestBody = null;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/videos":
                    requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                    return JsonResponse(HttpStatusCode.OK, """
                        {
                          "jobId": "video-job",
                          "status": "queued"
                        }
                        """);
                case "/api/v3/ai/videos/video-job":
                    return JsonResponse(HttpStatusCode.OK, """
                        {
                          "jobId": "video-job",
                          "status": "succeeded",
                          "fileId": "video-file",
                          "url": "https://beutl.beditor.net/api/contents/video-file",
                          "error": null
                        }
                        """);
                case "/api/contents/video-file":
                    return ByteResponse([1, 2, 3, 4], "video/mp4");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        viewModel.Prompt.Value = "A slow camera pan";
        viewModel.Seed.Value = 77;

        await viewModel.Generate.ExecuteAsync();

        Assert.That(requestBody, Does.Contain("\"seed\":77"));
    }

    [AvaloniaTest]
    public async Task VideoGeneration_JobNotFoundRetiresTheCreateKeyBeforeAnotherAttempt()
    {
        await TestReset.ResetShellAsync();
        var keys = new List<string?>();
        int creates = 0;
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/videos":
                    keys.Add(IdempotencyKeyOf(request));
                    int sequence = ++creates;
                    return JsonResponse(HttpStatusCode.OK, $$"""
                        {
                          "jobId": "video-{{sequence}}",
                          "status": "queued"
                        }
                        """);
                case "/api/v3/ai/videos/video-1":
                    return JsonResponse(HttpStatusCode.NotFound, """
                        {
                          "error_code": "aiJobNotFound",
                          "message": "The job no longer exists.",
                          "documentation_url": null
                        }
                        """);
                case "/api/v3/ai/videos/video-2":
                    return JsonResponse(HttpStatusCode.OK, """
                        {
                          "jobId": "video-2",
                          "status": "failed",
                          "error": "aiProviderError"
                        }
                        """);
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        await using AiVideoGenerationDialogViewModel viewModel =
            CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        viewModel.Prompt.Value = "A slow camera pan";

        await viewModel.Generate.ExecuteAsync();
        Assert.That(
            viewModel.Error.Value,
            Is.EqualTo(Beutl.Language.Strings.AiRequestWasDeleted));
        await viewModel.Generate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.Not.EqualTo(keys[0]));
            Assert.That(
                viewModel.Error.Value,
                Is.EqualTo(Beutl.Language.Strings.AiProviderError));
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_AsksUnderOneNameForTheSameFrameUnderAnotherFileName()
    {
        // 場面から切り出したフレームは、その都度ちがう名前のファイルに落ちる。
        // サーバーはフレームを中身と種類だけで見分けるので、名前まで数えると、
        // 途切れた実行を同じ一枚で送り直しただけで別の依頼になり、支払い済みの
        // ものへ戻れないまま二度課金される。
        await TestReset.ResetShellAsync();
        string firstPath = Path.Combine(Path.GetTempPath(), $"frame-{Guid.NewGuid():N}.png");
        string secondPath = Path.Combine(Path.GetTempPath(), $"frame-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(firstPath, s_png);
        await File.WriteAllBytesAsync(secondPath, s_png);
        var keys = new List<string?>();
        bool cutFirstAttempt = true;
        try
        {
            using var handler = new StubHandler(request =>
            {
                switch (request.RequestUri?.AbsolutePath)
                {
                    case "/api/v3/user/entitlements":
                        return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                    case "/api/v3/ai/videos/frames":
                        keys.Add(IdempotencyKeyOf(request));
                        if (cutFirstAttempt)
                        {
                            cutFirstAttempt = false;
                            // 答えが返らない。作られて課金されたのかどうか、この
                            // 側からは分からない。
                            throw new HttpRequestException("The connection was reset.");
                        }

                        return JsonResponse(HttpStatusCode.OK, """
                            {
                              "jobId": "video-job",
                              "status": "queued"
                            }
                            """);
                    case "/api/v3/ai/videos/video-job":
                        return JsonResponse(HttpStatusCode.OK, """
                            {
                              "jobId": "video-job",
                              "status": "succeeded",
                              "fileId": "video-file",
                              "url": "https://beutl.beditor.net/api/contents/video-file",
                              "error": null
                            }
                            """);
                    case "/api/contents/video-file":
                        return ByteResponse([1, 2, 3, 4], "video/mp4");
                    default:
                        return JsonResponse(HttpStatusCode.NotFound, "{}");
                }
            });
            using var httpClient = new HttpClient(handler);
            await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
            SetAuthenticatedUser(clients, httpClient);
            await using AiVideoGenerationDialogViewModel viewModel =
                CreateVideoGenerationDialog(clients);
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
                && viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
            viewModel.Prompt.Value = "A slow camera pan";

            viewModel.FirstFramePath.Value = firstPath;
            await viewModel.Generate.ExecuteAsync();

            // 同じ一枚を、切り出し直したぶんだけ別の名前で。
            viewModel.FirstFramePath.Value = secondPath;
            await viewModel.Generate.ExecuteAsync();

            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(
                keys[1],
                Is.EqualTo(keys[0]),
                "The same picture is the same request, whatever the file was called.");
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [AvaloniaTest]
    public async Task VideoOptions_FollowTheChosenModel()
    {
        await TestReset.ResetShellAsync();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/capabilities" => JsonResponse(
                HttpStatusCode.OK,
                VideoCapabilitiesJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        await using AiVideoGenerationDialogViewModel viewModel =
            CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.ModelPicker.Options.Count == 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                viewModel.ResolutionOptions.Select(option => option.Value),
                Is.EqualTo(new[] { "720p", "1080p" }));
            Assert.That(
                viewModel.DurationOptions.Select(option => option.Seconds),
                Is.EqualTo(new[] { 4, 6, 8 }));
            Assert.That(viewModel.SupportsSeed.Value, Is.True);
        }

        viewModel.SelectedDuration.Value = viewModel.DurationOptions
            .First(option => option.Seconds == 4);
        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "minimax/hailuo-3");

        using (Assert.EnterMultipleScope())
        {
            // What the model renders at, not what the dialog used to offer.
            Assert.That(
                viewModel.ResolutionOptions.Select(option => option.Value),
                Is.EqualTo(new[] { "2K" }));
            Assert.That(viewModel.SelectedResolution.Value.Value, Is.EqualTo("2K"));
            // The lengths this model takes, not the three the dialog used to
            // offer: 5 and 7 were unreachable before.
            Assert.That(
                viewModel.DurationOptions.Select(option => option.Seconds),
                Is.EqualTo(new[] { 5, 6, 7, 8 }));
            // 4 seconds is gone; the nearest length it does take is 5, and
            // leaving 4 in place would be charged for and then refused.
            Assert.That(viewModel.SelectedDuration.Value.Seconds, Is.EqualTo(5));
            // A seed it cannot take is not offered, and never sent.
            Assert.That(viewModel.SupportsSeed.Value, Is.False);
            Assert.That(viewModel.Seed.Value, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task ImageBackgrounds_FollowTheChosenModel()
    {
        await TestReset.ResetShellAsync();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/capabilities" => JsonResponse(
                HttpStatusCode.OK,
                ImageCapabilitiesJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using AiImageGenerationDialogViewModel viewModel =
            CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.ModelPicker.Options.Count == 3);

        using (Assert.EnterMultipleScope())
        {
            // What GPT Image-1 publishes, in the order the server lists it.
            Assert.That(
                viewModel.BackgroundOptions.Select(option => option.Value),
                Is.EqualTo(new[] { "auto", "opaque", "transparent" }));
            Assert.That(viewModel.HasBackgroundChoice.Value, Is.True);
        }

        viewModel.SelectedBackground.Value = viewModel.BackgroundOptions
            .First(option => option.Value == "transparent");
        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "openai/gpt-image-2");

        using (Assert.EnterMultipleScope())
        {
            // GPT Image-2 fills a background in rather than cutting one out, so
            // transparent is gone and the choice falls back to the model's own.
            Assert.That(
                viewModel.BackgroundOptions.Select(option => option.Value),
                Is.EqualTo(new[] { "auto", "opaque" }));
            Assert.That(viewModel.SelectedBackground.Value.Value, Is.EqualTo("auto"));
        }

        viewModel.ModelPicker.Selected.Value = viewModel.ModelPicker.Options
            .First(option => option.Id.Value == "qwen/qwen-image-3-pro");

        using (Assert.EnterMultipleScope())
        {
            // A model that publishes no background at all leaves nothing to
            // choose, so the control is hidden and nothing is sent.
            Assert.That(
                viewModel.BackgroundOptions.Select(option => option.Value),
                Is.EqualTo(new[] { "auto" }));
            Assert.That(viewModel.HasBackgroundChoice.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task ComposedPromptLimit_DisablesImageAndVideoCommandsBeforeSubmission()
    {
        await TestReset.ResetShellAsync();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var image = CreateImageGenerationDialog(clients);
        await using var video = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => image.Usage.HasSnapshot.Value
            && video.Usage.HasSnapshot.Value
            && video.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);

        image.Prompt.Value = "subject";
        image.Style.Value = new string('s', 2_000);
        image.Exclusions.Value = new string('x', 2_000);
        video.Prompt.Value = "subject";
        video.Style.Value = new string('s', 2_000);
        video.Exclusions.Value = new string('x', 2_000);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.PromptValidationError.Value, Does.Contain("4000"));
            Assert.That(image.CanGenerate.Value, Is.False);
            Assert.That(video.PromptValidationError.Value, Does.Contain("4000"));
            Assert.That(video.CanGenerate.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task ImageEdit_UsesResultSnapshotWhenTaskChangesBeforeImport()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-image-edit-dialog");
        string sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, s_png);
        try
        {
            using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
            {
                "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
                "/api/v3/ai/images/edit" => JsonResponse(HttpStatusCode.OK, ImageResponseJson("edit-job", "edit-file")),
                "/api/contents/edit-file" => ByteResponse(s_png, "image/png"),
                _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
            });
            using var httpClient = new HttpClient(handler);
            await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
            SetAuthenticatedUser(clients, httpClient);
            using var viewModel = CreateImageEditDialog(clients, editor);
            viewModel.SourceFilePath.Value = sourcePath;
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value && viewModel.CanEdit.Value);

            await viewModel.Edit.ExecuteAsync();
            viewModel.SelectedTask.Value = viewModel.Tasks.Single(task => task.Value == "upscale");
            await viewModel.AddToScene.ExecuteAsync();
            HeadlessTestHelpers.Settle();

            string persistedElement = CoreSerializer.SerializeToJsonObject(editor.Scene.Children.Single()).ToJsonString();
            Assert.Multiple(() =>
            {
                Assert.That(persistedElement, Does.Not.Contain("edit-job"));
                Assert.That(persistedElement, Does.Not.Contain("edit-file"));
            });
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_PollsResultAndDisposeRemovesPreviewFile()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-dialog");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/videos" => JsonResponse(HttpStatusCode.OK, """
                {
                  "jobId": "video-job",
                  "status": "queued"
                }
                """),
            "/api/v3/ai/videos/video-job" => JsonResponse(HttpStatusCode.OK, """
                {
                  "jobId": "video-job",
                  "status": "succeeded",
                  "fileId": "video-file",
                  "url": "https://beutl.beditor.net/api/contents/video-file",
                  "error": null
                }
                """),
            "/api/contents/video-file" => ByteResponse([1, 2, 3, 4], "video/mp4"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        viewModel.Prompt.Value = "A slow camera pan";

        await viewModel.Generate.ExecuteAsync();

        string resultPath = viewModel.ResultVideoPath.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(resultPath, Is.Not.Null);
            Assert.That(File.Exists(resultPath), Is.True);
            Assert.That(viewModel.StatusText.Value, Is.EqualTo(Beutl.Language.Strings.AiVideoCompleted));
        });

        await viewModel.DisposeAsync();
        Assert.That(File.Exists(resultPath), Is.False);
    }

    [AvaloniaTest]
    public async Task VideoGeneration_PreservesWebmMetadataAndRetriesTransientPollingFailures()
    {
        await TestReset.ResetShellAsync();
        int polls = 0;
        var delays = new List<TimeSpan>();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/videos" => JsonResponse(HttpStatusCode.OK, """
                { "jobId": "webm-job", "status": "queued" }
                """),
            "/api/v3/ai/videos/webm-job" when Interlocked.Increment(ref polls) == 1 =>
                JsonResponse(HttpStatusCode.InternalServerError, """
                    {
                      "error_code": "aiProviderError",
                      "message": "Provider status is temporarily unavailable."
                    }
                    """),
            "/api/v3/ai/videos/webm-job" => JsonResponse(HttpStatusCode.OK, """
                {
                  "jobId": "webm-job",
                  "status": "succeeded",
                  "fileId": "webm-file",
                  "url": "https://beutl.beditor.net/api/contents/webm-file",
                  "fileName": "generated.webm",
                  "contentType": "video/webm"
                }
                """),
            "/api/contents/webm-file" => ByteResponse([1, 2, 3, 4], "video/webm"),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients);
        viewModel.PollDelayAsync = (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };
        viewModel.PollInterval = TimeSpan.FromMilliseconds(10);
        viewModel.MaximumTransientPollDelay = TimeSpan.FromMilliseconds(25);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        viewModel.Prompt.Value = "WebM output";
        await WaitUntilAsync(() => viewModel.CanGenerate.Value);

        await viewModel.Generate.ExecuteAsync();

        string resultPath = viewModel.ResultVideoPath.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(polls, Is.EqualTo(2));
            Assert.That(delays, Does.Contain(TimeSpan.FromMilliseconds(10)));
            Assert.That(resultPath, Does.EndWith(".webm"));
            Assert.That(File.Exists(resultPath), Is.True);
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(
                    File.GetUnixFileMode(resultPath),
                    Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
        }

        await viewModel.DisposeAsync();
        Assert.That(File.Exists(resultPath), Is.False);
    }

    [AvaloniaTest]
    public async Task VideoAvailability_CancelsStaleDurationCheckAndFailsClosed()
    {
        await TestReset.ResetShellAsync();
        var sixSecondCheckStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sixSecondCheckCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var durations = new List<int>();
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/ai-availability")
            {
                using JsonDocument json = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken));
                int duration = json.RootElement.GetProperty("durationSeconds").GetInt32();
                durations.Add(duration);
                if (duration == 6)
                {
                    sixSecondCheckStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    finally
                    {
                        if (cancellationToken.IsCancellationRequested)
                            sixSecondCheckCanceled.TrySetResult();
                    }
                }
                return JsonResponse(
                    duration == 8 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                    "{ \"available\": true }");
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        }, handleAvailability: false);
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        await using var viewModel = CreateVideoGenerationDialog(clients);
        await sixSecondCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SelectedDuration.Value = viewModel.DurationOptions.Single(option => option.Seconds == 4);
        await sixSecondCheckCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        viewModel.SelectedDuration.Value = viewModel.DurationOptions.Single(option => option.Seconds == 8);
        Assert.That(viewModel.EstimatedUsage.State.Value, Is.EqualTo(AiOperationAvailabilityState.Unknown));
        await Task.Delay(350);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(durations, Does.Contain(4));
            Assert.That(durations, Does.Contain(8));
            Assert.That(viewModel.EstimatedUsage.State.Value, Is.EqualTo(AiOperationAvailabilityState.Unknown));
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_ReplacingResultRemovesSupersededPreviewFile()
    {
        await TestReset.ResetShellAsync();
        int submissions = 0;
        using var handler = new StubHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (path == "/api/v3/ai/videos" && request.Method == HttpMethod.Post)
            {
                int number = Interlocked.Increment(ref submissions);
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    jobId = $"video-job-{number}",
                    status = "queued",
                }));
            }
            if (path.StartsWith("/api/v3/ai/videos/video-job-", StringComparison.Ordinal))
            {
                string number = path[(path.LastIndexOf('-') + 1)..];
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    jobId = $"video-job-{number}",
                    status = "succeeded",
                    fileId = $"video-file-{number}",
                    url = $"https://beutl.beditor.net/api/contents/video-file-{number}",
                    error = (string?)null,
                }));
            }
            if (path.StartsWith("/api/contents/video-file-", StringComparison.Ordinal))
                return ByteResponse([1, 2, 3, 4], "video/mp4");
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        viewModel.Prompt.Value = "Replace this preview";

        await viewModel.Generate.ExecuteAsync();
        string firstPath = viewModel.ResultVideoPath.Value!;
        await viewModel.Generate.ExecuteAsync();
        string secondPath = viewModel.ResultVideoPath.Value!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
            Assert.That(File.Exists(firstPath), Is.False);
            Assert.That(File.Exists(secondPath), Is.True);
        }

        await viewModel.DisposeAsync();
        Assert.That(File.Exists(secondPath), Is.False);
    }

    [AvaloniaTest]
    public async Task ProviderFailure_ShowsRefundSafeRetryMessage()
    {
        await TestReset.ResetShellAsync();
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            "/api/v3/ai/images" => JsonResponse(HttpStatusCode.InternalServerError, """
                { "error_code": "aiProviderError", "message": "Provider failed.", "documentation_url": null }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A prompt";

        await viewModel.Generate.ExecuteAsync();

        Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiProviderError));
    }

    // The three ways one run can end, and what each one means for the key that
    // named it: unknown keeps it, a settled failure spends it, a result spends it.
    [AvaloniaTest]
    public async Task ImageGeneration_RetryingAnInterruptedRunAsksForTheJobItMayHavePaidFor()
    {
        await TestReset.ResetShellAsync();
        var keys = new List<string?>();
        bool cutFirstAttempt = true;
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    keys.Add(IdempotencyKeyOf(request));
                    if (cutFirstAttempt)
                    {
                        cutFirstAttempt = false;
                        // The answer never arrives, so this client cannot know
                        // whether the picture was made and charged for.
                        throw new HttpRequestException("The connection was reset.");
                    }

                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        await viewModel.Generate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.EqualTo(keys[0]),
                "The second ask is for the job the first may already have paid for.");
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_AskingAgainAfterAPictureArrivesIsANewRequest()
    {
        await TestReset.ResetShellAsync();
        var keys = new List<string?>();
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    keys.Add(IdempotencyKeyOf(request));
                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        await viewModel.Generate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            // Repeating the key would hand back the first picture; asking for
            // the same prompt again is asking for another picture.
            Assert.That(keys[1], Is.Not.EqualTo(keys[0]));
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_DoesNotRetryUnderAKeyTheServerHasSettledAsFailed()
    {
        await TestReset.ResetShellAsync();
        var keys = new List<string?>();
        bool failFirstAttempt = true;
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    keys.Add(IdempotencyKeyOf(request));
                    if (failFirstAttempt)
                    {
                        failFirstAttempt = false;
                        // Failed and refunded server-side. The job is settled,
                        // so its key can only ever answer with that failure.
                        return JsonResponse(HttpStatusCode.InternalServerError, """
                            { "error_code": "aiProviderError", "message": "Provider failed." }
                            """);
                    }

                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiProviderError));

        await viewModel.Generate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.Not.EqualTo(keys[0]));
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_CollectsAPaidPictureEvenWithNothingLeftToSpend()
    {
        await TestReset.ResetShellAsync();
        var keys = new List<string?>();
        bool cutFirstAttempt = true;
        bool anythingLeftToSpend = true;
        using var handler = new StubHandler(
            (request, _) =>
            {
                switch (request.RequestUri?.AbsolutePath)
                {
                    case "/api/v3/user/entitlements":
                        return Task.FromResult(JsonResponse(HttpStatusCode.OK, EntitlementsJson()));
                    case "/api/v3/user/ai-availability":
                        return Task.FromResult(JsonResponse(
                            HttpStatusCode.OK,
                            anythingLeftToSpend
                                ? "{ \"available\": true }"
                                : "{ \"available\": false }"));
                    case "/api/v3/ai/images":
                        keys.Add(IdempotencyKeyOf(request));
                        if (cutFirstAttempt)
                        {
                            cutFirstAttempt = false;
                            // The picture was made and charged for; only the
                            // answer went missing, and it took the last of the
                            // allowance with it.
                            anythingLeftToSpend = false;
                            throw new HttpRequestException("The connection was reset.");
                        }

                        return Task.FromResult(JsonResponse(
                            HttpStatusCode.OK,
                            ImageResponseJson("image-job", "image-file")));
                    case "/api/contents/image-file":
                        return Task.FromResult(ByteResponse(s_png, "image/png"));
                    default:
                        return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{}"));
                }
            },
            handleAvailability: false);
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        await viewModel.Generate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            // The server hands back the job this name already made before it
            // looks at the balance, so checking the balance here would refuse to
            // collect the very picture that emptied it.
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.EqualTo(keys[0]));
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
            Assert.That(viewModel.Error.Value, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_AsksUnderANewNameOnceTheJobItNamedIsGone()
    {
        await TestReset.ResetShellAsync();
        var keys = new List<string?>();
        bool reportDeleted = true;
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    keys.Add(IdempotencyKeyOf(request));
                    if (reportDeleted)
                    {
                        reportDeleted = false;
                        // The job that key created was deleted, so the key can
                        // only ever answer with this.
                        return JsonResponse(HttpStatusCode.Conflict, """
                            { "error_code": "aiRequestWasDeleted", "message": "Gone." }
                            """);
                    }

                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiRequestWasDeleted));

        await viewModel.Generate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.Not.EqualTo(keys[0]),
                "A key whose job is gone would answer with that forever.");
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_KeepsTheNameOfAJobTheServerIsStillWorkingOn()
    {
        await TestReset.ResetShellAsync();
        var keys = new List<string?>();
        bool reportInProgress = true;
        using var handler = new StubHandler(request =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    keys.Add(IdempotencyKeyOf(request));
                    if (reportInProgress)
                    {
                        reportInProgress = false;
                        // The first attempt is still running and already paid
                        // for; its key is the only way back to it.
                        return JsonResponse(HttpStatusCode.Conflict, """
                            { "error_code": "aiRequestInProgress", "message": "Still running." }
                            """);
                    }

                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "A calm blue sky";

        await viewModel.Generate.ExecuteAsync();
        Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiRequestInProgress));

        await viewModel.Generate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.EqualTo(keys[0]),
                "Asking again for the running job is how its result is recovered.");
            Assert.That(viewModel.ResultImage.Value, Is.Not.Null);
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_SendsEveryReferenceAndStopsAtWhatTheModelTakes()
    {
        await TestReset.ResetShellAsync();
        string[] referencePaths = Enumerable.Range(0, 3)
            .Select(index => Path.Combine(
                Path.GetTempPath(),
                $"beutl-ai-reference-{index}-{Guid.NewGuid():N}.png"))
            .ToArray();
        foreach (string path in referencePaths)
            await File.WriteAllBytesAsync(path, s_png);

        string? uploadBody = null;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/images":
                    uploadBody = request.Content is null
                        ? null
                        : await request.Content.ReadAsStringAsync(cancellationToken);
                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("image-job", "image-file"));
                case "/api/contents/image-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        try
        {
            using var viewModel = CreateImageGenerationDialog(clients);
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
            viewModel.Prompt.Value = "A calm blue sky";
            viewModel.MaxReferenceImages.Value = 2;

            viewModel.AddReferenceImages(referencePaths);
            await viewModel.Generate.ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                // A model that takes two is offered two, not refused after the
                // usage has been reserved for three.
                Assert.That(viewModel.ReferenceImages, Has.Count.EqualTo(2));
                Assert.That(viewModel.CanAddReferenceImage.Value, Is.False);
                Assert.That(uploadBody, Does.Contain(Path.GetFileName(referencePaths[0])));
                Assert.That(uploadBody, Does.Contain(Path.GetFileName(referencePaths[1])));
                Assert.That(uploadBody, Does.Not.Contain(Path.GetFileName(referencePaths[2])));
                Assert.That(viewModel.Error.Value, Is.Null);
            }
        }
        finally
        {
            foreach (string path in referencePaths)
                File.Delete(path);
        }
    }

    [AvaloniaTest]
    public async Task ClosingImageTool_CancelsInFlightRequestWithoutPostDisposeMutation()
    {
        await TestReset.ResetShellAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/images")
            {
                requestStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "Cancel this request";

        Task operation = viewModel.Generate.ExecuteAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Dispose();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await operation;
        await viewModel.DisposeAsync();
    }

    [AvaloniaTest]
    public async Task DisposingImageDialog_AwaitsCancellationIgnoringRequestAndRejectsLateResult()
    {
        await TestReset.ResetShellAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int contentRequests = 0;
        using var handler = new StubHandler(async (request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/images")
            {
                requestStarted.TrySetResult();
                await releaseRequest.Task;
                return JsonResponse(HttpStatusCode.OK, ImageResponseJson("late-job", "late-file"));
            }
            if (request.RequestUri?.AbsolutePath == "/api/contents/late-file")
            {
                Interlocked.Increment(ref contentRequests);
                return ByteResponse(s_png, "image/png");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateImageGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.Prompt.Value = "Do not publish";
        int resultPublications = 0;
        using IDisposable publication = viewModel.ResultImage
            .Where(image => image is not null)
            .Subscribe(_ => resultPublications++);

        Task operation = viewModel.Generate.ExecuteAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = viewModel.DisposeAsync().AsTask();

        Assert.That(disposal.IsCompleted, Is.False);
        releaseRequest.TrySetResult();
        await operation;
        await disposal;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resultPublications, Is.Zero);
            Assert.That(contentRequests, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task DisposingImageEditDialog_AwaitsOperationAndRejectsLateResult()
    {
        await TestReset.ResetShellAsync();
        string sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(sourcePath, s_png);
        try
        {
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int contentRequests = 0;
            using var handler = new StubHandler(async (request, _) =>
            {
                if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                if (request.RequestUri?.AbsolutePath == "/api/v3/ai/images/edit")
                {
                    requestStarted.TrySetResult();
                    await releaseRequest.Task;
                    return JsonResponse(HttpStatusCode.OK, ImageResponseJson("late-edit", "late-edit-file"));
                }
                if (request.RequestUri?.AbsolutePath == "/api/contents/late-edit-file")
                {
                    Interlocked.Increment(ref contentRequests);
                    return ByteResponse(s_png, "image/png");
                }

                return JsonResponse(HttpStatusCode.NotFound, "{}");
            });
            using var httpClient = new HttpClient(handler);
            await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
            SetAuthenticatedUser(clients, httpClient);
            var viewModel = CreateImageEditDialog(clients);
            viewModel.SourceFilePath.Value = sourcePath;
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value && viewModel.CanEdit.Value);
            int resultPublications = 0;
            using IDisposable publication = viewModel.ResultImage
                .Where(image => image is not null)
                .Subscribe(_ => resultPublications++);

            Task operation = viewModel.Edit.ExecuteAsync();
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task disposal = viewModel.DisposeAsync().AsTask();

            Assert.That(disposal.IsCompleted, Is.False);
            releaseRequest.TrySetResult();
            await operation;
            await disposal;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resultPublications, Is.Zero);
                Assert.That(contentRequests, Is.Zero);
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task DisposingVideoDialog_CancelsSubmissionAndLeavesNoPreviewFile()
    {
        await TestReset.ResetShellAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string resultDirectory = AiTemporaryFileStore.GetCategoryDirectory("results");
        string[] filesBefore = Directory.Exists(resultDirectory)
            ? Directory.GetFiles(resultDirectory, "ai-video-*")
            : [];
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/videos")
            {
                requestStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.EstimatedUsage.State.Value == AiOperationAvailabilityState.Available);
        viewModel.Prompt.Value = "Cancel this video";

        Task operation = viewModel.Generate.ExecuteAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = viewModel.DisposeAsync().AsTask();

        await operation;
        await disposal;
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            Directory.Exists(resultDirectory)
                ? Directory.GetFiles(resultDirectory, "ai-video-*")
                : [],
            Is.EqualTo(filesBefore));
    }

    [AvaloniaTest]
    public async Task DisposingVideoDialog_DuringBlockedFrameRenderPublishesNoFileOrPreview()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-frame-capture-disposal");
        var renderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRender = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        string inputDirectory = AiTemporaryFileStore.GetCategoryDirectory("inputs");
        string[] filesBefore = Directory.Exists(inputDirectory)
            ? Directory.GetFiles(inputDirectory, "frame-*.png")
            : [];
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        var viewModel = CreateVideoGenerationDialog(clients, editor);
        viewModel.CurrentFrameRenderer = async _ =>
        {
            renderStarted.TrySetResult();
            return await releaseRender.Task;
        };

        Task operation = viewModel.CaptureCurrentFrame.ExecuteAsync();
        await renderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int publishedFramePaths = 0;
        using IDisposable publication = viewModel.FirstFramePath
            .Where(path => path is not null)
            .Subscribe(_ => publishedFramePaths++);
        Task disposal = viewModel.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);
        releaseRender.TrySetResult(new Bitmap(2, 2));
        await operation;
        await disposal;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(publishedFramePaths, Is.Zero);
            Assert.That(
                Directory.Exists(inputDirectory)
                    ? Directory.GetFiles(inputDirectory, "frame-*.png")
                    : [],
                Is.EqualTo(filesBefore));
        }
    }

    [AvaloniaTest]
    public async Task CapturingOrClearingFrame_RemovesSupersededTemporaryInput()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-frame-capture-replacement");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        await using var viewModel = CreateVideoGenerationDialog(clients, editor);
        viewModel.CurrentFrameRenderer = _ => Task.FromResult(new Bitmap(2, 2));

        await viewModel.CaptureCurrentFrame.ExecuteAsync();
        string firstPath = viewModel.FirstFramePath.Value!;
        await viewModel.CaptureCurrentFrame.ExecuteAsync();
        string secondPath = viewModel.FirstFramePath.Value!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
            Assert.That(File.Exists(firstPath), Is.False);
            Assert.That(File.Exists(secondPath), Is.True);
        }

        viewModel.ClearFirstFrame.Execute();
        Assert.That(File.Exists(secondPath), Is.False);
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_BatchesAtomicallyAndImportsTranslatedCaptions()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation");
        int translationRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using JsonDocument json = JsonDocument.Parse(body);
                object[] segments = json.RootElement.GetProperty("segments")
                    .EnumerateArray()
                    .Select(segment => (object)new
                    {
                        id = segment.GetProperty("id").GetString(),
                        text = "T-" + segment.GetProperty("text").GetString(),
                    })
                    .ToArray();
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    jobId = $"translation-{requestNumber}",
                    segments,
                }));
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value = Enumerable.Range(0, 201)
            .Select(index => new AiTranscriptionSegment
            {
                Start = index * 2,
                End = index * 2 + 1,
                Text = $"line-{index}",
            })
            .ToArray();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);

        await viewModel.Translate.ExecuteAsync();
        await viewModel.AddToScene.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues, Has.Count.EqualTo(201));
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-line-0"));
            Assert.That(editor.Scene.Children, Has.Count.EqualTo(201));
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_SecondBatchFailureKeepsPaidPartialAndResumesWithoutRebilling()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation-failure");
        int translationRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                return requestNumber is 1 or 3
                    ? CreateTranslationResponse(request, "translation-first")
                    : JsonResponse(HttpStatusCode.InternalServerError, """
                        {
                          "error_code": "aiProviderError",
                          "message": "Provider failed.",
                          "documentation_url": null
                        }
                        """);
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value = CreateTranslationBatchSegments();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);
        string[] originalTexts = viewModel.Cues.Select(cue => cue.Text).ToArray();

        await viewModel.Translate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(originalTexts));
            Assert.That(viewModel.IsTranslating.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiProviderError));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
            Assert.That(viewModel.PartialResultMessage.Value, Does.Contain("1"));
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-line-0"));
            Assert.That(viewModel.Cues[^1].Text, Is.EqualTo("line-200"));
        });

        viewModel.RefreshAvailability();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);
        await viewModel.Translate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(3),
                "The completed first batch must not be submitted and billed again.");
            Assert.That(viewModel.Cues.All(cue => cue.Text.StartsWith("T-", StringComparison.Ordinal)), Is.True);
            Assert.That(viewModel.HasPartialResult.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_PartialRunKeepsItsModelAfterRemovalAndPickerChange()
    {
        await TestReset.ResetShellAsync();
        bool firstModelRemoved = false;
        var sentModels = new List<string?>();
        int translationRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/capabilities")
                return JsonResponse(HttpStatusCode.OK, CaptionCapabilitiesJson(firstModelRemoved));
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using JsonDocument json = JsonDocument.Parse(body);
                sentModels.Add(json.RootElement.TryGetProperty("model", out JsonElement model)
                    ? model.GetString()
                    : null);
                int requestNumber = ++translationRequests;
                return requestNumber is 1 or 3
                    ? CreateTranslationResponse(request, $"translation-{requestNumber}")
                    : JsonResponse(HttpStatusCode.InternalServerError, """
                        {
                          "error_code": "aiProviderError",
                          "message": "Provider failed.",
                          "documentation_url": null
                        }
                        """);
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.TranslationModelPicker.Options.Count == 2);
        viewModel.ResultSegments.Value = CreateTranslationBatchSegments();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);

        await viewModel.Translate.ExecuteAsync();
        viewModel.ApplyPartialResult.Execute();

        firstModelRemoved = true;
        IAiModelCatalogService catalog = clients.GetResource<IAiModelCatalogService>();
        catalog.Invalidate();
        await viewModel.TranslationModelPicker.LoadAsync(
            AiOperations.CaptionTranslation,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                viewModel.TranslationModelPicker.SelectedModel,
                Is.EqualTo(new AiModelId("translation-a")));
            Assert.That(
                viewModel.TranslationModelPicker.Selected.Value!.IsAvailable,
                Is.False,
                "The removed model stays visible only because the partial run still owns it.");
        }
        viewModel.TranslationModelPicker.Selected.Value =
            viewModel.TranslationModelPicker.Options.First(option =>
                option.Id == new AiModelId("translation-b"));

        viewModel.RefreshAvailability();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);
        await viewModel.Translate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(translationRequests, Is.EqualTo(3));
            Assert.That(sentModels, Is.EqualTo(new[]
            {
                "translation-a",
                "translation-a",
                "translation-a",
            }));
            Assert.That(viewModel.HasPartialResult.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_SecondBatchCancellationPreservesOriginalCues()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation-cancel");
        int translationRequests = 0;
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                if (requestNumber == 1)
                    return CreateTranslationResponse(request, "translation-first");

                secondRequestStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value = CreateTranslationBatchSegments();
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);
        string[] originalTexts = viewModel.Cues.Select(cue => cue.Text).ToArray();

        Task operation = viewModel.Translate.ExecuteAsync();
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        GetLifetimeCancellationSource(viewModel).Cancel();
        await operation;
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(originalTexts));
            Assert.That(viewModel.IsTranslating.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-line-0"));
            Assert.That(viewModel.Cues[^1].Text, Is.EqualTo("line-200"));
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_PaidPartialSurvivesDialogCloseAndStillResumes()
    {
        await TestReset.ResetShellAsync();
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "translation-restart-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
        IObservable<CaptionDraftScope?> draftScopes =
            Observable.Return<CaptionDraftScope?>(draftScope);
        int translationRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                int requestNumber = Interlocked.Increment(ref translationRequests);
                return requestNumber is 1 or 3
                    ? CreateTranslationResponse(request, $"translation-{requestNumber}")
                    : JsonResponse(HttpStatusCode.InternalServerError, """
                        {
                          "error_code": "aiProviderError",
                          "message": "Provider failed.",
                          "documentation_url": null
                        }
                        """);
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);

        using (var firstDialog = CreateSubtitleDialog(
                   clients,
                   draftStore: draftStore,
                   draftScopes: draftScopes))
        {
            await WaitUntilAsync(() => firstDialog.Usage.HasSnapshot.Value);
            firstDialog.ResultSegments.Value = CreateTranslationBatchSegments();
            await WaitUntilAsync(() => firstDialog.CanTranslate.Value);
            await firstDialog.Translate.ExecuteAsync();
            Assert.That(firstDialog.HasPartialResult.Value, Is.True);
        }
        AssertStoredCaptionDraftJob(draftStore, draftScope, "translation-1");

        using var restoredDialog = CreateSubtitleDialog(
            clients,
            draftStore: draftStore,
            draftScopes: draftScopes);
        await WaitUntilAsync(() => restoredDialog.Usage.HasSnapshot.Value);
        Assert.That(restoredDialog.HasPartialResult.Value, Is.True);
        restoredDialog.ApplyPartialResult.Execute();
        await WaitUntilAsync(() => restoredDialog.CanTranslate.Value);
        await restoredDialog.Translate.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(translationRequests, Is.EqualTo(3));
            Assert.That(restoredDialog.Cues, Has.Count.EqualTo(201));
            Assert.That(
                restoredDialog.Cues.All(cue => cue.Text.StartsWith("T-", StringComparison.Ordinal)),
                Is.True);
            Assert.That(restoredDialog.HasPartialResult.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_SaveFailureAfterPaidResponsePreservesSeedForRestart()
    {
        await TestReset.ResetShellAsync();
        var draftStore = new FailingCaptionDraftStore(failOnSave: 2);
        CaptionDraftScope scope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
        IObservable<CaptionDraftScope?> scopes = Observable.Return<CaptionDraftScope?>(scope);
        var keys = new List<string?>();
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                keys.Add(IdempotencyKeyOf(request));
                return CreateTranslationResponse(request, "translation-paid");
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);

        using (var first = CreateSubtitleDialog(clients, draftStore: draftStore, draftScopes: scopes))
        {
            await WaitUntilAsync(() => first.Usage.HasSnapshot.Value);
            first.ResultSegments.Value =
            [new AiTranscriptionSegment { Start = 0, End = 1, Text = "paid" }];
            await WaitUntilAsync(() => first.CanTranslate.Value);
            await first.Translate.ExecuteAsync();
            Assert.That(
                first.Error.Value,
                Is.EqualTo(Beutl.Language.Strings.AiSubtitle_RunCannotBeRecorded));
            Assert.That(first.HasPartialResult.Value, Is.True);
        }

        draftStore.FailOnSave = null;
        using var restored = CreateSubtitleDialog(clients, draftStore: draftStore, draftScopes: scopes);
        await WaitUntilAsync(() => restored.Usage.HasSnapshot.Value && restored.Cues.Count == 1);
        restored.RefreshAvailability();
        await WaitUntilAsync(() => restored.CanTranslate.Value);
        await restored.Translate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.EqualTo(keys[0]));
            Assert.That(restored.Cues.Single().Text, Is.EqualTo("T-paid"));
            Assert.That(restored.HasPartialResult.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_EditDuringDeferredResponsePreservesUserRevisionAndJobMetadata()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-translation-stale-response");
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "translation-stale-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), editor.Scene.Id);
        var translationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                translationStarted.TrySetResult();
                await releaseTranslation.Task.WaitAsync(cancellationToken);
                return CreateTranslationResponse(request, "translation-stale");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(
            clients,
            editor,
            draftStore,
            Observable.Return<CaptionDraftScope?>(draftScope));
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 1, Text = "original caption" },
        ];
        await WaitUntilAsync(() => viewModel.CanTranslate.Value);

        Task operation = viewModel.Translate.ExecuteAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Cues[0].Text = "user edit while translating";
        releaseTranslation.TrySetResult();
        await operation;
        AssertStoredCaptionDraftJob(draftStore, draftScope, "translation-stale");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Cues, Has.Count.EqualTo(1));
            Assert.That(viewModel.Cues[0].Text, Is.EqualTo("user edit while translating"));
            Assert.That(viewModel.IsTranslating.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(viewModel.HasPartialResult.Value, Is.True,
                "A paid response must remain recoverable when the editor revision changes.");
        }

        viewModel.ApplyPartialResult.Execute();
        Assert.That(viewModel.Cues[0].Text, Is.EqualTo("T-original caption"));
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_ResponseLossSurvivesCueEditAnotherRunAndRestart()
    {
        await TestReset.ResetShellAsync();
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "translation-ledger-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
        IObservable<CaptionDraftScope?> scopes = Observable.Return<CaptionDraftScope?>(draftScope);
        var keys = new List<string?>();
        int requestCount = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                keys.Add(IdempotencyKeyOf(request));
                int sequence = ++requestCount;
                if (sequence == 1)
                {
                    return JsonResponse(HttpStatusCode.ServiceUnavailable, """
                        {
                          "error_code": "aiResultUnavailable",
                          "message": "The paid result is temporarily unavailable.",
                          "documentation_url": null
                        }
                        """);
                }
                return CreateTranslationResponse(request, $"translation-{sequence}");
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);

        using (var firstDialog = CreateSubtitleDialog(
                   clients,
                   draftStore: draftStore,
                   draftScopes: scopes))
        {
            await WaitUntilAsync(() => firstDialog.Usage.HasSnapshot.Value);
            firstDialog.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 0, End = 1, Text = "A" },
            ];
            await WaitUntilAsync(() => firstDialog.CanTranslate.Value);

            await firstDialog.Translate.ExecuteAsync();
            Assert.That(firstDialog.HasOutstandingTranslationRequest.Value, Is.True);

            firstDialog.Cues[0].Text = "B";
            await WaitUntilAsync(() => firstDialog.CanTranslate.Value);
            await firstDialog.Translate.ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(requestCount, Is.EqualTo(2));
                Assert.That(keys[1], Is.Not.EqualTo(keys[0]));
                Assert.That(firstDialog.Cues.Single().Text, Is.EqualTo("T-B"));
                Assert.That(firstDialog.HasOutstandingTranslationRequest.Value, Is.True,
                    "Settling B must leave A's paid recovery visible.");
            }
        }

        Assert.That(draftStore.TryOpen(draftScope, out ICaptionDraftSession? storedSession), Is.True);
        using (storedSession)
        {
            CaptionDraftEntry stored = storedSession!.Read().Entry!;
            Assert.Multiple(() =>
            {
                Assert.That(stored.Draft.TranslationResume!.SourceCues.Single().Text, Is.EqualTo("A"));
                Assert.That(stored.Draft.TranslationResume.RequestKeyNamePending, Is.True);
            });
        }

        using var restoredDialog = CreateSubtitleDialog(
            clients,
            draftStore: draftStore,
            draftScopes: scopes);
        await WaitUntilAsync(() => restoredDialog.Usage.HasSnapshot.Value);
        await WaitUntilAsync(() => restoredDialog.Cues.Count == 1);
        await WaitUntilAsync(() => restoredDialog.TranslationModelPicker.IsLoaded.Value);
        restoredDialog.RefreshAvailability();
        await WaitUntilAsync(() => restoredDialog.CanTranslate.Value);
        Assert.That(restoredDialog.Cues.Single().Text, Is.EqualTo("A"));

        await restoredDialog.Translate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestCount, Is.EqualTo(3));
            Assert.That(keys[2], Is.EqualTo(keys[0]),
                "Restart recovery must ask for A under the key that may already have paid for it.");
            Assert.That(restoredDialog.Cues.Single().Text, Is.EqualTo("T-A"));
            Assert.That(restoredDialog.HasOutstandingTranslationRequest.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task SubtitleTranslation_CaptionImportKeepsPaidRecoveryAcrossRestart()
    {
        await TestReset.ResetShellAsync();
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "translation-import-ledger-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
        IObservable<CaptionDraftScope?> scopes = Observable.Return<CaptionDraftScope?>(draftScope);
        var keys = new List<string?>();
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/translations")
            {
                keys.Add(IdempotencyKeyOf(request));
                return keys.Count == 1
                    ? JsonResponse(HttpStatusCode.ServiceUnavailable, """
                        {
                          "error_code": "aiResultUnavailable",
                          "message": "The paid result is temporarily unavailable.",
                          "documentation_url": null
                        }
                        """)
                    : CreateTranslationResponse(request, "translation-recovered");
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);

        using (var firstDialog = CreateSubtitleDialog(
                   clients,
                   draftStore: draftStore,
                   draftScopes: scopes))
        {
            await WaitUntilAsync(() => firstDialog.Usage.HasSnapshot.Value);
            firstDialog.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 0, End = 1, Text = "paid A" },
            ];
            await WaitUntilAsync(() => firstDialog.CanTranslate.Value);
            await firstDialog.Translate.ExecuteAsync();

            bool imported = firstDialog.ImportCaptionBytes(Encoding.UTF8.GetBytes("""
                1
                00:00:00,000 --> 00:00:01,000
                imported B

                """), CaptionFormats.Srt);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(imported, Is.True);
                Assert.That(firstDialog.Cues.Single().Text, Is.EqualTo("imported B"));
                Assert.That(firstDialog.HasOutstandingTranslationRequest.Value, Is.True);
            }
        }

        using var restoredDialog = CreateSubtitleDialog(
            clients,
            draftStore: draftStore,
            draftScopes: scopes);
        await WaitUntilAsync(() => restoredDialog.Usage.HasSnapshot.Value
            && restoredDialog.Cues.Count == 1
            && restoredDialog.TranslationModelPicker.IsLoaded.Value);
        restoredDialog.RefreshAvailability();
        await WaitUntilAsync(() => restoredDialog.CanTranslate.Value);
        Assert.That(restoredDialog.Cues.Single().Text, Is.EqualTo("paid A"));

        await restoredDialog.Translate.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.EqualTo(keys[0]));
            Assert.That(restoredDialog.Cues.Single().Text, Is.EqualTo("T-paid A"));
        }
    }

    [AvaloniaTest]
    public async Task SourceFileTranscription_DeferredPaidResultPersistsResponseJobMetadata()
    {
        await TestReset.ResetShellAsync();
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "source-transcription-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), Guid.NewGuid());
        var transcriptionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranscription = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int transcriptionRequests = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                Interlocked.Increment(ref transcriptionRequests);
                transcriptionStarted.TrySetResult();
                await releaseTranscription.Task.WaitAsync(cancellationToken);
                return CreateTranscriptionResponse("source-transcription-job");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(
            clients,
            draftStore: draftStore,
            draftScopes: Observable.Return<CaptionDraftScope?>(draftScope));
        string sourcePath = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "source-transcription.wav");
        WritePcmWave(sourcePath, sampleRate: 16_000, sampleCount: 16_000);
        try
        {
            await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value);
            var originalSource = new AudioSourceItem(
                "Source audio",
                sourcePath,
                TimeSpan.FromSeconds(1));
            viewModel.SelectedAudioSource.Value = originalSource;
            await WaitUntilAsync(() => viewModel.CanTranscribe.Value);

            Task operation = viewModel.Transcribe.ExecuteAsync();
            await transcriptionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.SelectedAudioSource.Value = new AudioSourceItem(
                "Other audio",
                Path.Combine(BeutlHomeIsolation.CurrentHome!, "other.wav"),
                TimeSpan.FromSeconds(1));
            viewModel.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 0, End = 1, Text = "user edit" },
            ];
            releaseTranscription.TrySetResult();
            await operation;

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Cues.Single().Text, Is.EqualTo("user edit"));
                Assert.That(viewModel.HasPartialResult.Value, Is.True);
            });
            AssertStoredCaptionDraftJob(
                draftStore,
                draftScope,
                "source-transcription-job");

            viewModel.SelectedAudioSource.Value = originalSource;
            viewModel.RefreshAvailability();
            await WaitUntilAsync(() => viewModel.CanTranscribe.Value);
            await viewModel.Transcribe.ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(transcriptionRequests, Is.EqualTo(1),
                    "A completed source transcription must apply without another HTTP request.");
                Assert.That(viewModel.Cues.Single().Text, Is.EqualTo("new caption"));
                Assert.That(viewModel.HasPartialResult.Value, Is.False);
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task SourceFileTranscription_SaveFailureAfterFinalResponsePreservesSeedForRestart()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-source-save-failure");
        var draftStore = new FailingCaptionDraftStore(failOnSave: 2);
        CaptionDraftScope scope = new("user-a", Guid.NewGuid(), editor.Scene.Id);
        IObservable<CaptionDraftScope?> scopes = Observable.Return<CaptionDraftScope?>(scope);
        string sourcePath = Path.Combine(BeutlHomeIsolation.CurrentHome!, "source-save-failure.wav");
        WritePcmWave(sourcePath, 16_000, 16_000);
        var keys = new List<string?>();
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                keys.Add(IdempotencyKeyOf(request));
                return CreateTranscriptionResponse("source-paid");
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        try
        {
            var source = new AudioSourceItem("Source", sourcePath, TimeSpan.FromSeconds(1));
            using (var first = CreateSubtitleDialog(clients, editor, draftStore, scopes))
            {
                await WaitUntilAsync(() => first.Usage.HasSnapshot.Value);
                first.SelectedAudioSource.Value = source;
                await WaitUntilAsync(() => first.CanTranscribe.Value);
                await first.Transcribe.ExecuteAsync();
                Assert.That(
                    first.Error.Value,
                    Is.EqualTo(Beutl.Language.Strings.AiSubtitle_RunCannotBeRecorded));
                Assert.That(first.HasPartialResult.Value, Is.True);
            }

            draftStore.FailOnSave = null;
            using var restored = CreateSubtitleDialog(clients, editor, draftStore, scopes);
            restored.SelectedAudioSource.Value = source;
            await WaitUntilAsync(() => restored.Usage.HasSnapshot.Value);
            restored.RefreshAvailability();
            await WaitUntilAsync(() => restored.CanTranscribe.Value);
            await restored.Transcribe.ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(keys, Has.Count.EqualTo(2));
                Assert.That(keys[1], Is.EqualTo(keys[0]));
                Assert.That(restored.HasPartialResult.Value, Is.False);
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task SceneTranscription_ResponseLossSurvivesRangeLanguageChangeAndRestart()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-scene-transcription-ledger");
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "scene-transcription-ledger-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), editor.Scene.Id);
        IObservable<CaptionDraftScope?> scopes = Observable.Return<CaptionDraftScope?>(draftScope);
        var keys = new List<string?>();
        int requestCount = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                keys.Add(IdempotencyKeyOf(request));
                int sequence = ++requestCount;
                if (sequence == 1)
                {
                    return JsonResponse(HttpStatusCode.ServiceUnavailable, """
                        {
                          "error_code": "aiResultUnavailable",
                          "message": "The paid result is temporarily unavailable.",
                          "documentation_url": null
                        }
                        """);
                }
                return CreateTranscriptionResponse($"transcription-{sequence}");
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);

        using (var firstDialog = CreateSubtitleDialog(
                   clients,
                   editor,
                   draftStore,
                   scopes))
        {
            ConfigureSceneMix(firstDialog);
            await WaitUntilAsync(() => firstDialog.Usage.HasSnapshot.Value
                && firstDialog.SelectedAudioSource.Value?.IsSceneMix == true
                && firstDialog.CanTranscribe.Value);

            await firstDialog.Transcribe.ExecuteAsync();
            Assert.That(firstDialog.HasOutstandingTranscriptionRequest.Value, Is.True);

            firstDialog.SelectedSourceLanguage.Value = firstDialog.SourceLanguages
                .First(option => option.Code == "ja");
            editor.Scene.Duration = TimeSpan.FromMilliseconds(50);
            firstDialog.RefreshAvailability();
            await WaitUntilAsync(() => firstDialog.CanTranscribe.Value);
            await firstDialog.Transcribe.ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(requestCount, Is.EqualTo(2));
                Assert.That(keys[1], Is.Not.EqualTo(keys[0]));
                Assert.That(firstDialog.HasOutstandingTranscriptionRequest.Value, Is.True,
                    "Settling the replacement run must not retire the original recovery.");
            }
        }

        Assert.That(draftStore.TryOpen(draftScope, out ICaptionDraftSession? storedSession), Is.True);
        using (storedSession)
        {
            CaptionSceneTranscriptionResume resume =
                storedSession!.Read().Entry!.Draft.SceneTranscriptionResume!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(resume.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
                Assert.That(resume.RangeStart, Is.EqualTo(TimeSpan.Zero));
                Assert.That(resume.ChunkCount, Is.EqualTo(1));
                Assert.That(resume.Language, Is.Null);
                Assert.That(resume.RequestKeyNamePending, Is.True);
            }
        }

        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        using var restoredDialog = CreateSubtitleDialog(
            clients,
            editor,
            draftStore,
            scopes);
        ConfigureSceneMix(restoredDialog);
        await WaitUntilAsync(() => restoredDialog.Usage.HasSnapshot.Value
            && restoredDialog.SelectedAudioSource.Value?.IsSceneMix == true
            && restoredDialog.TranscriptionModelPicker.IsLoaded.Value
            && restoredDialog.CanTranscribe.Value);

        await restoredDialog.Transcribe.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestCount, Is.EqualTo(3));
            Assert.That(keys[2], Is.EqualTo(keys[0]));
            Assert.That(restoredDialog.HasOutstandingTranscriptionRequest.Value, Is.False);
        }

        static void ConfigureSceneMix(AiSubtitleDialogViewModel dialog)
        {
            dialog.SceneMixChunkDuration = TimeSpan.FromSeconds(1);
            dialog.SceneMixAudioComposer = (start, duration, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
                return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                    new float[sampleCount],
                    16_000,
                    1,
                    start));
            };
        }
    }

    [AvaloniaTest]
    public async Task SceneTranscription_SaveFailureAfterFinalResponsePreservesSeedForRestart()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-scene-save-failure");
        var draftStore = new FailingCaptionDraftStore(failOnSave: 2);
        CaptionDraftScope scope = new("user-a", Guid.NewGuid(), editor.Scene.Id);
        IObservable<CaptionDraftScope?> scopes = Observable.Return<CaptionDraftScope?>(scope);
        var keys = new List<string?>();
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                keys.Add(IdempotencyKeyOf(request));
                return CreateTranscriptionResponse("scene-paid");
            }
            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);

        static void ConfigureSceneMix(AiSubtitleDialogViewModel dialog)
        {
            dialog.SceneMixChunkDuration = TimeSpan.FromSeconds(1);
            dialog.SceneMixAudioComposer = (start, duration, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
                return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                    new float[sampleCount], 16_000, 1, start));
            };
        }

        using (var first = CreateSubtitleDialog(clients, editor, draftStore, scopes))
        {
            ConfigureSceneMix(first);
            await WaitUntilAsync(() => first.Usage.HasSnapshot.Value
                && first.SelectedAudioSource.Value?.IsSceneMix == true
                && first.CanTranscribe.Value);
            await first.Transcribe.ExecuteAsync();
            Assert.That(
                first.Error.Value,
                Is.EqualTo(Beutl.Language.Strings.AiSubtitle_RunCannotBeRecorded));
            Assert.That(first.HasPartialResult.Value, Is.True);
        }

        draftStore.FailOnSave = null;
        using var restored = CreateSubtitleDialog(clients, editor, draftStore, scopes);
        ConfigureSceneMix(restored);
        await WaitUntilAsync(() => restored.Usage.HasSnapshot.Value
            && restored.SelectedAudioSource.Value?.IsSceneMix == true
            && restored.CanTranscribe.Value);
        await restored.Transcribe.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(2));
            Assert.That(keys[1], Is.EqualTo(keys[0]));
            Assert.That(restored.HasPartialResult.Value, Is.False);
        }
    }

    private static void WritePcmWave(string path, int sampleRate, int sampleCount)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        int dataLength = checked(sampleCount * sizeof(short));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        for (int index = 0; index < sampleCount; index++)
        {
            writer.Write((short)0);
        }
    }

    [AvaloniaTest]
    public async Task Transcription_HoldingAName_DoesNotOpenTheTranslationButton()
    {
        // 文字起こしと翻訳は別々に課金される。名前を 1 つにまとめていると、
        // 回収待ちの文字起こしが翻訳側の残高・モデルの判定まで開けてしまい、
        // 支払えない依頼が新規に出ていく。
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-outstanding-scope");
        using var handler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            // 走り切っている最中。決着ではないので、その名前は残る。
            "/api/v3/ai/transcriptions" => JsonResponse(HttpStatusCode.Conflict, """
                {
                  "error_code": "aiRequestInProgress",
                  "message": "The first attempt is still running.",
                  "documentation_url": null
                }
                """),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);

        await viewModel.Transcribe.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.Error.Value,
                Is.EqualTo(Beutl.Language.Strings.AiRequestInProgress));
            Assert.That(
                viewModel.HasOutstandingTranscriptionRequest.Value,
                Is.True,
                "The chunk that was charged for stays collectable.");
            Assert.That(
                viewModel.HasOutstandingTranslationRequest.Value,
                Is.False,
                "Nothing was named on the translation's account.");
        });
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_SecondChunkFailureKeepsPaidPartialAndResumesWithoutRebilling()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-failure");
        var draftStore = new FileCaptionDraftStore(Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "scene-mix-failure-drafts"));
        CaptionDraftScope draftScope = new("user-a", Guid.NewGuid(), editor.Scene.Id);
        int transcriptionRequests = 0;
        bool firstModelRemoved = false;
        // What each chunk was sent as. A retry that names its request the way
        // the first attempt did is the difference between recovering a paid
        // transcription and buying it a second time.
        var sentKeys = new List<string>();
        var sentBodies = new List<string>();
        string audioDirectory = AiTemporaryFileStore.GetCategoryDirectory("audio");
        string[] filesBefore = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/capabilities")
                return JsonResponse(HttpStatusCode.OK, CaptionCapabilitiesJson(firstModelRemoved));
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                int requestNumber = Interlocked.Increment(ref transcriptionRequests);
                sentKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                sentBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));
                return requestNumber is 1 or 3
                    ? CreateTranscriptionResponse("transcription-first")
                    : JsonResponse(HttpStatusCode.InternalServerError, """
                        {
                          "error_code": "aiProviderError",
                          "message": "Provider failed.",
                          "documentation_url": null
                        }
                        """);
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(
            clients,
            editor,
            draftStore,
            Observable.Return<CaptionDraftScope?>(draftScope));
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true
            && viewModel.TranscriptionModelPicker.Options.Count == 2);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        var originalSegments = new[]
        {
            new AiTranscriptionSegment { Start = 0, End = 0.04, Text = "existing caption" },
        };
        viewModel.ResultSegments.Value = originalSegments;
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value && viewModel.Cues.Count == 1);

        await viewModel.Transcribe.ExecuteAsync();
        AssertStoredCaptionDraftJob(draftStore, draftScope, "transcription-first");

        string[] filesAfter = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(2));
            Assert.That(viewModel.ResultSegments.Value, Is.SameAs(originalSegments));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "existing caption" }));
            Assert.That(viewModel.IsTranscribing.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.EqualTo(Beutl.Language.Strings.AiProviderError));
            Assert.That(filesAfter, Is.EqualTo(filesBefore));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "new caption" }));
        });

        firstModelRemoved = true;
        IAiModelCatalogService catalog = clients.GetResource<IAiModelCatalogService>();
        catalog.Invalidate();
        await viewModel.TranscriptionModelPicker.LoadAsync(
            AiOperations.Transcription,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                viewModel.TranscriptionModelPicker.SelectedModel,
                Is.EqualTo(new AiModelId("transcription-a")));
            Assert.That(viewModel.TranscriptionModelPicker.Selected.Value!.IsAvailable, Is.False);
        }
        viewModel.TranscriptionModelPicker.Selected.Value =
            viewModel.TranscriptionModelPicker.Options.First(option =>
                option.Id == new AiModelId("transcription-b"));

        // A partial resume belongs only to the exact source/range/language
        // tuple. Changing the scene's own range must price a first chunk for it.
        editor.Scene.Duration = TimeSpan.FromMilliseconds(40);
        viewModel.RefreshAvailability();
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        viewModel.RefreshAvailability();
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);

        viewModel.RefreshAvailability();
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);
        await viewModel.Transcribe.ExecuteAsync();
        string[] filesAfterResume = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(3),
                "The completed first chunk must not be submitted and billed again.");
            // The server settled this chunk as failed and refunded it. Its key
            // can only ever answer with that failure now, so the retry asks
            // under a new one rather than replaying a spent request.
            Assert.That(sentKeys, Has.Count.EqualTo(3));
            Assert.That(sentKeys[2], Is.Not.EqualTo(sentKeys[1]),
                "A key the server has settled as failed cannot be asked again.");
            Assert.That(sentKeys[0], Is.Not.EqualTo(sentKeys[1]),
                "Separate chunks are separate requests and must not share a key.");
            // The name is part of what the server fingerprints, so repeating the
            // key with a different one would be refused as a conflict.
            Assert.That(sentBodies[1], Does.Contain("scene-mix-chunk-0001.wav"));
            Assert.That(sentBodies[2], Does.Contain("scene-mix-chunk-0001.wav"));
            Assert.That(sentBodies[0], Does.Contain("scene-mix-chunk-0000.wav"));
            Assert.That(sentBodies, Has.All.Contains("transcription-a"),
                "Every remaining chunk must stay on the model that started the partial run.");
            Assert.That(viewModel.Cues, Has.Count.EqualTo(2));
            Assert.That(viewModel.HasPartialResult.Value, Is.False);
            Assert.That(filesAfterResume, Is.EqualTo(filesBefore));
        });
    }

    [AvaloniaTest]
    public async Task SceneMixResumeRevision_ChangesWhenSceneContentIsEdited()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-revision");
        using var httpClient = new HttpClient(new StubHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements"
                ? JsonResponse(HttpStatusCode.OK, EntitlementsJson())
                : JsonResponse(HttpStatusCode.NotFound, "{}")));
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = CreateSubtitleDialog(clients, editor);
        long before = viewModel.SceneAudioRevision;
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(
                Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!,
                $"scene-revision-{Guid.NewGuid():N}.belm")),
        };

        editor.Scene.AddChild(element, ElementOverlapHandling.Allow);

        Assert.That(viewModel.SceneAudioRevision, Is.GreaterThan(before));
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_SecondChunkCancellationPreservesResultAndDeletesTemporaryFiles()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-cancel");
        int transcriptionRequests = 0;
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string audioDirectory = AiTemporaryFileStore.GetCategoryDirectory("audio");
        string[] filesBefore = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                int requestNumber = Interlocked.Increment(ref transcriptionRequests);
                if (requestNumber == 1)
                    return CreateTranscriptionResponse("transcription-first");

                secondRequestStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        var originalSegments = new[]
        {
            new AiTranscriptionSegment { Start = 0, End = 0.04, Text = "existing caption" },
        };
        viewModel.ResultSegments.Value = originalSegments;
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value && viewModel.Cues.Count == 1);

        Task operation = viewModel.Transcribe.ExecuteAsync();
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        GetLifetimeCancellationSource(viewModel).Cancel();
        await operation;
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        string[] filesAfter = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "scene-mix-*.wav")
            : [];
        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(2));
            Assert.That(viewModel.ResultSegments.Value, Is.SameAs(originalSegments));
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "existing caption" }));
            Assert.That(viewModel.IsTranscribing.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(filesAfter, Is.EqualTo(filesBefore));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        viewModel.ApplyPartialResult.Execute();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "new caption" }));
        });
    }

    [AvaloniaTest]
    public async Task SceneMixTranscription_EditDuringFinalChunkKeepsPaidResultRecoverable()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-stale-response");
        int transcriptionRequests = 0;
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements")
                return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
            if (request.RequestUri?.AbsolutePath == "/api/v3/ai/transcriptions")
            {
                int requestNumber = Interlocked.Increment(ref transcriptionRequests);
                if (requestNumber == 2)
                {
                    secondRequestStarted.TrySetResult();
                    await releaseSecondRequest.Task.WaitAsync(cancellationToken);
                }
                return CreateTranscriptionResponse($"transcription-{requestNumber}");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromMilliseconds(50);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sampleCount = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 0.04, Text = "existing caption" },
        ];
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value && viewModel.Cues.Count == 1);

        Task operation = viewModel.Transcribe.ExecuteAsync();
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        editor.Scene.Duration = TimeSpan.FromMilliseconds(80);
        viewModel.Cues[0].Text = "user edit while transcribing";
        releaseSecondRequest.TrySetResult();
        await operation;

        Assert.Multiple(() =>
        {
            Assert.That(transcriptionRequests, Is.EqualTo(2));
            Assert.That(viewModel.Cues.Select(cue => cue.Text),
                Is.EqualTo(new[] { "user edit while transcribing" }));
            Assert.That(viewModel.HasPartialResult.Value, Is.True);
        });

        editor.Scene.Duration = TimeSpan.FromMilliseconds(100);
        viewModel.RefreshAvailability();
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);
        await viewModel.Transcribe.ExecuteAsync();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Cues, Has.Count.EqualTo(2));
            Assert.That(viewModel.HasPartialResult.Value, Is.False);
            Assert.That(transcriptionRequests, Is.EqualTo(2),
                "A completed scene transcription must apply without another HTTP request.");
        });
    }

    private static AiTranscriptionSegment[] CreateTranslationBatchSegments()
        => Enumerable.Range(0, 201)
            .Select(index => new AiTranscriptionSegment
            {
                Start = index * 2,
                End = index * 2 + 1,
                Text = $"line-{index}",
            })
            .ToArray();

    private static HttpResponseMessage CreateTranslationResponse(HttpRequestMessage request, string jobId)
    {
        string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using JsonDocument json = JsonDocument.Parse(body);
        object[] segments = json.RootElement.GetProperty("segments")
            .EnumerateArray()
            .Select(segment => (object)new
            {
                id = segment.GetProperty("id").GetString(),
                text = "T-" + segment.GetProperty("text").GetString(),
            })
            .ToArray();
        return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            jobId,
            segments,
        }));
    }

    private static HttpResponseMessage CreateTranscriptionResponse(string jobId)
        => JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            jobId,
            language = "en",
            segments = new[] { new { start = 0.0, end = 0.04, text = "new caption" } },
        }));

    [AvaloniaTest]
    public async Task SceneMixTranscription_StopsWhereItIsWhenAskedTo()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-subtitle-scene-mix-stop");
        int transcriptionRequests = 0;
        int composeCalls = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/v3/user/entitlements":
                    return JsonResponse(HttpStatusCode.OK, EntitlementsJson());
                case "/api/v3/ai/transcriptions":
                    Interlocked.Increment(ref transcriptionRequests);
                    await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                    return CreateTranscriptionResponse("transcription-stopped");
                default:
                    return JsonResponse(HttpStatusCode.NotFound, "{}");
            }
        });
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, httpClient);
        using var viewModel = CreateSubtitleDialog(clients, editor);
        viewModel.SceneMixChunkDuration = TimeSpan.FromSeconds(60);
        viewModel.SceneMixAudioComposer = (start, duration, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref composeCalls) == 1)
            {
                viewModel.StopRequest.Execute();
            }

            int sampleCount = Math.Max(1, (int)Math.Round(duration.TotalSeconds * 16_000));
            return Task.FromResult<AudioFrameSnapshot?>(new AudioFrameSnapshot(
                new float[sampleCount],
                16_000,
                1,
                start));
        };
        await WaitUntilAsync(() => viewModel.Usage.HasSnapshot.Value
            && viewModel.SelectedAudioSource.Value?.IsSceneMix == true);
        editor.Scene.Start = TimeSpan.Zero;
        editor.Scene.Duration = TimeSpan.FromSeconds(60);
        await WaitUntilAsync(() => viewModel.CanTranscribe.Value);

        await viewModel.Transcribe.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(transcriptionRequests, Is.Zero,
                "Stopping before the upload means the account is not charged for it.");
            Assert.That(composeCalls, Is.EqualTo(1),
                "A minute of scene is not composed after the person has left.");
            Assert.That(viewModel.IsTranscribing.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.Null,
                "Leaving on purpose is not an error to report back.");
            Assert.That(viewModel.CanTranscribe.Value, Is.True,
                "And it can be started again.");
        }
    }

    // The wave is one part of a multipart upload, so its own header says how much
    // audio actually made it into the request.
    private static int ReadWaveDataLength(byte[]? body)
    {
        Assert.That(body, Is.Not.Null);
        byte[] payload = body!;
        int start = payload.AsSpan().IndexOf("RIFF"u8);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "The upload carries a wave.");
        return BitConverter.ToInt32(payload, start + 40);
    }

    private static string? IdempotencyKeyOf(HttpRequestMessage request)
        => request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values)
            ? values.SingleOrDefault()
            : null;

    private static AiImageGenerationDialogViewModel CreateImageGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageGenerationService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editor);

    private static AiImageEditDialogViewModel CreateImageEditDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageEditingService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editor);

    private static AiVideoGenerationDialogViewModel CreateVideoGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiVideoService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobKindRegistry>(),
            clients.GetResource<IAiJobMonitor>(),
            editor);

    private static AiSubtitleDialogViewModel CreateSubtitleDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        ICaptionDraftStore? draftStore = null,
        IObservable<CaptionDraftScope?>? draftScopes = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiTranscriptionService>(),
            clients.GetResource<IAiCaptionTranslationService>(),
            CaptionCatalog.CreateDefault("Default"),
            draftStore ?? CaptionDraftStoreProvider.Current,
            draftScopes ?? Observable.Return<CaptionDraftScope?>(null),
            editor);

    private static IAiPlanCoordinator CreatePlanCoordinator(BeutlApiApplication clients)
        => new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>());

    private static void AssertStoredCaptionDraftJob(
        FileCaptionDraftStore store,
        CaptionDraftScope scope,
        string expectedJobId)
    {
        using JsonDocument envelope = JsonDocument.Parse(File.ReadAllBytes(store.GetStoragePath(scope)));
        Assert.That(envelope.RootElement.GetProperty("jobId").GetString(), Is.EqualTo(expectedJobId));
    }

    private static LifetimeCancellationSource GetLifetimeCancellationSource(
        AiSubtitleDialogViewModel viewModel)
        => (LifetimeCancellationSource)typeof(AiSubtitleDialogViewModel)
            .GetField("_lifetimeCts", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(viewModel)!;

    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10, cancellationTokenSource.Token);
        }
    }

    private static void SetAuthenticatedUser(BeutlApiApplication app, HttpClient httpClient)
    {
        httpClient.BaseAddress = new Uri("https://beutl.beditor.net");
        var profile = new Profile(new ProfileResponse
        {
            Id = "test-user",
            Name = "test",
            DisplayName = "Test User",
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, app);
        var user = new AuthenticatedUser(profile, new AuthResponse
        {
            Token = "token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, app, DateTime.UtcNow);
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!).Value = user;
    }

    // Two video models with different shapes: one takes 4/6/8 seconds at 720p or
    // 1080p, the other renders only at 2K, refuses anything under five seconds
    // and takes no seed.
    private static string VideoCapabilitiesJson() => """
        {
          "operations": {
            "video.generate": {
              "models": [
                {
                  "id": "google/veo-3.1",
                  "displayName": "Veo 3.1",
                  "costTier": "medium",
                  "isDefault": true,
                  "durationsSeconds": [4, 6, 8],
                  "resolutions": ["720p", "1080p"],
                  "aspectRatios": ["16:9", "9:16"],
                  "audio": true,
                  "seed": true
                },
                {
                  "id": "minimax/hailuo-3",
                  "displayName": "MiniMax H3",
                  "costTier": "low",
                  "isDefault": false,
                  "durationsSeconds": [5, 6, 7, 8],
                  "resolutions": ["2K"],
                  "aspectRatios": ["16:9"],
                  "audio": true,
                  "seed": false
                }
              ],
              "minDurationSeconds": 1,
              "maxDurationSeconds": 60,
              "resolutions": ["720p", "1080p", "2K"],
              "aspectRatios": ["16:9", "9:16"]
            }
          }
        }
        """;

    private static string CaptionCapabilitiesJson(bool removeFirst)
    {
        static object Model(string id, bool isDefault) => new
        {
            id,
            displayName = id,
            costTier = "low",
            isDefault,
        };

        object[] translationModels = removeFirst
            ? [Model("translation-b", true)]
            : [Model("translation-a", true), Model("translation-b", false)];
        object[] transcriptionModels = removeFirst
            ? [Model("transcription-b", true)]
            : [Model("transcription-a", true), Model("transcription-b", false)];
        return JsonSerializer.Serialize(new
        {
            operations = new Dictionary<string, object>
            {
                ["subtitle.translate"] = new { models = translationModels },
                ["audio.transcribe"] = new { models = transcriptionModels },
            },
        });
    }

    // 拡大に 2 つのモデル。画面が見せているモデルがそのまま送られることを
    // 確かめるために、既定でないほうを選べる形にしてある。
    private static string ImageEditCapabilitiesJson() => """
        {
          "operations": {
            "image.edit.upscale": {
              "models": [
                {
                  "id": "openai/gpt-image-1",
                  "displayName": "GPT Image-1",
                  "costTier": "medium",
                  "isDefault": true
                },
                {
                  "id": "topaz/gigapixel-2",
                  "displayName": "Gigapixel 2",
                  "costTier": "low",
                  "isDefault": false
                }
              ]
            }
          }
        }
        """;

    private static string? ModelOfMultipart(string body)
    {
        const string Marker = "name=model";
        int at = body.IndexOf(Marker, StringComparison.Ordinal);
        if (at < 0) return null;

        int start = body.IndexOf("\r\n\r\n", at, StringComparison.Ordinal);
        if (start < 0) return null;

        start += 4;
        int end = body.IndexOf("\r\n", start, StringComparison.Ordinal);
        return end < 0 ? null : body[start..end];
    }

    private static string ImageCapabilitiesJson() => """
        {
          "operations": {
            "image.generate": {
              "models": [
                {
                  "id": "openai/gpt-image-1",
                  "displayName": "GPT Image-1",
                  "costTier": "medium",
                  "isDefault": true,
                  "aspectRatios": ["1:1", "3:2", "2:3"],
                  "backgrounds": ["auto", "opaque", "transparent"],
                  "seed": false,
                  "maxReferenceImages": 4
                },
                {
                  "id": "openai/gpt-image-2",
                  "displayName": "GPT Image-2",
                  "costTier": "low",
                  "isDefault": false,
                  "aspectRatios": ["16:9", "1:1"],
                  "backgrounds": ["auto", "opaque"],
                  "seed": false,
                  "maxReferenceImages": 4
                },
                {
                  "id": "qwen/qwen-image-3-pro",
                  "displayName": "Qwen Image 3 Pro",
                  "costTier": "low",
                  "isDefault": false,
                  "aspectRatios": ["16:9", "1:1"],
                  "backgrounds": ["auto"],
                  "seed": false,
                  "maxReferenceImages": 4
                }
              ],
              "aspectRatios": ["16:9", "1:1", "9:16", "4:3", "3:4", "3:2", "2:3"],
              "backgrounds": ["auto", "opaque", "transparent"]
            }
          }
        }
        """;

    private static HttpResponseMessage EventStreamResponse(Stream body)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string EntitlementsJson() => """
        {
          "plan": "pro",
          "subscriptionStatus": "active",
          "currentPeriodStart": "2026-08-01T00:00:00Z",
          "currentPeriodEnd": "2026-09-01T00:00:00Z",
          "canUseAi": true,
          "balance": {
            "monthlyUsage": {
              "usedPercent": 0,
              "remainingPercent": 100,
              "isExhausted": false
            },
            "additionalCredits": 0,
            "hasAdditionalCreditDebt": false
          },
          "availability": {
            "image.generate": true,
            "image.edit.remove_background": true,
            "image.edit.upscale": true,
            "image.edit.restyle": true,
            "image.edit.remove_object": true,
            "image.edit.outpaint": true,
            "audio.transcribe": true,
            "subtitle.translate": true,
            "video.generate": true
          }
        }
        """;

    private static string ImageResponseJson(string jobId, string fileId) => $$"""
        {
          "jobId": "{{jobId}}",
          "fileId": "{{fileId}}",
          "url": "https://beutl.beditor.net/api/contents/{{fileId}}"
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ByteResponse(byte[] bytes, string mediaType)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) },
            },
        };

    private sealed class FailingCaptionDraftStore(int? failOnSave) : ICaptionDraftStore
    {
        private readonly Dictionary<CaptionDraftScope, CaptionDraftEntry> _drafts = [];
        private readonly HashSet<CaptionDraftScope> _owners = [];
        private readonly object _gate = new();
        private int _saveCount;

        public int? FailOnSave { get; set; } = failOnSave;

        public bool TryOpen(
            CaptionDraftScope scope,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out ICaptionDraftSession? session)
        {
            lock (_gate)
            {
                if (!_owners.Add(scope))
                {
                    session = null;
                    return false;
                }

                session = new Session(this, scope);
                return true;
            }
        }

        private sealed class Session(FailingCaptionDraftStore owner, CaptionDraftScope scope)
            : ICaptionDraftSession
        {
            private FailingCaptionDraftStore? _owner = owner;

            public CaptionDraftScope Scope { get; } = scope;

            public CaptionDraftReadResult Read()
            {
                FailingCaptionDraftStore store = GetOwner();
                lock (store._gate)
                {
                    return store._drafts.GetValueOrDefault(Scope) is { } entry
                        ? new CaptionDraftReadResult(CaptionDraftReadOutcome.Read, entry)
                        : CaptionDraftReadResult.Absent;
                }
            }

            public void Save(CaptionDraftEntry entry)
            {
                FailingCaptionDraftStore store = GetOwner();
                lock (store._gate)
                {
                    int save = ++store._saveCount;
                    if (store.FailOnSave == save)
                        throw new IOException("Injected caption draft save failure.");
                    store._drafts[Scope] = entry;
                }
            }

            public void Delete()
            {
                FailingCaptionDraftStore store = GetOwner();
                lock (store._gate)
                {
                    store._drafts.Remove(Scope);
                }
            }

            public void Dispose()
            {
                FailingCaptionDraftStore? store = Interlocked.Exchange(ref _owner, null);
                if (store is null)
                    return;
                lock (store._gate)
                {
                    store._owners.Remove(Scope);
                }
            }

            private FailingCaptionDraftStore GetOwner()
                => _owner ?? throw new ObjectDisposedException(nameof(Session));
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        bool handleAvailability = true) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = handleAvailability
                && request.RequestUri?.AbsolutePath
                == "/api/v3/user/ai-availability"
                ? JsonResponse(HttpStatusCode.OK, "{ \"available\": true }")
                : await responder(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }
}
