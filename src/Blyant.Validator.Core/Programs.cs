using System.ComponentModel;
using System.Diagnostics;

namespace Blyant.Validator.Core;

/// <summary>What the engines that run another program share (vnu with Java, the Typst CLI).</summary>
public static class Programs
{
    /// <summary>
    /// Stops a program that takes too long or whose check is stopped, with the programs it started (Java, Typst's
    /// workers). A program that ended meanwhile, or cannot be stopped, is left as it is
    /// </summary>
    public static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It ended meanwhile, or cannot be stopped
        }
    }
}
