using DualSenseClient.HidHide;

namespace DualSenseClient.Tests.HidHide;

/// <summary>
/// Tests of Linux controller identity and group resolution.
/// </summary>
[TestFixture]
public sealed class LinuxHidingIdentityTests
{
    /// <summary>
    /// Temporary sysfs and group-file root.
    /// </summary>
    private string _tempDir = null!;

    /// <summary>
    /// Creates an isolated temp directory for each test.
    /// </summary>
    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LinuxHidingIdentityTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Removes the temp directory.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A hidraw uevent with HID_UNIQ yields the controller MAC.
    /// </summary>
    [Test]
    public void TryGetHidUniq_ReturnsMac()
    {
        string ueventDir = Path.Combine(_tempDir, "class", "hidraw", "hidraw2", "device");
        Directory.CreateDirectory(ueventDir);
        File.WriteAllText(Path.Combine(ueventDir, "uevent"),
            "HID_ID=0003:0000054C:00000CE6\nHID_NAME=DualSense\nHID_UNIQ=d0:bc:c1:17:55:63\n");

        bool parsed = LinuxHidingIdentity.TryGetHidUniq(_tempDir, "/dev/hidraw2", out string id);

        Assert.That(parsed, Is.True);
        Assert.That(id, Is.EqualTo("d0:bc:c1:17:55:63"));
    }

    /// <summary>
    /// An empty HID_UNIQ means the device cannot be hidden.
    /// </summary>
    [Test]
    public void TryGetHidUniq_EmptyUniq_ReturnsFalse()
    {
        string ueventDir = Path.Combine(_tempDir, "class", "hidraw", "hidraw2", "device");
        Directory.CreateDirectory(ueventDir);
        File.WriteAllText(Path.Combine(ueventDir, "uevent"), "HID_UNIQ=\n");

        Assert.That(LinuxHidingIdentity.TryGetHidUniq(_tempDir, "/dev/hidraw2", out _), Is.False);
    }

    /// <summary>
    /// A missing uevent means the device cannot be hidden.
    /// </summary>
    [Test]
    public void TryGetHidUniq_MissingFile_ReturnsFalse() => Assert.That(LinuxHidingIdentity.TryGetHidUniq(_tempDir, "/dev/hidraw9", out _), Is.False);

    /// <summary>
    /// An HID_UNIQ containing unsafe characters is rejected.
    /// </summary>
    [Test]
    public void TryGetHidUniq_InvalidCharacters_ReturnsFalse()
    {
        string ueventDir = Path.Combine(_tempDir, "class", "hidraw", "hidraw2", "device");
        Directory.CreateDirectory(ueventDir);
        File.WriteAllText(Path.Combine(ueventDir, "uevent"), "HID_UNIQ=bad id\n");

        Assert.That(LinuxHidingIdentity.TryGetHidUniq(_tempDir, "/dev/hidraw2", out _), Is.False);
    }

    /// <summary>
    /// The group file's third field is the GID.
    /// </summary>
    [Test]
    public void TryGetGroupGid_ReturnsGid()
    {
        string groupFile = Path.Combine(_tempDir, "group");
        File.WriteAllText(groupFile, "root:x:0:\ndualsenseclient:x:4242:goose\n");

        Assert.That(LinuxHidingIdentity.TryGetGroupGid(groupFile, "dualsenseclient", out uint gid), Is.True);
        Assert.That(gid, Is.EqualTo(4242));
    }

    /// <summary>
    /// A missing group is reported as unavailable.
    /// </summary>
    [Test]
    public void TryGetGroupGid_MissingGroup_ReturnsFalse()
    {
        string groupFile = Path.Combine(_tempDir, "group");
        File.WriteAllText(groupFile, "root:x:0:\n");

        Assert.That(LinuxHidingIdentity.TryGetGroupGid(groupFile, "dualsenseclient", out _), Is.False);
    }
}