using CodeShield.Core.Scope;

namespace CodeShield.Core.Rules;

public static class UnitIds
{
    /// <summary>Rewrites the file part of a "file#name" unit ID through the rename map.</summary>
    public static string ToBaseline(string unitId, ChangeMap map)
    {
        var separator = unitId.IndexOf('#', StringComparison.Ordinal);
        if (separator < 0)
        {
            return unitId;
        }

        var baselineFile = map.BaselinePathOf(unitId[..separator]);
        return baselineFile is null ? unitId : baselineFile + unitId[separator..];
    }
}
