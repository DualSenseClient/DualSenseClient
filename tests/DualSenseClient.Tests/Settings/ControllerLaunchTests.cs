using System.Xml.Linq;
using System.Xml.XPath;
using DualSenseClient.Launch;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class ControllerLaunchTests
{
    [Test]
    public void SubscriptionMatchesOnlyKnownControllerConnections()
    {
        XDocument document = XDocument.Parse(ControllerLaunchTask.CreateXml(@"C:\Apps & Games\DualSenseClient.exe", "S-1-5-21-123",
            ["AA:BB:CC:DD:EE:FF", "aabbccddeeff", "invalid"]));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string subscription = document.Descendants(ns + "Subscription").Single().Value;
        string filter = XDocument.Parse(subscription).Descendants("Select").Single().Value;

        XElement Event(int id, string address)
        {
            return new XElement("Event", new XElement("System", new XElement("EventID", id)),
                new XElement("EventData", new XElement("Data", new XAttribute("Name", "bthAddr"), address)));
        }

        Assert.That((bool)Event(9, "187723572702975").XPathEvaluate("boolean(self::" + filter + ")"), Is.True);
        Assert.That((bool)Event(9, "123").XPathEvaluate("boolean(self::" + filter + ")"), Is.False);
        Assert.That((bool)Event(4, "187723572702975").XPathEvaluate("boolean(self::" + filter + ")"), Is.False);
        Assert.That(document.Descendants(ns + "Command").Single().Value, Is.EqualTo(@"C:\Apps & Games\DualSenseClient.exe"));
        Assert.That(document.Descendants(ns + "LogonType").Single().Value, Is.EqualTo("InteractiveToken"));
        Assert.That(document.Descendants(ns + "Arguments").Single().Value, Is.EqualTo("--controller-connect"));
    }

    [Test]
    public void RegistrationRequiresAValidKnownController() =>
        Assert.Throws<InvalidOperationException>(() => ControllerLaunchTask.CreateXml("app.exe", "user", ["", "invalid"]));

    [Test]
    public void ConnectionLaunchDefaultsOffAndPreservesTrayPreference()
    {
        UiSettings settings = new UiSettings
        {
            LaunchOnControllerConnect = true,
            StartInTray = true
        };
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        UiSettings restored = System.Text.Json.JsonSerializer.Deserialize<UiSettings>(json)!;
        Assert.That(new UiSettings().LaunchOnControllerConnect, Is.False);
        Assert.That(restored.LaunchOnControllerConnect, Is.True);
        Assert.That(restored.StartInTray, Is.True);
    }
}