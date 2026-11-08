using DualSenseClient.Controllers.TouchpadMouse;
using DualSenseClient.Controllers.TouchpadMouse.Windows;

namespace DualSenseClient.Tests.Controllers.TouchpadMouse;

public class TouchpadMouseInjectorFactoryTests
{
    [Test]
    public void IsSupported_MatchesOperatingSystem() => Assert.That(TouchpadMouseInjectorFactory.IsSupported, Is.EqualTo(OperatingSystem.IsWindows()));

    [Test]
    public void NullMouseInjector_MethodsAreInertAndReturnFalse()
    {
        NullMouseInjector injector = new NullMouseInjector();
        Assert.Multiple(() =>
        {
            Assert.That(injector.Move(5, -5), Is.False);
            Assert.That(injector.LeftDown(), Is.False);
            Assert.That(injector.LeftUp(), Is.False);
            Assert.That(injector.RightDown(), Is.False);
            Assert.That(injector.RightUp(), Is.False);
            Assert.That(injector.Wheel(120), Is.False);
            Assert.That(injector.HorizontalWheel(-120), Is.False);
        });
    }

    [Test]
    public void Create_OnWindows_ReturnsSendInputInjector()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("SendInput is Windows-only.");
        }

        Assert.That(TouchpadMouseInjectorFactory.Create(), Is.InstanceOf<SendInputMouseInjector>());
    }
}