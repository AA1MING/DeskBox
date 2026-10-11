using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>Shared, culture-independent keys for alphabetical ordering and search.</summary>
public static class NameService
{
    private sealed record Keys(string Full, string Initials, int Category);
    private static readonly ConcurrentDictionary<string, Keys> Cache = new(StringComparer.Ordinal);
    private static readonly Lazy<Dictionary<int, string>> Readings = new(LoadReadings);
    private static readonly Dictionary<string, string[]> Phrases = new()
    {
        ["重庆"] = new[] { "chong", "qing" }, ["音乐"] = new[] { "yin", "yue" },
        ["网易"] = new[] { "wang", "yi" }, ["银行"] = new[] { "yin", "hang" },
        ["行业"] = new[] { "hang", "ye" }, ["重新"] = new[] { "chong", "xin" },
        ["重复"] = new[] { "chong", "fu" }, ["长安"] = new[] { "chang", "an" },
        ["长沙"] = new[] { "chang", "sha" }, ["长城"] = new[] { "chang", "cheng" },
        ["厦门"] = new[] { "xia", "men" }
    };
    public static IComparer<string> Comparer { get; } = System.Collections.Generic.Comparer<string>.Create(Compare);

    private static Dictionary<int, string> LoadReadings()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskBox.Pinyin.tsv")
            ?? throw new InvalidOperationException("Missing embedded pinyin data.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = new Dictionary<int, string>();
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                result[code] = parts[1];
        }
        return result;
    }

    private static Keys GetKeys(string name) => Cache.GetOrAdd(name, value =>
    {
        var full = new StringBuilder();
        var initials = new StringBuilder();
        int category = 3;
        bool classified = false;
        for (int index = 0; index < value.Length;)
        {
            string[]? phrase = null;
            foreach (var entry in Phrases)
            {
                if (value.AsSpan(index).StartsWith(entry.Key, StringComparison.Ordinal)) { phrase = entry.Value; break; }
            }
            var rune = Rune.GetRuneAt(value, index);
            bool chinese = Readings.Value.TryGetValue(rune.Value, out string? reading);
            if (!classified && (chinese || Rune.IsLetterOrDigit(rune)))
            {
                category = chinese ? 1 : Rune.IsDigit(rune) ? 2 : 0;
                classified = true;
            }
            if (phrase != null)
            {
                foreach (var syllable in phrase) { full.Append(syllable); initials.Append(syllable[0]); }
                index += phrase.Length;
                continue;
            }
            if (chinese)
            {
                full.Append(reading);
                initials.Append(reading![0]);
            }
            else if (Rune.IsLetterOrDigit(rune))
            {
                string part = rune.ToString().ToLowerInvariant();
                full.Append(part);
                initials.Append(part);
            }
            index += rune.Utf16SequenceLength;
        }
        return new Keys(full.ToString(), initials.ToString(), category);
    });

    public static bool Matches(string name, string query)
    {
        var keys = GetKeys(name);
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(term =>
        {
            string normalized = string.Concat(term.Where(char.IsLetterOrDigit)).ToLowerInvariant().Replace('ü', 'v');
            return name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (normalized.Length > 0 && (keys.Full.Contains(normalized, StringComparison.Ordinal) || keys.Initials.Contains(normalized, StringComparison.Ordinal)));
        });
    }

    public static IEnumerable<DeskItem> Search(IEnumerable<DeskItem> roots, string query)
    {
        foreach (var item in roots)
        {
            if (Matches(item.Name, query) || (!string.IsNullOrWhiteSpace(item.StoredName) && Matches(Path.GetFileName(item.StoredName), query))) yield return item;
            foreach (var child in Search(item.Children, query)) yield return child;
        }
    }

    public static List<DeskItem> Sort(IEnumerable<DeskItem> items, string mode) => mode switch
    {
        "Name" => items.OrderBy(i => i.IsGroup ? 0 : 1).ThenBy(i => i.Name, Comparer).ToList(),
        "Type" => items.OrderBy(i => (int)i.Kind).ThenBy(i => i.Name, Comparer).ToList(),
        "Frequent" => items.OrderByDescending(i => i.ClickCount).ThenBy(i => i.Name, Comparer).ToList(),
        _ => items.ToList()
    };

    private static int Compare(string? left, string? right)
    {
        left ??= string.Empty; right ??= string.Empty;
        var a = GetKeys(left); var b = GetKeys(right);
        int category = a.Category.CompareTo(b.Category);
        if (category != 0) return category;
        int natural = NaturalCompare(a.Full, b.Full);
        return natural != 0 ? natural : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int aStart = i, bStart = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                while (aStart < i - 1 && a[aStart] == '0') aStart++;
                while (bStart < j - 1 && b[bStart] == '0') bStart++;
                int length = (i - aStart).CompareTo(j - bStart);
                if (length != 0) return length;
                int digits = a.AsSpan(aStart, i - aStart).SequenceCompareTo(b.AsSpan(bStart, j - bStart));
                if (digits != 0) return digits;
            }
            else
            {
                int letter = a[i++].CompareTo(b[j++]);
                if (letter != 0) return letter;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
