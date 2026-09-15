using PixelBackup.App.Cli;
using Xunit;

namespace PixelBackup.Tests;

/// <summary>
/// Die Kommandozeile: Auswerten der Argumente und Übereinstimmung von Hilfe,
/// Handbuchseite und tatsächlich vorhandenen Befehlen.
/// </summary>
public class CommandLineTests
{
    private static readonly string ManPage =
        File.ReadAllText(Path.Combine(RepositoryRoot, "packaging", "man", "pixel-backup.1"));

    private static readonly string ManText =
        File.ReadAllText(Path.Combine(RepositoryRoot, "packaging", "man", "pixel-backup.1.txt"));

    /// <summary>Der Projektstamm – von der Testdatei aus nach oben gesucht.</summary>
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Directory.Build.props nicht gefunden.");
        }
    }

    // ------------------------------------------------------------ Auswerten

    [Fact]
    public void WithoutArguments_TheInterfaceStarts()
    {
        var options = CommandLine.Parse(Array.Empty<string>());

        Assert.Equal(CliCommand.Gui, options.Command);
        Assert.True(options.IsValid);
        Assert.True(CommandLine.WantsGui(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("devices", CliCommand.Devices)]
    [InlineData("geraete", CliCommand.Devices)]
    [InlineData("info", CliCommand.Info)]
    [InlineData("categories", CliCommand.Categories)]
    [InlineData("gruppen", CliCommand.Categories)]
    [InlineData("backup", CliCommand.Backup)]
    [InlineData("sichern", CliCommand.Backup)]
    [InlineData("restore", CliCommand.Restore)]
    [InlineData("wiederherstellen", CliCommand.Restore)]
    [InlineData("list", CliCommand.List)]
    [InlineData("verify", CliCommand.Verify)]
    [InlineData("components", CliCommand.Components)]
    [InlineData("update", CliCommand.Update)]
    [InlineData("gui", CliCommand.Gui)]
    [InlineData("manual", CliCommand.Manual)]
    [InlineData("BACKUP", CliCommand.Backup)]
    public void EveryCommandIsRecognised(string name, CliCommand expected)
    {
        Assert.Equal(expected, CommandLine.Parse(new[] { name }).Command);
    }

    [Theory]
    [InlineData("--help", CliCommand.Help)]
    [InlineData("-h", CliCommand.Help)]
    [InlineData("--version", CliCommand.Version)]
    [InlineData("-v", CliCommand.Version)]
    [InlineData("--manual", CliCommand.Manual)]
    public void TheUsualSwitchesWorkWithoutACommand(string argument, CliCommand expected)
    {
        Assert.Equal(expected, CommandLine.Parse(new[] { argument }).Command);
    }

    [Fact]
    public void Backup_ReadsEveryOption()
    {
        var options = CommandLine.Parse(new[]
        {
            "backup", "--device", "ABC123", "--output", "/mnt/platte",
            "--categories", "photos,videos, apps", "--incremental", "--no-hashes",
            "--archive", "--password", "geheim", "--yes", "--quiet", "--language", "en"
        });

        Assert.True(options.IsValid);
        Assert.Equal(CliCommand.Backup, options.Command);
        Assert.Equal("ABC123", options.Device);
        Assert.Equal("/mnt/platte", options.Output);
        Assert.Equal(new[] { "photos", "videos", "apps" }, options.Categories);
        Assert.True(options.Incremental);
        Assert.True(options.NoHashes);
        Assert.True(options.Archive);
        Assert.Equal("geheim", options.Password);
        Assert.True(options.AssumeYes);
        Assert.True(options.Quiet);
        Assert.Equal("en", options.Language);
    }

    [Fact]
    public void OptionsAlsoWorkWithEquals()
    {
        var options = CommandLine.Parse(new[] { "backup", "--output=/tmp/ziel", "--categories=photos" });

        Assert.Equal("/tmp/ziel", options.Output);
        Assert.Equal(new[] { "photos" }, options.Categories);
    }

    [Fact]
    public void ShortFormsMatchTheLongOnes()
    {
        var options = CommandLine.Parse(new[] { "restore", "-d", "X1", "-s", "satz", "-a", "-y" });

        Assert.Equal("X1", options.Device);
        Assert.Equal("satz", options.Set);
        Assert.True(options.AllCategories);
        Assert.True(options.AssumeYes);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("overwrite")]
    [InlineData("both")]
    public void ConflictModesAreAccepted(string mode)
    {
        var options = CommandLine.Parse(new[] { "restore", "--conflict", mode });

        Assert.True(options.IsValid);
        Assert.Equal(mode, options.Conflict);
    }

    [Fact]
    public void WithoutAnythingElseTheDefaultsHold()
    {
        var options = CommandLine.Parse(new[] { "backup" });

        Assert.Equal("skip", options.Conflict);
        Assert.False(options.AssumeYes);
        Assert.False(options.Json);
        Assert.Empty(options.Categories);
        Assert.Null(options.Output);
    }

    [Theory]
    [InlineData("--kaputt")]
    [InlineData("-x")]
    public void UnknownOptionsAreRefused(string argument)
    {
        var options = CommandLine.Parse(new[] { "backup", argument });

        Assert.False(options.IsValid);
        Assert.Contains(argument, options.Error);
    }

    [Fact]
    public void UnknownCommandsAreRefused()
    {
        var options = CommandLine.Parse(new[] { "schneckenpost" });

        Assert.False(options.IsValid);
        Assert.Contains("schneckenpost", options.Error);
    }

    [Fact]
    public void TwoCommandsAreRefused()
    {
        Assert.False(CommandLine.Parse(new[] { "backup", "restore" }).IsValid);
    }

    [Theory]
    [InlineData("--device")]
    [InlineData("--output")]
    [InlineData("--categories")]
    [InlineData("--set")]
    [InlineData("--conflict")]
    [InlineData("--password")]
    [InlineData("--language")]
    public void AnOptionWithoutValueIsRefused(string option)
    {
        var options = CommandLine.Parse(new[] { "backup", option });

        Assert.False(options.IsValid);
        Assert.Contains(option, options.Error);
    }

    [Fact]
    public void WrongValuesAreRefused()
    {
        Assert.False(CommandLine.Parse(new[] { "restore", "--conflict", "vielleicht" }).IsValid);
        Assert.False(CommandLine.Parse(new[] { "backup", "--language", "fr" }).IsValid);
    }

    [Theory]
    [InlineData("-help")]
    [InlineData("-?")]
    [InlineData("/?")]
    [InlineData("/h")]
    [InlineData("/help")]
    public void WindowsStyleHelpSwitchesWork(string argument)
    {
        var options = CommandLine.Parse(new[] { argument });

        Assert.True(options.IsValid);
        Assert.Equal(CliCommand.Help, options.Command);
    }

    [Theory]
    [InlineData("-version")]
    [InlineData("/v")]
    [InlineData("/version")]
    public void WindowsStyleVersionSwitchesWork(string argument)
    {
        Assert.Equal(CliCommand.Version, CommandLine.Parse(new[] { argument }).Command);
    }

    [Theory]
    [InlineData("pixel-backup")]
    [InlineData("PixelBackup.exe")]
    [InlineData("pixelbackup")]
    public void TheProgramNameAsCommandGivesAHint(string name)
    {
        var options = CommandLine.Parse(new[] { name });

        Assert.False(options.IsValid);
        Assert.Contains("--help", options.Error);
        Assert.True(CommandLine.IsProgramName(name));
    }

    [Fact]
    public void OtherWordsAreNotTheProgramName()
    {
        Assert.False(CommandLine.IsProgramName("backup"));
        Assert.False(CommandLine.IsProgramName("pixel"));
    }

    // ------------------------------------------------- Hilfe und Handbuch

    [Fact]
    public void TheHelpNamesEveryCommand()
    {
        var help = CliHelp.Text;

        foreach (var command in CommandLine.CommandNames)
        {
            Assert.Contains(command, help, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheManPageNamesEveryCommand()
    {
        foreach (var command in CommandLine.CommandNames)
        {
            Assert.Contains(command, ManPage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheManPageNamesEveryOption()
    {
        var options = new[]
        {
            "--device", "--output", "--categories", "--all", "--set", "--incremental",
            "--no-hashes", "--archive", "--password", "--conflict", "--dry-run",
            "--yes", "--json", "--quiet", "--language", "--install"
        };

        foreach (var option in options)
        {
            // In der Handbuchseite steht der Bindestrich als \- geschrieben.
            var roff = option.Replace("-", "\\-");
            Assert.True(
                ManPage.Contains(roff, StringComparison.Ordinal) || ManPage.Contains(option, StringComparison.Ordinal),
                $"{option} fehlt in der Handbuchseite.");
        }
    }

    [Fact]
    public void TheTextVersionIsUpToDate()
    {
        // pixel-backup manual gibt diese Textfassung aus; sie muss dieselben
        // Abschnitte enthalten wie die Handbuchseite.
        foreach (var section in new[] { "NAME", "BESCHREIBUNG", "BEFEHLE", "OPTIONEN", "RÜCKGABEWERTE", "BEISPIELE" })
        {
            Assert.Contains(section, ManText, StringComparison.Ordinal);
        }

        Assert.Contains("pixel-backup", ManText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmbeddedManualIsTheTextVersion()
    {
        var manual = CliHelp.Manual;

        Assert.Contains("PIXEL-BACKUP(1)", manual, StringComparison.Ordinal);
        Assert.Equal(ManText.TrimEnd(), manual.TrimEnd());
    }
}
