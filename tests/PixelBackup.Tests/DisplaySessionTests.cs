using PixelBackup.Core.Localization;
using PixelBackup.Core.Services;
using Xunit;

namespace PixelBackup.Tests;

/// <summary>
/// Erkennung der grafischen Sitzung (X11, Wayland, gar keine). Geprüft wird die
/// reine Auswertung, damit die Prüfungen auf jedem Betriebssystem laufen.
/// </summary>
[Collection("Sprache")]
public class DisplaySessionTests : IDisposable
{
    private readonly AppLanguage _original = Localizer.I.Lang;

    // Die Texte hängen an der Sprache – für die Prüfungen fest auf Deutsch.
    public DisplaySessionTests() => Localizer.I.Lang = AppLanguage.De;

    public void Dispose() => Localizer.I.Lang = _original;

    [Theory]
    [InlineData("x11", null, ":0")]
    [InlineData("x11", "", "")]
    [InlineData(null, null, ":0")]
    [InlineData("X11", null, null)]
    public void DetectSession_RecognisesX11(string? sessionType, string? wayland, string? display)
    {
        Assert.Equal(LinuxSessionType.X11, LinuxEnvironment.DetectSession(sessionType, wayland, display));
    }

    [Theory]
    [InlineData("wayland", "wayland-0", ":1")]
    [InlineData("wayland", null, null)]
    // Manche Sitzungen setzen XDG_SESSION_TYPE nicht – WAYLAND_DISPLAY genügt.
    [InlineData(null, "wayland-1", ":0")]
    [InlineData(" Wayland ", null, ":0")]
    public void DetectSession_RecognisesWayland(string? sessionType, string? wayland, string? display)
    {
        Assert.Equal(LinuxSessionType.Wayland, LinuxEnvironment.DetectSession(sessionType, wayland, display));
    }

    [Fact]
    public void DetectSession_WithoutAnyVariableThereIsNoSession()
    {
        Assert.Equal(LinuxSessionType.None, LinuxEnvironment.DetectSession(null, null, null));
        Assert.Equal(LinuxSessionType.None, LinuxEnvironment.DetectSession("tty", "", ""));
    }

    [Fact]
    public void DisplayProblem_X11AndXWaylandAreFine()
    {
        Assert.Null(LinuxEnvironment.DescribeDisplayProblem(LinuxSessionType.X11, hasXDisplay: true));
        Assert.Null(LinuxEnvironment.DescribeDisplayProblem(LinuxSessionType.Wayland, hasXDisplay: true));
    }

    [Fact]
    public void DisplayProblem_WaylandWithoutXWaylandIsExplained()
    {
        var problem = LinuxEnvironment.DescribeDisplayProblem(LinuxSessionType.Wayland, hasXDisplay: false);

        Assert.NotNull(problem);
        Assert.Contains("XWayland", problem);
    }

    [Fact]
    public void DisplayProblem_WithoutSessionMentionsBothVariables()
    {
        var problem = LinuxEnvironment.DescribeDisplayProblem(LinuxSessionType.None, hasXDisplay: false);

        Assert.NotNull(problem);
        Assert.Contains("DISPLAY", problem);
        Assert.Contains("WAYLAND_DISPLAY", problem);
    }

    [Fact]
    public void DescribeSession_NamesXWaylandOnlyWhenItIsThere()
    {
        Assert.Contains("XWayland", LinuxEnvironment.DescribeSession(LinuxSessionType.Wayland, hasXDisplay: true));
        Assert.Contains("fehlt", LinuxEnvironment.DescribeSession(LinuxSessionType.Wayland, hasXDisplay: false));
        Assert.Equal("X11", LinuxEnvironment.DescribeSession(LinuxSessionType.X11, hasXDisplay: true));
        Assert.Equal(string.Empty, LinuxEnvironment.DescribeSession(LinuxSessionType.Unknown, hasXDisplay: false));
    }

    [Theory]
    [InlineData("Unable to load shared library 'libX11.so.6' or one of its dependencies.", "libX11.so.6")]
    [InlineData("System.DllNotFoundException: libXrandr.so.2", "libXrandr.so.2")]
    [InlineData("dlopen failed: libfontconfig.so.1: cannot open shared object file", "libfontconfig.so.1")]
    public void DescribeMissingLibrary_NamesTheLibrary(string message, string soname)
    {
        var hint = LinuxEnvironment.DescribeMissingLibrary(message);

        Assert.NotNull(hint);
        Assert.Contains(soname, hint);
    }

    [Fact]
    public void DescribeMissingLibrary_IgnoresOtherFailures()
    {
        Assert.Null(LinuxEnvironment.DescribeMissingLibrary(null));
        Assert.Null(LinuxEnvironment.DescribeMissingLibrary(string.Empty));
        Assert.Null(LinuxEnvironment.DescribeMissingLibrary("Die Sicherung ist fehlgeschlagen."));
    }

    [Fact]
    public void DescribeMissingLibrary_AddsTheInstallCommandOfTheDistribution()
    {
        var hint = LinuxEnvironment.DescribeMissingLibrary("Unable to load shared library 'libX11.so.6'");
        var command = LinuxEnvironment.InstallCommand("libx11-6");

        Assert.NotNull(hint);

        // Ohne erkannte Paketverwaltung (etwa unter Windows) bleibt es beim Namen.
        if (command is not null)
        {
            Assert.Contains("sudo", hint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DescribeSession_FollowsTheLanguage()
    {
        Localizer.I.Lang = AppLanguage.De;
        Assert.Equal("keine grafische Sitzung", LinuxEnvironment.DescribeSession(LinuxSessionType.None, false));

        Localizer.I.Lang = AppLanguage.En;
        Assert.Equal("no graphical session", LinuxEnvironment.DescribeSession(LinuxSessionType.None, false));
    }
}
