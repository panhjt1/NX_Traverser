using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// Independent of NX so data handling can be regression tested without its SDK.
public static class ExportUtilities
{
    // View matrices map model -> view; occurrence transforms map prototype ->
    // assembly (column-vector convention). C * inverse(R) keeps prototype views.
    public static double[,] PrototypeViewInAssembly(double[,] camera, double[,] occurrence)
    {
        if (camera == null || camera.GetLength(0) != 3 || camera.GetLength(1) != 3 ||
            occurrence == null || occurrence.GetLength(0) != 4 || occurrence.GetLength(1) != 4)
            throw new ArgumentException("无效的截图旋转矩阵。");
        var rotation = new double[3, 3];
        double scale = 0;
        for (int c = 0; c < 3; ++c)
        {
            double length = 0;
            for (int r = 0; r < 3; ++r) length += occurrence[r, c] * occurrence[r, c];
            length = Math.Sqrt(length);
            if (double.IsNaN(length) || double.IsInfinity(length) || length < 1e-12)
                throw new ArgumentException("组件旋转矩阵包含无效值。");
            if (c == 0) scale = length;
            if (Math.Abs(length / scale - 1) > 1e-7) throw new ArgumentException("不支持非均匀缩放组件的原型视角截图。");
            for (int r = 0; r < 3; ++r) rotation[r, c] = occurrence[r, c] / length;
        }
        for (int a = 0; a < 3; ++a)
            for (int b = a + 1; b < 3; ++b)
            {
                double dot = 0;
                for (int r = 0; r < 3; ++r) dot += rotation[r, a] * rotation[r, b];
                if (Math.Abs(dot) > 1e-7) throw new ArgumentException("不支持剪切组件的原型视角截图。");
            }
        double det = rotation[0, 0] * (rotation[1, 1] * rotation[2, 2] - rotation[1, 2] * rotation[2, 1]) -
            rotation[0, 1] * (rotation[1, 0] * rotation[2, 2] - rotation[1, 2] * rotation[2, 0]) +
            rotation[0, 2] * (rotation[1, 0] * rotation[2, 1] - rotation[1, 1] * rotation[2, 0]);
        if (det < 0) throw new ArgumentException("镜像组件不能通过视图旋转还原原型视角，请使用非镜像实例。");
        var result = new double[3, 3];
        for (int r = 0; r < 3; ++r)
            for (int c = 0; c < 3; ++c)
                for (int k = 0; k < 3; ++k)
                {
                    if (double.IsNaN(camera[r, k]) || double.IsInfinity(camera[r, k]))
                        throw new ArgumentException("视图旋转矩阵包含无效值。");
                    result[r, c] += camera[r, k] * rotation[c, k];
                }
        return result;
    }

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
