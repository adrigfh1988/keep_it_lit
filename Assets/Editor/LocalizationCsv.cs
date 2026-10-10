using System.Collections.Generic;
using System.Text;

/// <summary>
/// S9 (D4): the one RFC 4180 CSV reader / writer used by <see cref="LocalizationImporter"/> - quoted fields, "" escapes, embedded newlines,
/// UTF-8 without BOM (a BOM, if a spreadsheet added one, is skipped on read). Rows come back as string arrays; blank lines are skipped.
/// </summary>
public static class LocalizationCsv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;
        if (text[0] == '﻿') text = text.Substring(1);

        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"') { quoted = true; any = true; }
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); any = true; }
            else if (c == '\r') { /* swallowed; the \n ends the row */ }
            else if (c == '\n')
            {
                if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
                row.Clear(); field.Clear(); any = false;
            }
            else { field.Append(c); any = true; }
        }
        if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    public static string Write(IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        foreach (string[] row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(row[i]));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string Escape(string s)
    {
        s ??= "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
