using DualSenseClient.Core.Utilities;

namespace DualSenseClient.Tests.Core;

public class PathResolverTests
{
    [Test]
    public void BaseDirectory_IsNotNullOrEmpty()
    {
        Assert.That(PathResolver.BaseDirectory, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void BaseDirectory_IsAbsolute()
    {
        Assert.That(Path.IsPathRooted(PathResolver.BaseDirectory), Is.True);
    }

    [Test]
    public void BaseDirectory_IsNotInTempDirectory()
    {
        string tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Assert.That(PathResolver.BaseDirectory, Does.Not.StartWith(tempPath));
    }

    [Test]
    public void BaseDirectory_IsWritable()
    {
        string probeFile = Path.Combine(PathResolver.BaseDirectory, $".write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probeFile, string.Empty);
            Assert.That(File.Exists(probeFile), Is.True);
        }
        finally
        {
            File.Delete(probeFile);
        }
    }

    [Test]
    public void GetFullPath_RelativePath_CombinesWithBaseDirectory()
    {
        string result = PathResolver.GetFullPath("some/relative/path");
        Assert.That(result, Is.EqualTo(Path.Combine(PathResolver.BaseDirectory, "some/relative/path")));
    }

    [Test]
    public void GetFullPath_RootedPath_ReturnsAsIs()
    {
        string rooted = Path.GetFullPath("/some/rooted/path");
        string result = PathResolver.GetFullPath(rooted);
        Assert.That(result, Is.EqualTo(rooted));
    }

    [Test]
    public void GetFullPath_SimpleFileName_CombinesWithBase()
    {
        string result = PathResolver.GetFullPath("config.json");
        Assert.That(result, Is.EqualTo(Path.Combine(PathResolver.BaseDirectory, "config.json")));
    }

    [Test]
    public void GetFullPath_MultipleSegments_CombinesAll()
    {
        string result = PathResolver.GetFullPath("subdir", "config.json");
        Assert.That(result, Is.EqualTo(Path.Combine(PathResolver.BaseDirectory, "subdir", "config.json")));
    }

    [Test]
    public void GetFullPath_SingleSegmentArray_CombinesWithBase()
    {
        string result = PathResolver.GetFullPath(new[] { "file.txt" });
        Assert.That(result, Is.EqualTo(Path.Combine(PathResolver.BaseDirectory, "file.txt")));
    }

    [Test]
    public void GetFullPath_ThreeSegments_CombinesAll()
    {
        string result = PathResolver.GetFullPath("a", "b", "c.txt");
        Assert.That(result, Is.EqualTo(Path.Combine(PathResolver.BaseDirectory, "a", "b", "c.txt")));
    }

    [Test]
    public void GetFullPath_RootedSegmentInParams_ReturnsRooted()
        // When the first of multiple segments is rooted, Path.Combine uses it as root
    {
        string rooted = Path.GetFullPath("/rooted");
        string result = PathResolver.GetFullPath(rooted, "child", "file.txt");
        Assert.That(result, Does.StartWith(Path.GetDirectoryName(rooted)!));
    }
}