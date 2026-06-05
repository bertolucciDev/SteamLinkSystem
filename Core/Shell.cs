using System.Diagnostics;

namespace Core;

public static class Shell
{
    public static String Run(string command)
    {
        var process = new Process();

        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.Arguments = $"-c \"{command}\"";

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return string.IsNullOrEmpty(error) ? output : error;
    }
}