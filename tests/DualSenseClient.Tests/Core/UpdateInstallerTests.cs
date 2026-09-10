using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DualSenseClient.Core.Utilities;

namespace DualSenseClient.Tests.Core;

public class UpdateInstallerTests
{
    [TestCase(UpdateTarget.Windows, "DualSenseClient.zip", "DualSenseClient.exe", false)]
    [TestCase(UpdateTarget.Linux, "DualSenseClient-linux.zip", "DualSenseClient", true)]
    [TestCase(UpdateTarget.AppImage, "DualSenseClient.AppImage", null, true)]
    public void Target_SelectsPlatformAsset(UpdateTarget target, string asset, string? entry, bool exec) =>
        Assert.That(UpdateInstaller.Target(target), Is.EqualTo(new UpdateAsset(asset, entry, exec)));

    [Test]
    public void GetAssetUrl_PicksPlatformAsset()
    {
        const string tag = "v1.0.0-1.387d3de";
        string page = $"https://github.com/DualSenseClient/DualSenseClient/releases/tag/{tag}";

        string download(string asset)
        {
            return $"https://github.com/DualSenseClient/DualSenseClient/releases/download/{tag}/{asset}";
        }

        UpdateChecker.ReleaseInfo release = new UpdateChecker.ReleaseInfo(tag, page,
            new Dictionary<string, string>
            {
                ["DualSenseClient.zip"] = download("DualSenseClient.zip"),
                ["DualSenseClient-linux.zip"] = download("DualSenseClient-linux.zip"),
                ["DualSenseClient.AppImage"] = download("DualSenseClient.AppImage")
            });

        Assert.Multiple(() =>
        {
            Assert.That(release.GetAssetUrl(UpdateTarget.Windows), Is.EqualTo(download("DualSenseClient.zip")));
            Assert.That(release.GetAssetUrl(UpdateTarget.Linux), Is.EqualTo(download("DualSenseClient-linux.zip")));
            Assert.That(release.GetAssetUrl(UpdateTarget.AppImage), Is.EqualTo(download("DualSenseClient.AppImage")));
        });
    }

    [Test]
    public void GetAssetUrl_MissingAsset_ReturnsNull()
    {
        UpdateChecker.ReleaseInfo release = new UpdateChecker.ReleaseInfo("v1.0.0-1.387d3de",
            "https://github.com/DualSenseClient/DualSenseClient/releases/tag/v1.0.0-1.387d3de",
            new Dictionary<string, string>());
        Assert.That(release.GetAssetUrl(UpdateTarget.Windows), Is.Null);
    }

    [Test]
    public void IsAppImage_FileSuffix_Detected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UpdateInstaller.IsAppImage("/home/user/DualSenseClient.AppImage"), Is.True);
            Assert.That(UpdateInstaller.IsAppImage("/home/user/DualSenseClient"), Is.False);
            Assert.That(UpdateInstaller.IsAppImage(null), Is.False);
        });
    }

    [Test]
    public async Task VerifyHashAsync_MatchAndMismatch()
    {
        string dir = Directory.CreateTempSubdirectory("update-test-").FullName;
        try
        {
            string file = Path.Combine(dir, "payload.bin");
            await File.WriteAllBytesAsync(file, "update-bytes"u8.ToArray());

            // Real hash of the payload for the positive case.
            string actual;
            using (SHA256 sha = SHA256.Create())
            {
                actual = Convert.ToHexString(sha.ComputeHash(await File.ReadAllBytesAsync(file))).ToLowerInvariant();
            }

            using HttpClient correct = new HttpClient(new CannedHandler($"{actual}  payload.bin\n"));
            using HttpClient wrong = new HttpClient(new CannedHandler(new string('0', 64) + "  payload.bin\n"));
            string url = "https://github.com/DualSenseClient/DualSenseClient/releases/download/v1.0.0-1.387d3de/DualSenseClient.zip";

            bool match = await UpdateInstaller.VerifyHashAsync(file, url, correct);
            bool mismatch = await UpdateInstaller.VerifyHashAsync(file, url, wrong);
            Assert.Multiple(() =>
            {
                Assert.That(match, Is.True);
                Assert.That(mismatch, Is.False);
            });
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task VerifyHashAsync_MissingSidecar_FallsBackToTrue()
    {
        string dir = Directory.CreateTempSubdirectory("update-test-").FullName;
        try
        {
            string file = Path.Combine(dir, "payload.bin");
            await File.WriteAllBytesAsync(file, "update-bytes"u8.ToArray());
            using HttpClient missing = new HttpClient(new CannedHandler(null));
            Assert.That(
                await UpdateInstaller.VerifyHashAsync(file,
                    "https://github.com/DualSenseClient/DualSenseClient/releases/download/v1.0.0-1.387d3de/DualSenseClient.zip", missing), Is.True);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void ExtractEntry_RoundTripsSingleFile()
    {
        string dir = Directory.CreateTempSubdirectory("update-test-").FullName;
        try
        {
            string zip = Path.Combine(dir, "update.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("DualSenseClient.exe");
                using StreamWriter writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write("new-binary");
            }

            string dest = Path.Combine(dir, "out", "DualSenseClient.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            UpdateInstaller.ExtractEntry(zip, "DualSenseClient.exe", dest);
            Assert.That(File.ReadAllText(dest), Is.EqualTo("new-binary"));
            Assert.Throws<FileNotFoundException>(() => UpdateInstaller.ExtractEntry(zip, "missing.exe", dest));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void SwapExecutable_RenamesAsideAndCleansUp()
    {
        string dir = Directory.CreateTempSubdirectory("update-test-").FullName;
        try
        {
            string exe = Path.Combine(dir, "DualSenseClient.exe");
            string next = Path.Combine(dir, "new.exe");
            File.WriteAllText(exe, "old");
            File.WriteAllText(next, "new");

            UpdateInstaller.SwapExecutable(next, exe);
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(exe), Is.EqualTo("new"));
                Assert.That(File.ReadAllText(exe + ".old"), Is.EqualTo("old"));
            });

            UpdateInstaller.CleanupOld(exe);
            Assert.That(File.Exists(exe + ".old"), Is.False);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task VerifyHashAsync_SidecarError_FailsClosed()
    {
        string dir = Directory.CreateTempSubdirectory("update-test-").FullName;
        try
        {
            string file = Path.Combine(dir, "payload.bin");
            await File.WriteAllBytesAsync(file, "update-bytes"u8.ToArray());
            using HttpClient broken = new HttpClient(new CannedHandler(null, HttpStatusCode.InternalServerError));
            Assert.That(
                await UpdateInstaller.VerifyHashAsync(file,
                    "https://github.com/DualSenseClient/DualSenseClient/releases/download/v1.0.0-1.387d3de/DualSenseClient.zip", broken), Is.False);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string? _body;
        private readonly HttpStatusCode _missingStatus;

        public CannedHandler(string? body, HttpStatusCode missingStatus = HttpStatusCode.NotFound)
        {
            _body = body;
            _missingStatus = missingStatus;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            _body is null
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Error", null, _missingStatus))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body)
                });
    }
}