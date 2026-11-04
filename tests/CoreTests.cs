using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

internal static class CoreTests
{
    private static int _checks;
    private static readonly double[] Identity = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
    private static readonly double[,] IdentityMatrix = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    private static void Assert(bool condition, string description)
    {
        ++_checks;
        if (!condition) throw new Exception(description);
    }

    private static void Size(BoundingBoxMath box, double x, double y, double z)
    {
        double[] actual = box.Dimensions(), expected = { x, y, z };
        for (int i = 0; i < 3; ++i) Assert(Math.Abs(actual[i] - expected[i]) < 1e-8, "Incorrect dimension " + i);
    }

    private static void Throws(Action action, string description)
    {
        bool failed = false;
        try { action(); } catch (ArgumentException) { failed = true; }
        Assert(failed, description);
    }

    public static void Main()
    {
        var box = new BoundingBoxMath();
        box.Add(new[] { -10.0, 2, 4 }, IdentityMatrix, new[] { 10.0, 2, 3 }, new double[3], Identity);
        box.Add(new[] { 5.0, -2, 1 }, IdentityMatrix, new[] { 2.0, 1, 1 }, new double[3], Identity);
        Size(box, 17, 6, 6);

        // A 10x2 rectangle aligned to a 30-degree WCS must remain 10x2.
        // Rotating its ACS AABB instead would overestimate both dimensions.
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var axes = new[] { c, s, 0, -s, c, 0, 0, 0, 1.0 };
        var directions = new double[,] { { c, s, 0 }, { -s, c, 0 }, { 0, 0, 1 } };
        var origin = new[] { 100.0, -50, 30 };
        var rotated = new BoundingBoxMath();
        rotated.Add(origin, directions, new[] { 10.0, 2, 3 }, origin, axes);
        Size(rotated, 10, 2, 3);
        rotated.Add(new[] { origin[0] + 12 * c, origin[1] + 12 * s, origin[2] },
            directions, new[] { 3.0, 2, 3 }, origin, axes);
        Size(rotated, 15, 2, 3);
        Throws(() => rotated.Add(origin, directions, new[] { double.NaN, 1, 2 }, origin, axes), "NaN must fail");
        Throws(() => rotated.Add(origin, directions, new[] { -1.0, 1, 2 }, origin, axes), "Negative length must fail");
        Size(rotated, 15, 2, 3);
        Assert(rotated.Count == 2, "Invalid body must not corrupt accumulator");
        var remote = new BoundingBoxMath();
        remote.Add(new[] { 2e6, 3e6, -4e6 }, IdentityMatrix, new[] { 10.0, 20, 30 }, new double[3], Identity);
        Size(remote, 10, 20, 30);

        Assert(ExportUtilities.NormalizeCoordinateMode("工作坐标系") == "WCS", "Chinese WCS");
        Assert(ExportUtilities.NormalizeCoordinateMode("绝对坐标系") == "ACS", "Chinese ACS");
        Throws(() => ExportUtilities.NormalizeCoordinateMode("bogus"), "Unknown coordinates must fail");
        string[] patterns = ExportUtilities.NormalizePatterns(new[] { "  ", null, " *总成*\r\n A? ", "A?" });
        Assert(patterns.Length == 2 && patterns[0] == "*总成*" && patterns[1] == "A?", "Multiline pattern normalization");
        Assert(ExportUtilities.CsvCell("件,\"A\"\r\nB") == "\"件,\"\"A\"\"\r\nB\"", "CSV escaping");
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        Assert(ExportUtilities.Dimension(12.5) == "12.500", "CSV decimal must be invariant");
        Assert(ExportUtilities.SafeFileStem("CON", "1").StartsWith("part_CON_"), "Windows device name");
        string first = ExportUtilities.SafeFileStem("a/b", "a/b"), second = ExportUtilities.SafeFileStem("a:b", "a:b");
        Assert(first != second, "Sanitized names must not collide");
        Assert(first.IndexOf('/') < 0 && second.IndexOf(':') < 0, "Windows filename characters");
        Assert(ExportUtilities.SafeFileStem(new string('x', 300), "long").Length < 100, "Bound filename length");
        OutputDirectoryRegression();
        LifetimeRegression();
        _checks += PartResourceTests.Run();
        _checks += ImageViewTests.Run() + BoundingBoxExportTests.Run() + TraversalCancellationTests.Run() + TransactionContractTests.Run();
        Console.WriteLine("PASS: " + _checks + " core regression checks");
    }

    private static void LifetimeRegression()
    {
        var lifetime = new PartLifetime<string>();
        lifetime.RegisterInitial("fully loaded", true);
        lifetime.RegisterInitial("unsaved edit", true);
        lifetime.RegisterInitial("clean partial", false);
        lifetime.RegisterInitial("untouched partial", false);
        lifetime.Touch("fully loaded");
        lifetime.Touch("unsaved edit");
        lifetime.Touch("clean partial");
        Assert(!lifetime.CanRelease("fully loaded"), "Keep preexisting full geometry");
        Assert(!lifetime.CanRelease("unsaved edit"), "Never discard preexisting edits");
        Assert(!lifetime.CanRelease("untouched partial"), "Do not sweep untouched partial parts");
        Assert(lifetime.CanRelease("clean partial"), "Release temporary promotion of clean partial part");
        Assert(lifetime.CanRelease("opened during failed load"), "Recover dependencies opened before a load throws");

        lifetime.Pin("new shared ancestor");
        lifetime.Pin("new shared ancestor");
        lifetime.Unpin("new shared ancestor");
        Assert(!lifetime.CanRelease("new shared ancestor"), "Shared ancestor remains pinned by parent frame");
        lifetime.Unpin("new shared ancestor");
        Assert(lifetime.CanRelease("new shared ancestor"), "Release ancestor when final frame exits");
        lifetime.Released("new shared ancestor");
        Assert(lifetime.CanRelease("new shared ancestor"), "A newly loaded part stays temporary if reopened");

        lifetime.Protect("clean partial");
        Assert(!lifetime.CanRelease("clean partial"), "Active work/display protection wins over touch");
        lifetime.RegisterInitial("partial repeated", false);
        lifetime.Touch("partial repeated");
        lifetime.Released("partial repeated");
        Assert(!lifetime.CanRelease("partial repeated"), "Released initial part needs a new touch");
        lifetime.Touch("partial repeated");
        Assert(lifetime.CanRelease("partial repeated"), "Repeated occurrence promotion can be released again");
    }

    private static void OutputDirectoryRegression()
    {
        string root = Path.Combine(Path.GetTempPath(), "nx-output-path-tests-" + Guid.NewGuid().ToString("N"));
        var minute = new DateTime(2026, 11, 4, 13, 0, 1);
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            string first = ExportUtilities.CreateRunDirectory(root, minute);
            File.WriteAllText(Path.Combine(first, "existing.txt"), "keep");
            string second = ExportUtilities.CreateRunDirectory(root, minute.AddSeconds(45));
            Assert(Path.GetFileName(first) == "202611041300" && Path.GetFileName(second) == "202611041300_2",
                "Local start minute, invariant calendar and collision suffix");
            File.WriteAllText(Path.Combine(root, "202611041300_3"), "existing file");
            Assert(Path.GetFileName(ExportUtilities.CreateRunDirectory(root, minute)) == "202611041300_4",
                "A conflicting file must be preserved and skipped");
            Assert(File.ReadAllText(Path.Combine(first, "existing.txt")) == "keep" &&
                File.ReadAllText(Path.Combine(root, "202611041300_3")) == "existing file",
                "Existing exports and colliding files are never overwritten");
            Assert(Path.GetFileName(ExportUtilities.CreateRunDirectory(root, minute.AddMinutes(1))) == "202611041301",
                "A new minute starts with an unsuffixed name");
            var paths = new string[8];
            Parallel.For(0, paths.Length, i => paths[i] = ExportUtilities.CreateRunDirectory(root, minute.AddMinutes(2)));
            Assert(new System.Collections.Generic.HashSet<string>(paths).Count == paths.Length,
                "Concurrent runs never share an output directory");
            Assert(Directory.GetDirectories(root, ".NX_Traverser_*.tmp").Length == 0,
                "Successful allocation leaves only the timestamp directories");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
