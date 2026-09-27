using System.Globalization;
using System.Text.RegularExpressions;
using Godot;

namespace LastTide;

/// <summary>
/// The string table. Every player-facing line lives in <c>assets/text/en.csv</c> (key,en; a literal
/// <c>\n</c> breaks a line), so localizing later means adding a column, not touching code.
/// </summary>
public static class Text
{
    static Dictionary<string, string>? table;

    public static string Get(string key)
    {
        table ??= Load("res://assets/text/en.csv");
        if (!table.TryGetValue(key, out var s)) return key;
        return s.Contains("[[") ? KeyToken.Replace(s, KeyName) : s;
    }

    /// <summary>The row as written, <c>[[Action]]</c> tokens unresolved (the hint notes draw them as key caps).</summary>
    public static string Raw(string key)
    {
        table ??= Load("res://assets/text/en.csv");
        return table.TryGetValue(key, out var s) ? s : key;
    }

    /// <summary><c>[[Dock]]</c> in a string names the key bound to that action now (rebinding changes the text too).</summary>
    static readonly Regex KeyToken = new(@"\[\[(\w+)\]\]", RegexOptions.Compiled);
    static string KeyName(Match m) =>
        Settings.Actions.Contains(m.Groups[1].Value) ? Settings.Label(Settings.Current.KeyFor(m.Groups[1].Value)) : m.Value;

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);

    public static bool Has(string key)
    {
        table ??= Load("res://assets/text/en.csv");
        return table.ContainsKey(key);
    }

    static Dictionary<string, string> Load(string path)
    {
        var result = new Dictionary<string, string>();
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"string table missing: {path}");
            return result;
        }
        bool header = true;
        while (!file.EofReached())
        {
            var row = file.GetCsvLine();
            if (header)
            {
                header = false;
                continue;
            }
            if (row.Length < 2 || row[0].Length == 0)
                continue;
            result[row[0]] = row[1].Replace("\\n", "\n");
        }
        return result;
    }
}
