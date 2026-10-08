using System;
using System.Collections.Generic;
using System.Globalization;

namespace KSPVE.Pure.Config;

public abstract class SettingDef
{
    protected SettingDef(string key, string description)
    {
        Key = key;
        Description = description;
    }

    public string Key { get; }
    public string Description { get; }

    internal abstract object DefaultObject { get; }
    internal abstract object Resolve(string raw, string path, List<ConfigIssue> issues);
}

public abstract class SettingDef<T> : SettingDef
{
    protected SettingDef(string key, T defaultValue, string description) : base(key, description)
    {
        Default = defaultValue;
    }

    public T Default { get; }

    internal override object DefaultObject => Default;

    internal override object Resolve(string raw, string path, List<ConfigIssue> issues) => Parse(raw, path, issues);

    /// <summary>Parses a raw config string; on failure adds an issue and returns a fallback.</summary>
    protected abstract T Parse(string raw, string path, List<ConfigIssue> issues);

    protected T Fallback(string path, List<ConfigIssue> issues, string reason)
    {
        issues.Add(new ConfigIssue(IssueSeverity.Warning, path, reason + "; using default " + Format(Default)));
        return Default;
    }

    protected static string Format(object value) => Convert.ToString(value, CultureInfo.InvariantCulture);
}

public sealed class BoolSetting : SettingDef<bool>
{
    public BoolSetting(string key, bool defaultValue, string description) : base(key, defaultValue, description) { }

    protected override bool Parse(string raw, string path, List<ConfigIssue> issues)
    {
        if (bool.TryParse(raw?.Trim(), out bool value)) return value;
        return Fallback(path, issues, $"'{raw}' is not True or False");
    }
}

public sealed class IntSetting : SettingDef<int>
{
    public IntSetting(string key, int defaultValue, int min, int max, string description) : base(key, defaultValue, description)
    {
        Min = min;
        Max = max;
    }

    public int Min { get; }
    public int Max { get; }

    protected override int Parse(string raw, string path, List<ConfigIssue> issues)
    {
        if (!int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return Fallback(path, issues, $"'{raw}' is not an integer");
        if (value < Min || value > Max)
        {
            int clamped = value < Min ? Min : Max;
            issues.Add(new ConfigIssue(IssueSeverity.Warning, path, $"{value} is outside [{Min}, {Max}]; clamped to {clamped}"));
            return clamped;
        }
        return value;
    }
}

public sealed class DoubleSetting : SettingDef<double>
{
    public DoubleSetting(string key, double defaultValue, double min, double max, string description) : base(key, defaultValue, description)
    {
        Min = min;
        Max = max;
    }

    public double Min { get; }
    public double Max { get; }

    protected override double Parse(string raw, string path, List<ConfigIssue> issues)
    {
        if (!double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsNaN(value) || double.IsInfinity(value))
            return Fallback(path, issues, $"'{raw}' is not a finite number");
        if (value < Min || value > Max)
        {
            double clamped = value < Min ? Min : Max;
            issues.Add(new ConfigIssue(IssueSeverity.Warning, path,
                $"{Format(value)} is outside [{Format(Min)}, {Format(Max)}]; clamped to {Format(clamped)}"));
            return clamped;
        }
        return value;
    }
}

public sealed class StringSetting : SettingDef<string>
{
    public StringSetting(string key, string defaultValue, string description) : base(key, defaultValue, description) { }

    protected override string Parse(string raw, string path, List<ConfigIssue> issues)
    {
        string s = raw?.Trim();
        if (!string.IsNullOrEmpty(s)) return s;
        return Fallback(path, issues, "value is empty");
    }
}

public sealed class EnumSetting<TEnum> : SettingDef<TEnum> where TEnum : struct, Enum
{
    public EnumSetting(string key, TEnum defaultValue, string description) : base(key, defaultValue, description) { }

    protected override TEnum Parse(string raw, string path, List<ConfigIssue> issues)
    {
        string s = raw?.Trim();
        bool looksNumeric = !string.IsNullOrEmpty(s) && (char.IsDigit(s[0]) || s[0] == '-' || s[0] == '+');
        if (!looksNumeric && Enum.TryParse(s, true, out TEnum value) && Enum.IsDefined(typeof(TEnum), value)) return value;
        return Fallback(path, issues, $"'{raw}' is not one of {string.Join(", ", Enum.GetNames(typeof(TEnum)))}");
    }
}
