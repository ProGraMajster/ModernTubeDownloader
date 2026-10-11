using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Tests;

public sealed class StartupAndTrayTests
{
    private sealed class Registration : IStartupRegistration
    {
        public string? Command;
        public string? Read() => Command;
        public void Write(string command) => Command = command;
        public void Delete() => Command = null;
    }

    [Fact]
    public void StartupQuotesUnicodeAndSpacePathsAndRepairsOnlyOwnedValue()
    {
        var registration = new Registration();
        var path = @"C:\Aplikacja żółć\ModernTubeDownloader.exe";
        var service = new WindowsStartupService(registration, path);
        Assert.Equal(StartupRegistrationStatus.Disabled, service.Inspect(true));
        service.Apply(true, true);
        Assert.Equal($"\"{path}\" --autostart --start-hidden", registration.Command);
        Assert.Equal(StartupRegistrationStatus.Current, service.Inspect(true));
        var moved = new WindowsStartupService(registration, @"C:\Moved\ModernTubeDownloader.exe");
        Assert.Equal(StartupRegistrationStatus.NeedsRepair, moved.Inspect(true));
        moved.Apply(true, false);
        Assert.Equal(StartupRegistrationStatus.Current, moved.Inspect(false));
        moved.Apply(false, false);
        Assert.Null(registration.Command);
        registration.Command = "foreign.exe";
        Assert.Throws<InvalidOperationException>(() => service.Apply(true, false));
        Assert.Throws<InvalidOperationException>(() => service.Apply(false, false));
        Assert.Equal("foreign.exe", registration.Command);
    }

    [Theory]
    [InlineData("ModernTubeDownloader.exe")]
    [InlineData("C:\\app\\other.exe")]
    [InlineData("C:\\app\"\\ModernTubeDownloader.exe")]
    public void StartupRejectsInvalidPaths(string path) => Assert.Throws<ArgumentException>(() => WindowsStartupService.BuildCommand(path, false));

    [Fact]
    public void StartupRejectsCommandLongerThanWindowsRunLimit() =>
        Assert.Throws<ArgumentException>(() => WindowsStartupService.BuildCommand("C:\\" + new string('x', 260) + "\\ModernTubeDownloader.exe", true));

    [Theory]
    [InlineData(TrayCloseBehavior.HideToTray, true, false, false, TrayCloseDecision.Hide)]
    [InlineData(TrayCloseBehavior.HideToTray, true, true, false, TrayCloseDecision.Hide)]
    [InlineData(TrayCloseBehavior.HideToTray, false, false, false, TrayCloseDecision.Exit)]
    [InlineData(TrayCloseBehavior.HideToTray, true, true, true, TrayCloseDecision.Confirm)]
    [InlineData(TrayCloseBehavior.Exit, true, true, false, TrayCloseDecision.Confirm)]
    [InlineData(TrayCloseBehavior.Exit, true, false, false, TrayCloseDecision.Exit)]
    [InlineData(TrayCloseBehavior.Ask, true, false, false, TrayCloseDecision.Confirm)]
    public void CloseDecisionPreservesWorkAndNeverHidesWithoutTray(TrayCloseBehavior behavior, bool tray, bool work, bool exit, TrayCloseDecision expected)
        => Assert.Equal(expected, TrayPolicy.Close(new() { CloseBehavior = behavior }, tray, work, exit));

    [Fact]
    public void HiddenStartupRequiresAnAccessibleTrayAndNeverHidesAManualLaunch()
    {
        var settings = new StartupSettings();
        Assert.True(TrayPolicy.StartHidden(settings, true, true, false));
        Assert.False(TrayPolicy.StartHidden(settings, true, false, false));
        Assert.False(TrayPolicy.StartHidden(settings, false, true, true));
        settings.ShowTrayIcon = false;
        Assert.False(TrayPolicy.StartHidden(settings, true, true, true));
    }

    [Fact]
    public async Task ManualSecondaryLaunchUsesExistingPipeToRestoreWindow()
    {
        var name = "MTDTrayTest-" + Guid.NewGuid().ToString("N");
        using var primary = new ExternalLinkInstance(name);
        using var secondary = new ExternalLinkInstance(name);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Start(_ => throw new InvalidOperationException("No media request expected."), onOpenWindow: () => { opened.SetResult(); return Task.CompletedTask; });
        Assert.True(await secondary.ForwardOpenWindowAsync());
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await primary.ForwardOpenWindowAsync());
    }
}
