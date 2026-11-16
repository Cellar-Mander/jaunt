using System.Net;
using System.Text;
using System.Text.Json;
using Jaunt.Core.Images;
using SkiaSharp;

namespace Jaunt.Core.Tests;

public sealed class ImageStoreTests : IDisposable
{
    private const string Banner = "https://images.ffxivvenues.com/abc/KqB9";
    private const string Other = "https://cdn.partake.gg/assets/b.webp";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "jaunt-images-" + Guid.NewGuid().ToString("N"));
    private readonly FakeNetwork network = new();
    private readonly ManualTime time = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// A store over the test folder. Unless <paramref name="decode"/> is set it converts downloads
    /// with <see cref="Convert"/>, as most tests are about fetching and keeping, not decoding.
    /// </summary>
    private ImageStore NewStore(TimeSpan? saveDelay = null, bool decode = false) =>
        new(folder, network, "Jaunt/test", (_, _) => { }, time, saveDelay, decode ? null : Convert);

    /// <summary>The PNG the fake network serves as an image, made once.</summary>
    private static readonly byte[] Png = ImageProcessingTests.Encoded(2, 2, SKEncodedImageFormat.Png);

    /// <summary>Stands in for decoding: a download that starts like <see cref="Png"/> is an image, and anything else isn't.</summary>
    private static byte[]? Convert(byte[] data, ImageKind kind) => data.AsSpan().StartsWith(Png.AsSpan(0, 8)) ? Png : null;

    /// <summary>A good image followed by padding, so it is over the 8 MB cap; made once.</summary>
    private static readonly byte[] OverCap = [.. Png, .. new byte[ImageStore.MaxImageBytes]];

    private static IReadOnlyList<ImageSource> Pictures(params string[] urls) => [.. urls.Select(u => new PictureSource(new Uri(u)))];

    private static async Task<ImageSlot> Fetched(ImageStore store, ImageKind kind, IReadOnlyList<ImageSource> sources)
    {
        var slot = store.Slot(kind, sources);
        store.Want(slot);
        await store.Idle;
        return slot;
    }

    [Fact]
    public async Task AFetchedImageIsKeptAsAPngAndShownAfterReopeningWithoutFetchingAgain()
    {
        network.Image(Banner, 600, 300);
        using (var store = NewStore(decode: true))
        {
            var slot = await Fetched(store, ImageKind.Splash, Pictures(Banner));

            using var kept = SKBitmap.Decode(slot.Path!);
            Assert.Equal((600, 300), (kept.Width, kept.Height));
        }

        network.Requests.Clear();
        using (var reopened = NewStore(decode: true))
        {
            var slot = reopened.Slot(ImageKind.Splash, Pictures(Banner));
            Assert.NotNull(slot.Path);
            // revert-check: not revert-sensitive (checks Want's existing answer for a kept image, which this change didn't touch)
            Assert.False(reopened.Want(slot));
            await reopened.Idle;
        }

        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task RequestsSayTheyAreFromJaunt()
    {
        network.Image(Banner);
        using var store = NewStore();

        await Fetched(store, ImageKind.Icon, Pictures(Banner));

        Assert.Equal(["Jaunt/test"], network.UserAgents);
    }

    [Fact]
    public async Task TheIndexIsSavedSoonAfterAFetchNotOnlyOnUnload()
    {
        network.Image(Banner);
        network.Image(Other);
        using var store = NewStore(saveDelay: TimeSpan.Zero);
        var index = Path.Combine(folder, "index.json");
        await Fetched(store, ImageKind.Icon, Pictures(Banner));
        for (var i = 0; i < 200 && !File.Exists(index); i++)
            await Task.Delay(10);

        // A later fetch is saved too, not only the first
        await Fetched(store, ImageKind.Icon, Pictures(Other));
        var otherKey = JsonSerializer.Serialize(ImageStore.KeyOf(ImageKind.Icon, Pictures(Other)));
        for (var i = 0; i < 200 && !(File.Exists(index) && (await ReadShared(index)).Contains(otherKey, StringComparison.Ordinal)); i++)
            await Task.Delay(10);

        // Copied while the store still runs, as if the game then crashed
        var copy = folder + "-copy";
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.EnumerateFiles(folder))
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        using (var reopened = new ImageStore(copy, network, "Jaunt/test", (_, _) => { }, time, process: Convert))
        {
            Assert.NotNull(reopened.Slot(ImageKind.Icon, Pictures(Banner)).Path);
            Assert.NotNull(reopened.Slot(ImageKind.Icon, Pictures(Other)).Path);
        }
        Directory.Delete(copy, recursive: true);
    }

    private static async Task<string> ReadShared(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (IOException)
        {
            // Being replaced at that moment
            return "";
        }
    }

    [Fact]
    public async Task AnUnexpectedFailureWaitsAndTriesAgain()
    {
        network.Fail(Banner, new InvalidOperationException("broken"));
        using var store = NewStore();
        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner));
        Assert.Null(slot.Path);

        network.Image(Other);
        network.Redirect(Banner, Other);
        network.Fail(Banner, null);
        time.Advance(ImageStore.RetryAfter);
        store.Want(slot);
        await store.Idle;

        Assert.NotNull(slot.Path);
    }

    [Fact]
    public async Task WithoutTheDecodersNativeLibraryItLogsOnceAndStopsDownloading()
    {
        var logged = new List<string>();
        network.Image(Banner);
        network.Image(Other);
        using var store = new ImageStore(
            folder, network, "Jaunt/test", (message, _) => logged.Add(message), time,
            process: (_, _) => throw new TypeInitializationException("SkiaSharp.SkiaApi", new DllNotFoundException("libSkiaSharp")));

        var first = await Fetched(store, ImageKind.Icon, Pictures(Banner));
        var second = await Fetched(store, ImageKind.Icon, Pictures(Other));
        time.Advance(ImageStore.RetryAfter);
        Assert.False(store.Want(first));
        Assert.False(store.Want(second));
        await store.Idle;

        Assert.Equal([Banner], network.Requests);
        Assert.Single(logged);
        Assert.Contains("libSkiaSharp", logged[0], StringComparison.Ordinal);
        Assert.Null(second.Path);
    }

    [Fact]
    public async Task AnInviteAnswerThatIsntJsonFallsToTheNextSource()
    {
        network.Respond("https://discord.com/api/v10/invites/lounge", HttpStatusCode.OK, "<html>Cloudflare</html>");
        network.Image(Banner);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, [new DiscordSource("lounge"), new PictureSource(new Uri(Banner))]);

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri(Banner)), slot.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlySlotsAskedForAreFetched()
    {
        network.Image(Banner);
        using var store = NewStore();

        var slot = store.Slot(ImageKind.Icon, Pictures(Banner));
        await store.Idle;

        Assert.Empty(network.Requests);
        Assert.Null(slot.Path);
    }

    [Fact]
    public async Task ASlotAskedForEveryFrameIsFetchedOnce()
    {
        network.Image(Banner);
        using var store = NewStore();
        var slot = store.Slot(ImageKind.Icon, Pictures(Banner));

        // Only the frame that starts the fetch reports it, as only that one allocates
        Assert.True(store.Want(slot));
        for (var i = 0; i < 10; i++)
            Assert.False(store.Want(slot));
        await store.Idle;
        Assert.False(store.Want(slot));
        await store.Idle;

        Assert.Equal([Banner], network.Requests);
        Assert.Same(slot, store.Slot(ImageKind.Icon, Pictures(Banner)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.OK)] // but not an image
    public async Task ASourceWithoutAnImageFallsToTheNext(HttpStatusCode status)
    {
        network.Respond(Banner, status, "<html>Not here</html>");
        network.Image(Other);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner, Other));

        Assert.Equal([Banner, Other], network.Requests);
        Assert.NotNull(slot.Path);
    }

    [Fact]
    public async Task AnImageLargerThan8MbIsSkippedEvenWhenItsLengthIsntStated()
    {
        // Over the cap but otherwise a good image
        Assert.NotNull(Convert(OverCap, ImageKind.Icon));
        network.Respond(Banner, HttpStatusCode.OK, OverCap, sendLength: false);
        network.Image(Other);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner, Other));

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri(Other)), slot.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownOrRefusingHostFallsToTheNextSource()
    {
        network.Fail(Banner, new HttpRequestException(HttpRequestError.NameResolutionError));
        network.Fail("https://refused.example/a.png", new HttpRequestException(HttpRequestError.ConnectionError));
        network.Image(Other);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner, "https://refused.example/a.png", Other));

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri(Other)), slot.Path, StringComparison.Ordinal);
    }

    public static TheoryData<Exception> PassingErrors => new()
    {
        new HttpRequestException(HttpRequestError.ResponseEnded, "cut off", new IOException()),
        new TaskCanceledException("timed out"),
    };

    [Theory]
    [MemberData(nameof(PassingErrors))]
    public async Task ACutOffResponseOrATimeoutWaitsAndTriesAgain(Exception error)
    {
        network.Fail(Banner, error);
        network.Image(Other);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner, Other));

        Assert.Null(slot.Path);
        Assert.Equal([Banner], network.Requests);
    }

    [Fact]
    public async Task ADownloadLargerThan8MbIsSkipped()
    {
        network.Respond(Banner, HttpStatusCode.OK, new byte[ImageStore.MaxImageBytes + 1], sendLength: false);
        network.Image(Other);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner, Other));

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri(Other)), slot.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task APassingFailureWaitsAndTriesAgainRatherThanFallingToTheNext(HttpStatusCode status)
    {
        network.Respond(Banner, status, "busy");
        network.Image(Other);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner, Other));
        Assert.Null(slot.Path);
        Assert.Equal([Banner], network.Requests);

        time.Advance(ImageStore.RetryAfter - TimeSpan.FromSeconds(1));
        store.Want(slot);
        await store.Idle;
        Assert.Single(network.Requests);

        network.Image(Banner);
        time.Advance(TimeSpan.FromSeconds(1));
        store.Want(slot);
        await store.Idle;
        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri(Banner)), slot.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASlotWithNoImageIsRememberedForADay()
    {
        network.Respond(Banner, HttpStatusCode.NotFound, "");
        using (var store = NewStore())
            Assert.Null((await Fetched(store, ImageKind.Splash, Pictures(Banner))).Path);

        network.Requests.Clear();
        time.Advance(ImageStore.MissingFor - TimeSpan.FromMinutes(1));
        using (var store = NewStore())
            await Fetched(store, ImageKind.Splash, Pictures(Banner));
        Assert.Empty(network.Requests);

        time.Advance(TimeSpan.FromMinutes(1));
        using (var store = NewStore())
            await Fetched(store, ImageKind.Splash, Pictures(Banner));
        Assert.Equal([Banner], network.Requests);
    }

    [Fact]
    public async Task ASlotWithNoImageIsntTriedAgainWhileTheGameRuns()
    {
        network.Respond(Banner, HttpStatusCode.NotFound, "");
        using var store = NewStore();
        var slot = await Fetched(store, ImageKind.Splash, Pictures(Banner));

        time.Advance(ImageStore.RetryAfter);
        store.Want(slot);
        await store.Idle;

        Assert.Equal([Banner], network.Requests);
    }

    [Fact]
    public async Task AKeptImageIsntLookedUpAgainWithinAWeek()
    {
        network.Json("https://discord.com/api/v10/invites/lounge", """{"guild":{"id":"1","icon":"old"}}""");
        network.Image("https://cdn.discordapp.com/icons/1/old.png?size=128");
        IReadOnlyList<ImageSource> sources = [new DiscordSource("lounge")];
        using (var store = NewStore())
            await Fetched(store, ImageKind.Icon, sources);

        network.Requests.Clear();
        time.Advance(ImageStore.FreshFor - TimeSpan.FromMinutes(1));
        using (var store = NewStore())
            Assert.NotNull((await Fetched(store, ImageKind.Icon, sources)).Path);

        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task AKeptImageIsLookedUpAgainAfterAWeekShowingTheOldOneMeanwhile()
    {
        network.Json("https://discord.com/api/v10/invites/lounge", """{"guild":{"id":"1","icon":"old"}}""");
        network.Image("https://cdn.discordapp.com/icons/1/old.png?size=128");
        IReadOnlyList<ImageSource> sources = [new DiscordSource("lounge")];
        using (var store = NewStore())
            await Fetched(store, ImageKind.Icon, sources);

        network.Json("https://discord.com/api/v10/invites/lounge", """{"guild":{"id":"1","icon":"new"}}""");
        network.Image("https://cdn.discordapp.com/icons/1/new.png?size=128");
        network.Requests.Clear();
        time.Advance(ImageStore.FreshFor);
        using (var store = NewStore())
        {
            var slot = store.Slot(ImageKind.Icon, sources);
            var old = slot.Path;
            Assert.NotNull(old);
            store.Want(slot);
            await store.Idle;

            Assert.NotEqual(old, slot.Path);
            Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri("https://cdn.discordapp.com/icons/1/new.png?size=128")), slot.Path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnImageAlreadyKeptIsNotDownloadedAgainForAnotherSlot()
    {
        network.Image(Banner);
        using var store = NewStore();
        await Fetched(store, ImageKind.Icon, Pictures(Banner));

        network.Respond(Other, HttpStatusCode.NotFound, "");

        var second = await Fetched(store, ImageKind.Icon, Pictures(Other, Banner));

        Assert.Equal([Banner, Other], network.Requests);
        Assert.NotNull(second.Path);
    }

    [Fact]
    public async Task ADiscordIconComesFromTheInvitesServer()
    {
        network.Json("https://discord.com/api/v10/invites/lounge", """{"guild":{"id":"1","icon":"abc","banner":null,"splash":"def"}}""");
        network.Image("https://cdn.discordapp.com/icons/1/abc.png?size=128");
        network.Image("https://cdn.discordapp.com/splashes/1/def.png?size=1024");
        using var store = NewStore();
        IReadOnlyList<ImageSource> sources = [new DiscordSource("lounge")];

        var icon = await Fetched(store, ImageKind.Icon, sources);
        var splash = await Fetched(store, ImageKind.Splash, sources);

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri("https://cdn.discordapp.com/icons/1/abc.png?size=128")), icon.Path, StringComparison.Ordinal);
        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Splash, new Uri("https://cdn.discordapp.com/splashes/1/def.png?size=1024")), splash.Path, StringComparison.Ordinal);
        // The invite is looked up once for both
        Assert.Single(network.Requests, r => r.Contains("/invites/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADiscordSplashIsTheServersBannerElseItsInviteSplash()
    {
        const string Invite = "https://discord.com/api/v10/invites/lounge";
        const string BannerImage = "https://cdn.discordapp.com/banners/1/b.png?size=1024";
        network.Json(Invite, """{"guild":{"id":"1","icon":"i","banner":"b","splash":"s"}}""");
        network.Image(BannerImage);
        network.Image("https://cdn.discordapp.com/splashes/1/s.png?size=1024");
        using var store = NewStore();
        IReadOnlyList<ImageSource> sources = [new DiscordSource("lounge")];

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Splash, new Uri(BannerImage)), (await Fetched(store, ImageKind.Splash, sources)).Path, StringComparison.Ordinal);

        // A banner that has gone falls to the splash
        Directory.Delete(folder, recursive: true);
        network.Respond(BannerImage, HttpStatusCode.NotFound, "");
        using var reopened = NewStore();
        Assert.EndsWith(
            ImageStore.FileNameOf(ImageKind.Splash, new Uri("https://cdn.discordapp.com/splashes/1/s.png?size=1024")),
            (await Fetched(reopened, ImageKind.Splash, sources)).Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABusyInviteLookupIsAskedAgainLater()
    {
        const string Invite = "https://discord.com/api/v10/invites/lounge";
        network.Respond(Invite, HttpStatusCode.TooManyRequests, "");
        network.Image("https://cdn.discordapp.com/icons/1/i.png?size=128");
        using var store = NewStore();
        var slot = await Fetched(store, ImageKind.Icon, [new DiscordSource("lounge"), new PictureSource(new Uri(Banner))]);
        Assert.Null(slot.Path);

        network.Json(Invite, """{"guild":{"id":"1","icon":"i"}}""");
        time.Advance(ImageStore.RetryAfter);
        store.Want(slot);
        await store.Idle;

        Assert.Equal(2, network.Requests.Count(r => r == Invite));
        Assert.NotNull(slot.Path);
    }

    [Fact]
    public async Task AnUnknownInviteFallsToTheNextSource()
    {
        network.Respond("https://discord.com/api/v10/invites/gone", HttpStatusCode.NotFound, """{"message":"Unknown Invite","code":10006}""");
        network.Image(Banner);
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, [new DiscordSource("gone"), new PictureSource(new Uri(Banner))]);

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri(Banner)), slot.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWebsitesIconIsTheOneItsHomePageNames()
    {
        network.Respond("https://lounge.example/", HttpStatusCode.OK, """<link rel="icon" href="/static/i.png">""");
        network.Image("https://lounge.example/static/i.png");
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, [new FaviconSource(new Uri("https://lounge.example/"))]);

        Assert.Equal(["https://lounge.example/", "https://lounge.example/static/i.png"], network.Requests);
        Assert.NotNull(slot.Path);
    }

    [Fact]
    public async Task AWebsitesIconsAreTriedInTurn()
    {
        network.Respond("https://lounge.example/", HttpStatusCode.OK, """<link rel="icon" href="/gone.png">""");
        network.Image("https://lounge.example/favicon.ico");
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, [new FaviconSource(new Uri("https://lounge.example/"))]);

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri("https://lounge.example/favicon.ico")), slot.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWebsitesIconLinksAreReadAgainstWhereRedirectsLed()
    {
        network.Redirect("https://lounge.example/", "https://www.lounge.example/home/");
        network.Respond("https://www.lounge.example/home/", HttpStatusCode.OK, """<link rel="icon" href="i.png">""");
        network.Image("https://www.lounge.example/home/i.png");
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, [new FaviconSource(new Uri("https://lounge.example/"))]);

        Assert.EndsWith(ImageStore.FileNameOf(ImageKind.Icon, new Uri("https://www.lounge.example/home/i.png")), slot.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWebsiteWithoutAHomePageMayStillHaveFaviconIco()
    {
        network.Respond("https://lounge.example/", HttpStatusCode.NotFound, "");
        network.Image("https://lounge.example/favicon.ico");
        using var store = NewStore();

        var slot = await Fetched(store, ImageKind.Icon, [new FaviconSource(new Uri("https://lounge.example/"))]);

        Assert.NotNull(slot.Path);
    }

    [Fact]
    public async Task IndexEntriesNotWorkedOutForAMonthAreDroppedWithTheirFiles()
    {
        network.Image(Banner);
        string path;
        using (var store = NewStore())
            path = (await Fetched(store, ImageKind.Icon, Pictures(Banner))).Path!;
        var stray = Path.Combine(folder, "stray.png");
        var halfWritten = Path.Combine(folder, ImageStore.FileNameOf(ImageKind.Icon, new Uri(Other)) + ".tmp");
        var other = Path.Combine(folder, "notes.txt");
        await File.WriteAllTextAsync(stray, "x");
        await File.WriteAllTextAsync(halfWritten, "x");
        await File.WriteAllTextAsync(other, "x");

        time.Advance(ImageStore.ForgetAfter - TimeSpan.FromMinutes(1));
        using (NewStore())
        {
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(Path.Combine(folder, "index.json")));
            Assert.True(File.Exists(other));
            Assert.False(File.Exists(stray));
            Assert.False(File.Exists(halfWritten));
        }

        time.Advance(TimeSpan.FromMinutes(1));
        using (var store = NewStore())
        {
            Assert.False(File.Exists(path));
            Assert.Null(store.Slot(ImageKind.Icon, Pictures(Banner)).Path);
        }
    }

    [Fact]
    public async Task AnUnreadableIndexStartsEmpty()
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.json"), "{not json");
        network.Image(Banner);

        using var store = NewStore();
        var slot = await Fetched(store, ImageKind.Icon, Pictures(Banner));

        Assert.NotNull(slot.Path);
    }

    [Theory]
    [InlineData("../../outside0123456789abcdef0123.png")]
    [InlineData("0123456789abcdef0123456789abcdeX.png")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("0123456789abcdef0123456789abcdef0.png")]
    public async Task AnIndexNamingAFileTheStoreDoesntMakeIsIgnored(string name)
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, name.Replace("../../", "", StringComparison.Ordinal)), "x");
        var key = ImageStore.KeyOf(ImageKind.Icon, Pictures(Banner));
        var entry = JsonSerializer.Serialize(new { File = name, Checked = time.GetUtcNow() });
        await File.WriteAllTextAsync(Path.Combine(folder, "index.json"), "{\"Version\":1,\"Slots\":{" + JsonSerializer.Serialize(key) + ":" + entry + "}}");

        using var store = NewStore();

        Assert.Null(store.Slot(ImageKind.Icon, Pictures(Banner)).Path);
    }

    [Fact]
    public async Task AnIndexNamingAFileTheStoreMadeIsRead()
    {
        Directory.CreateDirectory(folder);
        const string Name = "0123456789abcdef0123456789abcdef.png";
        await File.WriteAllTextAsync(Path.Combine(folder, Name), "x");
        var key = ImageStore.KeyOf(ImageKind.Icon, Pictures(Banner));
        var entry = JsonSerializer.Serialize(new { File = Name, Checked = time.GetUtcNow() });
        await File.WriteAllTextAsync(Path.Combine(folder, "index.json"), "{\"Version\":1,\"Slots\":{" + JsonSerializer.Serialize(key) + ":" + entry + "}}");

        using var store = NewStore();

        Assert.Equal(Path.Combine(folder, Name), store.Slot(ImageKind.Icon, Pictures(Banner)).Path);
    }

    [Fact]
    public async Task WantingASettledOrWaitingSlotDoesNotAllocate()
    {
        network.Respond(Banner, HttpStatusCode.ServiceUnavailable, "busy");
        using var store = NewStore();
        var settled = await Fetched(store, ImageKind.Icon, []);
        var waiting = await Fetched(store, ImageKind.Icon, Pictures(Banner));

        for (var i = 0; i < 3; i++)
        {
            store.Want(settled);
            store.Want(waiting);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = false;
        for (var i = 0; i < 100_000; i++)
            started |= store.Want(settled) | store.Want(waiting);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.False(started);
    }

    /// <summary>The network as a table of responses by address; any other address is not found.</summary>
    private sealed class FakeNetwork : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body, bool SendLength)> responses = [];
        private readonly Dictionary<string, Exception> failures = [];
        private readonly Dictionary<string, string> redirects = [];

        public List<string> Requests { get; } = [];

        public HashSet<string> UserAgents { get; } = [];

        /// <summary>Requests for the address throw, as the client does when the network fails; null stops that.</summary>
        public void Fail(string url, Exception? error)
        {
            lock (responses)
            {
                if (error is null)
                    failures.Remove(url);
                else
                    failures[url] = error;
            }
        }

        /// <summary>The address answers as if redirected to another, which it reports as where it ended.</summary>
        public void Redirect(string url, string to)
        {
            lock (responses)
                redirects[url] = to;
        }

        /// <summary>The address serves <see cref="Png"/>, which <see cref="Convert"/> takes as an image.</summary>
        public void Image(string url) => Respond(url, HttpStatusCode.OK, Png);

        /// <summary>The address serves a real WebP image of a size, for a store that decodes.</summary>
        public void Image(string url, int width, int height) =>
            Respond(url, HttpStatusCode.OK, ImageProcessingTests.Encoded(width, height));

        public void Json(string url, string json) => Respond(url, HttpStatusCode.OK, json);

        public void Respond(string url, HttpStatusCode status, string body) => Respond(url, status, Encoding.UTF8.GetBytes(body));

        public void Respond(string url, HttpStatusCode status, byte[] body, bool sendLength = true)
        {
            lock (responses)
                responses[url] = (status, body, sendLength);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            (HttpStatusCode Status, byte[] Body, bool SendLength) found;
            lock (responses)
            {
                Requests.Add(url);
                UserAgents.Add(request.Headers.UserAgent.ToString());
                if (failures.TryGetValue(url, out var error))
                    return Task.FromException<HttpResponseMessage>(error);
                if (redirects.TryGetValue(url, out var to))
                {
                    url = to;
                    request.RequestUri = new Uri(to);
                }

                found = responses.GetValueOrDefault(url, (HttpStatusCode.NotFound, [], true));
            }

            HttpContent content = found.SendLength ? new ByteArrayContent(found.Body) : new StreamContent(new UnknownLengthStream(found.Body));
            return Task.FromResult(new HttpResponseMessage(found.Status) { Content = content, RequestMessage = request });
        }
    }

    /// <summary>A stream that won't say its length, so a download's size is found only by reading it.</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
