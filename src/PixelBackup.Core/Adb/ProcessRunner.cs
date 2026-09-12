using System.Diagnostics;
using System.Text;

namespace PixelBackup.Core.Adb;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    public IEnumerable<string> OutputLines =>
        StandardOutput.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardOutput + Environment.NewLine + StandardError;

    /// <summary>Kurze, für die Oberfläche geeignete Fehlerbeschreibung.</summary>
    public string ErrorSummary
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return string.IsNullOrEmpty(line) ? $"Exit-Code {ExitCode}" : line;
        }
    }
}

/// <summary>Startet externe Prozesse (adb) und sammelt deren Ausgabe ein.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct = default,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stdout)
            {
                stdout.Append(e.Data).Append('\n');
            }

            onOutputLine?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stderr)
            {
                stderr.Append(e.Data).Append('\n');
            }

            onErrorLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new AdbException($"Der Prozess '{fileName}' konnte nicht gestartet werden: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // Wartet zusätzlich auf das Ende der asynchronen Ausgabeverarbeitung.
        process.WaitForExit();

        string output;
        string error;
        lock (stdout)
        {
            output = stdout.ToString();
        }

        lock (stderr)
        {
            error = stderr.ToString();
        }

        return new ProcessResult(process.ExitCode, output, error);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Der Prozess ist bereits beendet oder darf nicht beendet werden.
        }
    }
}

public class AdbException : Exception
{
    public AdbException(string message) : base(message)
    {
    }

    public AdbException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
