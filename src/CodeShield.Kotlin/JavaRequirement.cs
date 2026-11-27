using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeShield.Kotlin;

/// <summary>The Java runtime the Kotlin worker needs: it is compiled for Java 17 bytecode.</summary>
public static partial class JavaRequirement
{
    public const string Description = "a Java runtime (JDK or JRE) 17 or newer";

    public static bool IsSupported(Version version) => version.Major >= 17;

    /// <summary>
    /// Parses the output of <c>java -version</c>, for example <c>openjdk version "21.0.11" 2026-04-21</c> or the legacy
    /// <c>java version "1.8.0_352"</c> (reported as major version 8).
    /// </summary>
    public static Version? Parse(string output)
    {
        var match = VersionPattern().Match(output);
        if (!match.Success)
        {
            return null;
        }

        var numbers = match.Groups["version"].Value.Split('.').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToList();
        if (numbers[0] == 1 && numbers.Count > 1)
        {
            numbers.RemoveAt(0);
        }

        while (numbers.Count < 3)
        {
            numbers.Add(0);
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    [GeneratedRegex("""version "(?<version>\d{1,9}(?:\.\d{1,9})*)""")]
    private static partial Regex VersionPattern();
}
