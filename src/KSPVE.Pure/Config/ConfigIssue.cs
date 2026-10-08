namespace KSPVE.Pure.Config;

public enum IssueSeverity
{
    Warning,
    Error
}

public sealed class ConfigIssue
{
    public ConfigIssue(IssueSeverity severity, string path, string message)
    {
        Severity = severity;
        Path = path;
        Message = message;
    }

    public IssueSeverity Severity { get; }
    public string Path { get; }
    public string Message { get; }

    public override string ToString() => $"{Severity} at {Path}: {Message}";
}
