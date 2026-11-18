using System.Diagnostics;

/// <summary>Process helpers shared by the tools/ scripts (pulled in with <c>#:include</c>).</summary>
static class Tool
{
    /// <summary>Runs a process and returns its stdout; its stderr is passed through and a non-zero exit throws.</summary>
    public static string Run(string file, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (workingDirectory is not null)
            start.WorkingDirectory = workingDirectory;
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (stderr.Result.Length > 0)
            Console.Error.Write(stderr.Result);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{file} exited with {process.ExitCode}");
        return stdout;
    }

    /// <summary><c>javap -p -c</c> output for classes in a jar or class folder.</summary>
    public static string Disassemble(string classPath, params string[] classes) => Run("javap", ["-cp", classPath, "-p", "-c", .. classes]);

    public static string GitCommit(string checkout)
    {
        try
        {
            return Run("git", ["-C", checkout, "rev-parse", "--short", "HEAD"]).Trim();
        }
        catch (Exception)
        {
            return "(unknown commit)";
        }
    }
}
