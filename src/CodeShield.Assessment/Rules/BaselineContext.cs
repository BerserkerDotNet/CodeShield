using CodeShield.Abstractions;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Rules;

/// <summary>The baseline counterpart of one analysis context, addressed through Git's rename map by current paths and unit IDs.</summary>
public sealed class BaselineContext(ChangeMap map, ContextFacts? facts)
{
    public bool Exists => facts is not null;

    public FileFact? FileOf(string currentFile)
    {
        var path = map.BaselinePathOf(currentFile);
        return path is null ? null : facts?.Files.FirstOrDefault(file => file.Path == path);
    }

    public string UnitIdOf(string currentUnitId) => UnitIds.ToBaseline(currentUnitId, map);

    public ClassFact? ClassOf(string currentUnitId)
    {
        var unitId = UnitIdOf(currentUnitId);
        return facts?.Classes.FirstOrDefault(candidate => candidate.UnitId == unitId);
    }

    /// <summary>True when the baseline file has no method, and the baseline context no class, with the unit's baseline ID.</summary>
    public bool LacksUnit(FileFact baselineFile, string currentUnitId)
    {
        var unitId = UnitIdOf(currentUnitId);
        return baselineFile.Methods.All(method => method.UnitId != unitId) && ClassOf(currentUnitId) is null;
    }
}
