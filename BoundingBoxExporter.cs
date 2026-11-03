using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

public sealed class BoundingBoxExporter : ITransactionHandler
{
    public bool RequiresPrototypeContext { get { return true; } }
    private readonly string _mode;
    private StreamWriter _writer;

    public BoundingBoxExporter(string mode)
    {
        _mode = ExportUtilities.NormalizeCoordinateMode(mode);
    }

    public void Process(Component comp, Session session, string outputFolder, string descriptiveName)
    {
        Part part = comp.Prototype as Part;
        if (part == null || session.Parts.Work != part || session.Parts.Display != part)
            throw new InvalidOperationException("测量必须在目标原型部件上下文中执行。");
        List<Tag> bodies = NxGeometry.CollectBodyTags(part);
        if (bodies.Count == 0) throw new InvalidOperationException("没有可测量实体。");
        UFSession uf = UFSession.GetUFSession();
        Tag temporaryCsys = Tag.Null, temporaryMatrix = Tag.Null;
        var origin = new double[3];
        var axes = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        var box = new BoundingBoxMath();
        try
        {
            Tag csys;
            if (_mode == "WCS")
            {
                csys = part.WCS.CoordinateSystem.Tag;
                Tag matrix;
                uf.Csys.AskCsysInfo(csys, out matrix, origin);
                uf.Csys.AskMatrixValues(matrix, axes);
            }
            else
            {
                // NULL_TAG means current WCS, NOT absolute coordinates.
                // A temporary identity CSYS avoids changing/saving the source WCS.
                uf.Csys.CreateMatrix(axes, out temporaryMatrix);
                uf.Csys.CreateTempCsys(origin, temporaryMatrix, out temporaryCsys);
                csys = temporaryCsys;
            }
            foreach (Tag body in bodies)
            {
                if (AssemblyTraverser.StopRequested) throw new OperationCanceledException("遍历已停止。");
                var corner = new double[3];
                var directions = new double[3, 3];
                var distances = new double[3];
                RunLog.Write("[BoundingBox " + _mode + "] body=" + body);
                uf.Modl.AskBoundingBoxExact(body, csys, corner, directions, distances);
                // Query aligned geometry directly; rotating an ACS AABB is not exact.
                box.Add(corner, directions, distances, origin, axes);
            }
        }
        finally
        {
            // Only delete temporary objects created here; never delete the actual WCS.
            try { if (temporaryCsys != Tag.Null) uf.Obj.DeleteObject(temporaryCsys); }
            finally { if (temporaryMatrix != Tag.Null) uf.Obj.DeleteObject(temporaryMatrix); }
        }
        double[] dimensions = box.Dimensions();
        if (_writer == null)
        {
            _writer = new StreamWriter(Path.Combine(outputFolder, "BoundingBoxResult.csv"), false, new UTF8Encoding(true));
            _writer.WriteLine("零件名,ID,x,y,z,单位,坐标系,范围,实体数");
        }
        _writer.WriteLine(string.Join(",", new[] {
            ExportUtilities.CsvCell(AssemblyTraverserUtils.GetDescriptiveName(comp)),
            ExportUtilities.CsvCell(comp.DisplayName),
            ExportUtilities.Dimension(dimensions[0]), ExportUtilities.Dimension(dimensions[1]), ExportUtilities.Dimension(dimensions[2]),
            ExportUtilities.CsvCell(part.PartUnits.ToString()), _mode, "PrototypeWithDescendants",
            bodies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _writer.Flush();
        RunLog.Write("[Dimensions] " + comp.DisplayName + " " + string.Join(" x ", new[] {
            ExportUtilities.Dimension(dimensions[0]), ExportUtilities.Dimension(dimensions[1]), ExportUtilities.Dimension(dimensions[2])
        }) + " " + part.PartUnits + " " + _mode);
    }

    public void Finish()
    {
        if (_writer == null) return;
        try { _writer.Dispose(); }
        finally { _writer = null; }
    }
}
