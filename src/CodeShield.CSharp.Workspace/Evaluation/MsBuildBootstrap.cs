using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;

namespace CodeShield.CSharp.Evaluation;

/// <summary>Registers the installed .NET SDK's MSBuild once, before any Microsoft.Build type is touched.</summary>
internal static class MsBuildBootstrap
{
    private static readonly Lock Gate = new();
    private static string _sdkVersion = "unknown";

    public static string SdkVersion => _sdkVersion;

    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (MSBuildLocator.IsRegistered)
            {
                return;
            }

            var instance = MSBuildLocator.RegisterDefaults();
            _sdkVersion = instance.Version.ToString();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string MsBuildVersion() => AssemblyVersions.Describe(typeof(Microsoft.Build.Evaluation.Project).Assembly);
}
