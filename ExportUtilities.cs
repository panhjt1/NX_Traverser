using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// Independent of NX so data handling can be regression tested without its SDK.
public static class ExportUtilities
{
    public static string CreateRunDirectory(string root, DateTime startedAt)
    {
        string minute = startedAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(root);
        // Claim the final name by moving a staging directory, without merging
        // outputs from concurrent NX sessions. The log placeholder also makes
        // the directory nonempty (Unix rename can replace an empty directory).
        string staging = Path.Combine(root, ".NX_Traverser_" + Guid.NewGuid().ToString("N") + ".tmp");
        Directory.CreateDirectory(staging);
        try
        {
            using (File.Create(Path.Combine(staging, "Traversal.log"))) { }
            for (int number = 1; ; ++number)
            {
                string name = minute + (number == 1 ? "" : "_" + number.ToString(CultureInfo.InvariantCulture));
                string output = Path.Combine(root, name);
                if (Directory.Exists(output) || File.Exists(output)) continue;
                try { Directory.Move(staging, output); return output; }
                catch (IOException)
                {
                    // Retry only a name collision, not permissions/disk errors.
                    if (!Directory.Exists(output) && !File.Exists(output)) throw;
                }
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                File.Delete(Path.Combine(staging, "Traversal.log"));
                Directory.Delete(staging, false);
            }
        }
    }

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

    // Convert a prototype ACS/WCS into a temporary CSYS in the original work
    // assembly. UF matrix rows are the local X/Y/Z axes in absolute space.
    public static void FrameInAssembly(double[] localOrigin, double[] localAxes,
        double[,] occurrence, out double[] origin, out double[] axes)
    {
        if (localOrigin == null || localOrigin.Length != 3 || localAxes == null || localAxes.Length != 9)
            throw new ArgumentException("无效的测量坐标系。");
        var identity = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        double[,] inverse = PrototypeViewInAssembly(identity, occurrence);
        var localFrame = new double[4, 4];
        for (int r = 0; r < 3; ++r)
            for (int c = 0; c < 3; ++c) localFrame[r, c] = localAxes[c * 3 + r];
        double[,] localInverse = PrototypeViewInAssembly(identity, localFrame);
        origin = new double[3]; axes = new double[9];
        for (int r = 0; r < 3; ++r)
        {
            origin[r] = occurrence[r, 3];
            for (int k = 0; k < 3; ++k) origin[r] += occurrence[r, k] * localOrigin[k];
            if (double.IsNaN(origin[r]) || double.IsInfinity(origin[r]))
                throw new ArgumentException("测量坐标系原点包含无效值。");
            for (int axis = 0; axis < 3; ++axis)
                for (int k = 0; k < 3; ++k) axes[axis * 3 + r] += inverse[k, r] * localInverse[axis, k];
        }
    }

    public static double UnitScale(string assemblyUnits, string targetUnits)
    {
        return MillimetersPerUnit(assemblyUnits) / MillimetersPerUnit(targetUnits);
    }

    private static double MillimetersPerUnit(string units)
    {
        if (units == "Millimeters") return 1;
        if (units == "Inches") return 25.4;
        throw new ArgumentException("未知部件单位: " + units);
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
