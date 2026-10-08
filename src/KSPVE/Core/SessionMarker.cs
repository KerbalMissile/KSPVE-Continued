using System.Globalization;
using System.IO;

namespace KSPVE.Core;

/// <summary>
/// A file that exists while KSP runs and is deleted on clean exit. It holds the number of
/// KSPVE errors logged this session, so the next start knows whether the last one ended in a
/// crash that KSPVE may have caused.
/// </summary>
internal sealed class SessionMarker
{
    private const int MaxRecordedErrors = 100;
    private readonly string _path;
    private int _errors;

    public SessionMarker(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        PreviousSessionUnclean = true;
        int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count);
        PreviousSessionErrorCount = count;
    }

    public bool PreviousSessionUnclean { get; }
    public int PreviousSessionErrorCount { get; }

    public void BeginSession()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path));
        Write();
    }

    public void RecordError()
    {
        if (_errors >= MaxRecordedErrors) return;
        _errors++;
        Write();
    }

    public void EndSession()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void Write() => File.WriteAllText(_path, _errors.ToString(CultureInfo.InvariantCulture));
}
