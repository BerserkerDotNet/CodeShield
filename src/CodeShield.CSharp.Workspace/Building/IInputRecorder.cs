namespace CodeShield.CSharp.Building;

/// <summary>What the workspace loader needs to report the files it reads: record by hash, or read and record in one step.</summary>
public interface IInputRecorder
{
    byte[] ReadAndRecord(string scope, string recordedPath, string fullPath);

    void Record(string scope, string path, string sha256);
}
