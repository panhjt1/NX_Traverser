using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// Independent of NX so data handling can be regression tested without its SDK.
public static class ExportUtilities
{
    public static string NormalizeCoordinateMode(string value)
    {
        string mode = (value ?? "ACS").Trim();
        if (mode.Equals("ACS", StringComparison.OrdinalIgnoreCase) || mode == "绝对坐标系") return "ACS";
        if (mode.Equals("WCS", StringComparison.OrdinalIgnoreCase) || mode == "工作坐标系") return "WCS";
        throw new ArgumentException("未知坐标系: " + value);
    }

    public static string[] NormalizePatterns(string[] values)
    {
        var result = new List<string>();
        foreach (string value in values ?? new string[0])
            foreach (string line in (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (!string.IsNullOrWhiteSpace(line) && !result.Contains(line.Trim())) result.Add(line.Trim());
        return result.ToArray();
    }

    public static string CsvCell(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    public static string Dimension(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    public static string SafeFileStem(string name, string identity)
    {
        var buffer = new StringBuilder();
        foreach (char ch in name ?? "Unnamed")
            buffer.Append(ch < 32 || "<>:\"/\\|?*".IndexOf(ch) >= 0 ? '_' : ch);
        string stem = buffer.ToString().Trim(' ', '.');
        if (stem.Length == 0) stem = "Unnamed";
        if (stem.Length > 80) stem = stem.Substring(0, 80).TrimEnd(' ', '.');
        // Prefix avoids Windows device names; hash disambiguates sanitization.
        using (var hash = SHA256.Create())
        {
            byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(identity ?? name ?? string.Empty));
            return "part_" + stem + "_" + BitConverter.ToString(digest, 0, 6).Replace("-", "").ToLowerInvariant();
        }
    }
}
