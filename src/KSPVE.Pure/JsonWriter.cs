using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KSPVE.Pure;

/// <summary>
/// Minimal streaming JSON writer for KSPVE reports. No external dependencies; numbers are
/// always written with the invariant culture; NaN, infinities and null strings become null.
/// </summary>
public sealed class JsonWriter
{
    private readonly StringBuilder _sb = new StringBuilder();
    private readonly Stack<bool> _containerHasItems = new Stack<bool>();
    private bool _afterName;

    public JsonWriter BeginObject() { BeforeValue(); _sb.Append('{'); _containerHasItems.Push(false); return this; }
    public JsonWriter EndObject() { _containerHasItems.Pop(); _sb.Append('}'); return this; }
    public JsonWriter BeginArray() { BeforeValue(); _sb.Append('['); _containerHasItems.Push(false); return this; }
    public JsonWriter EndArray() { _containerHasItems.Pop(); _sb.Append(']'); return this; }

    public JsonWriter Name(string name)
    {
        BeforeItem();
        WriteString(name);
        _sb.Append(':');
        _afterName = true;
        return this;
    }

    public JsonWriter Value(string value)
    {
        BeforeValue();
        if (value == null) _sb.Append("null");
        else WriteString(value);
        return this;
    }

    public JsonWriter Value(bool value) { BeforeValue(); _sb.Append(value ? "true" : "false"); return this; }

    public JsonWriter Value(long value) { BeforeValue(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }

    public JsonWriter Value(double value)
    {
        BeforeValue();
        if (double.IsNaN(value) || double.IsInfinity(value)) _sb.Append("null");
        else _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>Appends already-serialised JSON as a single value.</summary>
    public JsonWriter RawValue(string json) { BeforeValue(); _sb.Append(json); return this; }

    public JsonWriter Property(string name, string value) => Name(name).Value(value);
    public JsonWriter Property(string name, bool value) => Name(name).Value(value);
    public JsonWriter Property(string name, long value) => Name(name).Value(value);
    public JsonWriter Property(string name, double value) => Name(name).Value(value);

    public override string ToString() => _sb.ToString();

    private void BeforeItem()
    {
        if (_containerHasItems.Count == 0) return;
        if (_containerHasItems.Peek()) _sb.Append(',');
        else { _containerHasItems.Pop(); _containerHasItems.Push(true); }
    }

    private void BeforeValue()
    {
        if (_afterName) { _afterName = false; return; }
        BeforeItem();
    }

    private void WriteString(string s)
    {
        _sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': _sb.Append("\\\""); break;
                case '\\': _sb.Append("\\\\"); break;
                case '\n': _sb.Append("\\n"); break;
                case '\r': _sb.Append("\\r"); break;
                case '\t': _sb.Append("\\t"); break;
                default:
                    if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else _sb.Append(c);
                    break;
            }
        }
        _sb.Append('"');
    }
}
