#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Lossless line-based BepInEx configuration editing without loading plugin code.</summary>
internal sealed class ConfigurationDocument
{
    internal readonly struct Key : IEquatable<Key>
    {
        internal readonly string Section, Name;
        internal Key(string section, string name) { Section = section; Name = name; }
        public bool Equals(Key other) => Section == other.Section && Name == other.Name;
        public override bool Equals(object? other) => other is Key key && Equals(key);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Section) * 397 ^ StringComparer.Ordinal.GetHashCode(Name);
        public override string ToString() => "[" + Section + "] " + Name;
    }

    internal sealed class Edit
    {
        internal Key Key;
        internal string Value = "";
        internal string? Baseline;
        internal bool BaselineExists;
    }

    internal sealed class Conflict
    {
        internal Key Key;
        internal string? Baseline, External;
        internal string Target = "";
    }

    private readonly List<string> _lines;
    private readonly string _newline;
    private readonly bool _terminalNewline;
    private readonly Dictionary<Key, int> _entries = new();
    private readonly Dictionary<Key, string> _values = new();

    internal ConfigurationDocument(string text)
    {
        _newline = text.Contains("\r\n") ? "\r\n" : text.Contains("\r") ? "\r" : "\n";
        _terminalNewline = text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal);
        _lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (_terminalNewline) _lines.RemoveAt(_lines.Count - 1);
        if (text.Length == 0) _lines.Clear();
        Index();
    }

    internal bool TryGet(Key key, out string value) => _values.TryGetValue(key, out value!);

    // Only overlapping edits conflict. Unrelated external changes remain untouched.
    internal IReadOnlyList<Conflict> Conflicts(IEnumerable<Edit> edits)
    {
        var result = new List<Conflict>();
        var seen = new HashSet<Key>();
        foreach (var edit in edits)
        {
            Validate(edit.Key, edit.Value);
            if (!seen.Add(edit.Key)) throw new InvalidDataException("Duplicate configuration edit: " + edit.Key);
            bool exists = TryGet(edit.Key, out string external);
            if (exists && external == edit.Value) continue;
            if (exists != edit.BaselineExists || exists && external != edit.Baseline)
                result.Add(new Conflict { Key = edit.Key, Baseline = edit.BaselineExists ? edit.Baseline : null, External = exists ? external : null, Target = edit.Value });
        }
        return result;
    }

    internal string Merge(IEnumerable<Edit> edits, ISet<Key>? overwriteConflicts = null)
    {
        var changes = edits.ToList();
        var conflicts = Conflicts(changes);
        var blocked = conflicts.Where(c => overwriteConflicts == null || !overwriteConflicts.Contains(c.Key)).ToList();
        if (blocked.Count > 0) throw new InvalidDataException("Configuration values changed externally: " + string.Join(", ", blocked.Select(c => c.Key.ToString())));
        // Work on a copy: rejected edits never partially mutate this document.
        var output = new ConfigurationDocument(ToString());
        foreach (var edit in changes) output.Set(edit.Key, edit.Value);
        return output.ToString();
    }

    private void Set(Key key, string value)
    {
        Validate(key, value);
        if (_entries.TryGetValue(key, out int row))
        {
            string line = _lines[row]; int equals = line.IndexOf('=');
            string tail = line.Substring(equals + 1);
            int spaces = tail.Length - tail.TrimStart(' ', '\t').Length;
            _lines[row] = line.Substring(0, equals + 1) + tail.Substring(0, spaces) + value;
        }
        else
        {
            int insertion = -1;
            bool active = key.Section.Length == 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                string trimmed = _lines[i].Trim().TrimStart('\uFEFF');
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    if (active) { insertion = i; break; }
                    active = trimmed.Substring(1, trimmed.Length - 2) == key.Section;
                }
                if (active) insertion = i + 1;
            }
            if (active && insertion < 0) insertion = _lines.Count;
            if (insertion < 0)
            {
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Length > 0) _lines.Add("");
                if (key.Section.Length > 0) _lines.Add("[" + key.Section + "]");
                insertion = _lines.Count;
            }
            _lines.Insert(insertion, key.Name + " = " + value);
        }
        Index();
    }

    private void Index()
    {
        _entries.Clear(); _values.Clear(); string section = "";
        for (int i = 0; i < _lines.Count; i++)
        {
            string line = _lines[i], trimmed = line.Trim().TrimStart('\uFEFF');
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) { section = trimmed.Substring(1, trimmed.Length - 2); continue; }
            int equals = line.IndexOf('='); if (equals < 1) continue;
            var key = new Key(section, line.Substring(0, equals).Trim().TrimStart('\uFEFF'));
            if (_entries.ContainsKey(key)) throw new InvalidDataException("Duplicate configuration key: " + key);
            _entries[key] = i; _values[key] = line.Substring(equals + 1).Trim();
        }
    }

    private static void Validate(Key key, string value)
    {
        if (key.Section == null || key.Name == null || value == null || key.Name.Length == 0 || key.Name != key.Name.Trim() || key.Section != key.Section.Trim()
            || key.Section.IndexOfAny(new[] { '\r', '\n', '[', ']', '\0' }) >= 0 || key.Name.IndexOfAny(new[] { '\r', '\n', '=', '\0' }) >= 0
            || key.Name.StartsWith("#", StringComparison.Ordinal) || key.Name.StartsWith(";", StringComparison.Ordinal) || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            throw new InvalidDataException("Invalid configuration key or serialized value.");
    }

    public override string ToString() => string.Join(_newline, _lines) + (_terminalNewline && _lines.Count > 0 ? _newline : "");
}
