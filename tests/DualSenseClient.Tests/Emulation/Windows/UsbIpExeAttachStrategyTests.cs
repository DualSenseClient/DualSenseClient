using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation.Windows;

/// <summary>
/// Tests of <see cref="UsbIpExeAttachStrategy"/>: discovery of usbip.exe, the exact vendor CLI
/// argument lists for attach and detach, vhci port parsing and remembering, and the failure
/// contract (return false with an actionable error instead of throwing). All runs go through
/// <see cref="FakeProcessRunner"/>, and every discovery decision is driven by a throwaway
/// directory, so the tests are hermetic regardless of what is installed on the machine.
/// </summary>
[TestFixture]
public sealed class UsbIpExeAttachStrategyTests
{
    private const string Host = "127.0.0.1";
    private const int Port = 3240;
    private const string BusId = "1-1";

    private string _searchDirectory = string.Empty;

    [SetUp]
    public void CreateSearchDirectory()
    {
        _searchDirectory = Path.Combine(Path.GetTempPath(), "emulation-usbip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_searchDirectory);
    }

    [TearDown]
    public void DeleteSearchDirectory()
    {
        if (Directory.Exists(_searchDirectory))
        {
            Directory.Delete(_searchDirectory, true);
        }
    }

    [Test]
    public void TryAttach_AttachableDevice_InvokesVendorArgumentsAndReturnsTrue()
    {
        string exePath = CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "successfully attached to port 5", string.Empty)
        };
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(runner.Runs, Has.Count.EqualTo(1));
        Assert.That(runner.Runs[0].FileName, Is.EqualTo(exePath));
        Assert.That(runner.Runs[0].Timeout, Is.GreaterThan(TimeSpan.Zero));
        // The tcp port is a global vendor option and precedes the subcommand; the rest is the exact
        // attach argv. ArgumentList entries are passed verbatim, so no quoting is needed.
        Assert.That(runner.Runs[0].Arguments, Is.EqualTo(new[]
        {
            "--tcp-port", "3240", "attach", "-r", "127.0.0.1", "-b", "1-1"
        }));
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Information), Is.True);
    }

    [Test]
    public void TryAttach_NonZeroExit_ReturnsFalseWithStderrInError()
    {
        CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Result = new ProcessResult(1, string.Empty, "error: The remote computer refused the network connection.")
        };
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("The remote computer refused the network connection."));
        Assert.That(error, Does.Contain("exit code 1"));
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    [Test]
    public void TryAttach_UsbIpExeMissing_ReturnsFalseWithInstallGuidance()
    {
        FakeProcessRunner runner = new FakeProcessRunner();
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("USB/IP must be installed"));
        Assert.That(error, Does.Contain("usbip-win2"));
        Assert.That(runner.Runs, Is.Empty);
    }

    [Test]
    public void TryAttach_SuccessWithPortInOutput_RemembersPortForDetach()
    {
        CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "New device attached on port 2", string.Empty)
        };
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? attachError);
        runner.Result = new ProcessResult(0, "port 2 is successfully detached", string.Empty);

        bool detached = strategy.TryDetach(BusId, out string? detachError);

        Assert.That(attached, Is.True);
        Assert.That(attachError, Is.Null);
        Assert.That(detached, Is.True);
        Assert.That(detachError, Is.Null);
        Assert.That(runner.Runs, Has.Count.EqualTo(2));
        Assert.That(runner.Runs[1].Arguments, Is.EqualTo(new[]
        {
            "detach", "-p", "2"
        }));
    }

    [Test]
    public void TryAttach_RunnerTimesOut_ReturnsFalseWithError()
    {
        CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner
        {
            ExceptionToThrow = new TimeoutException("Synthetic timeout")
        };
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("timed out"));
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    [Test]
    public void TryAttach_RunnerThrows_ReturnsFalseWithError()
    {
        CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner
        {
            ExceptionToThrow = new InvalidOperationException("Synthetic start failure")
        };
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("Synthetic start failure"));
    }

    [Test]
    public void TryAttach_SuccessWithoutPortInOutput_ReturnsTrueAndLogsWarning()
    {
        CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "device attached", string.Empty)
        };
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    [Test]
    public void TryDetach_WithoutPriorAttach_ReturnsFalseAndLogsWarning()
    {
        CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner();
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.False);
        Assert.That(error, Does.Contain("no vhci port"));
        Assert.That(runner.Runs, Is.Empty);
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    [Test]
    public void TryDetach_NonZeroExit_ReturnsFalseAndKeepsPortForRetry()
    {
        CreateFakeUsbIpExe();
        FakeProcessRunner runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "successfully attached to port 7", string.Empty)
        };
        RecordingLogger logger = new RecordingLogger();
        UsbIpExeAttachStrategy strategy = CreateStrategy(runner, logger);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        runner.Result = new ProcessResult(1, string.Empty, "error: The device is not attached.");
        bool firstAttempt = strategy.TryDetach(BusId, out string? error);
        runner.Result = new ProcessResult(0, string.Empty, string.Empty);
        bool retry = strategy.TryDetach(BusId, out string? retryError);

        Assert.That(firstAttempt, Is.False);
        Assert.That(error, Does.Contain("The device is not attached."));
        Assert.That(retry, Is.True);
        Assert.That(retryError, Is.Null);
        Assert.That(runner.Runs, Has.Count.EqualTo(3));
        Assert.That(runner.Runs[2].Arguments, Is.EqualTo(new[]
        {
            "detach", "-p", "7"
        }));
    }

    [Test]
    public void TryFindUsbIpExe_DirectoryWithUsbIpExe_ReturnsTrueWithFullPath()
    {
        string exePath = CreateFakeUsbIpExe();

        bool found = UsbIpExeAttachStrategy.TryFindUsbIpExe(new[]
        {
            _searchDirectory
        }, out string path);

        Assert.That(found, Is.True);
        Assert.That(path, Is.EqualTo(exePath));
    }

    [Test]
    public void TryFindUsbIpExe_DirectoriesWithoutUsbIpExe_ReturnsFalse()
    {
        string nestedDirectory = Path.Combine(_searchDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);

        bool found = UsbIpExeAttachStrategy.TryFindUsbIpExe(new[]
        {
            _searchDirectory, nestedDirectory
        }, out string path);

        Assert.That(found, Is.False);
        Assert.That(path, Is.Empty);
    }

    [Test]
    public void TryFindUsbIpExe_MultipleMatches_ReturnsFirstInOrder()
    {
        string firstExePath = CreateFakeUsbIpExe();
        string secondDirectory = Path.Combine(_searchDirectory, "second");
        Directory.CreateDirectory(secondDirectory);
        File.WriteAllBytes(Path.Combine(secondDirectory, "usbip.exe"), Array.Empty<byte>());

        bool found = UsbIpExeAttachStrategy.TryFindUsbIpExe(new[]
        {
            _searchDirectory, secondDirectory
        }, out string path);

        Assert.That(found, Is.True);
        Assert.That(path, Is.EqualTo(firstExePath));
    }

    private string CreateFakeUsbIpExe()
    {
        string path = Path.Combine(_searchDirectory, "usbip.exe");
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    private UsbIpExeAttachStrategy CreateStrategy(FakeProcessRunner runner, RecordingLogger logger) =>
        new UsbIpExeAttachStrategy(runner, logger, new[]
        {
            _searchDirectory
        });
}