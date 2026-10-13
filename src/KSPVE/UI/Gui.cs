using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KSPVE.UI;

/// <summary>The settings window's controls, shared by every feature's section (IMGUI, inside the window's layout).</summary>
internal static class Gui
{
    private static readonly Dictionary<string, string> Typed = new Dictionary<string, string>();

    public static void Header(string text)
    {
        GUILayout.Space(8);
        GUILayout.Label("<b>" + text + "</b>");
    }

    public static bool Toggle(string label, bool value) => GUILayout.Toggle(value, " " + label);

    /// <summary>
    /// A labelled slider over a handy range, with a box to type any value at all (no limits). The value only changes
    /// when the player drags the slider or types a number: just opening a panel never clamps anything.
    /// </summary>
    public static double Slider(string label, double value, double min, double max, string format = "0.00")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(230));
        float shown = (float)Math.Max(min, Math.Min(max, value));
        float v = GUILayout.HorizontalSlider(shown, (float)min, (float)max, GUILayout.Width(160));
        string name = "KSPVE value " + label;
        if (v != shown)
        {
            value = Math.Round(v, 3);
            Typed.Remove(name);
        }
        // While the box has the keyboard, it shows what is being typed; otherwise the value itself.
        bool typing = GUI.GetNameOfFocusedControl() == name;
        string current = value.ToString(format, CultureInfo.InvariantCulture);
        string text = typing && Typed.TryGetValue(name, out string t) ? t : current;
        GUI.SetNextControlName(name);
        string entered = GUILayout.TextField(text, GUILayout.Width(80));
        if (entered != text)
        {
            Typed[name] = entered;
            if (double.TryParse(entered, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && !double.IsNaN(parsed) && !double.IsInfinity(parsed))
                value = parsed;
        }
        else if (!typing) Typed.Remove(name);
        GUILayout.EndHorizontal();
        return value;
    }

    /// <summary>A whole-number slider.</summary>
    public static int IntSlider(string label, int value, int min, int max) => (int)Math.Round(Slider(label, value, min, max, "0"));

    /// <summary>One of several named choices, as a grid of buttons under a label.</summary>
    public static int Choice(string label, int value, string[] names, int columns = 4)
    {
        if (!string.IsNullOrEmpty(label)) GUILayout.Label(label);
        return GUILayout.SelectionGrid(value, names, Math.Max(1, Math.Min(columns, names.Length)));
    }

    /// <summary>An enum as a grid of buttons.</summary>
    public static T Choice<T>(string label, T value, int columns = 4) where T : struct, Enum
    {
        string[] names = Enum.GetNames(typeof(T));
        var values = (T[])Enum.GetValues(typeof(T));
        int index = Array.IndexOf(values, value);
        int picked = Choice(label, index, names, columns);
        return picked >= 0 && picked < values.Length ? values[picked] : value;
    }

    public static void Note(string text) => GUILayout.Label("<i>" + text + "</i>");
}
