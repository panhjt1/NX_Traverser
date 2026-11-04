using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

public sealed class BoundingBoxExporter : ITransactionHandler
{
    private readonly string _mode;
    private StreamWriter _writer;

    public BoundingBoxExporter(string mode)
    {
        _mode = ExportUtilities.NormalizeCoordinateMode(mode);
    }

    public void Process(Component comp, Session session, string outputFolder, string descriptiveName)
    {
        using (var context = new NxAssemblyExportContext(session, comp))
            Process(context, outputFolder, descriptiveName);
    }

    public void Process(NxAssemblyExportContext context, string outputFolder, string descriptiveName)
    {
        Component comp = context.Target;
        Part part = context.TargetPart;
        if (part == null) throw new InvalidOperationException("无法取得测量目标。");
        List<Tag> bodies = context.CollectVisibleBodyTags();
        UFSession uf = UFSession.GetUFSession();
        Tag temporaryCsys = Tag.Null, temporaryMatrix = Tag.Null;
        var localOrigin = new double[3];
        var localAxes = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        if (_mode == "WCS")
        {
            // Read the target's WCS without activating it or changing its WCS.
            Tag matrix;
            uf.Csys.AskCsysInfo(part.WCS.CoordinateSystem.Tag, out matrix, localOrigin);
            uf.Csys.AskMatrixValues(matrix, localAxes);
        }
        double unitScale = ExportUtilities.UnitScale(context.AssemblyPart.PartUnits.ToString(), part.PartUnits.ToString());
        // UF placement translation is in assembly units, WCS origin in target units.
        for (int i = 0; i < localOrigin.Length; ++i) localOrigin[i] /= unitScale;
        double[] origin, axes;
        ExportUtilities.FrameInAssembly(localOrigin, localAxes, context.Transform, out origin, out axes);
        var box = new BoundingBoxMath();
        try
        {
            // Occurrence geometry and query CSYS must both be in current work
            // assembly space. NULL_TAG would mean its WCS, not the target ACS.
            uf.Csys.CreateMatrix(axes, out temporaryMatrix);
            uf.Csys.CreateTempCsys(origin, temporaryMatrix, out temporaryCsys);
            foreach (Tag body in bodies)
            {
                if (AssemblyTraverser.StopRequested) throw new OperationCanceledException("遍历已停止。");
                var corner = new double[3];
                var directions = new double[3, 3];
                var distances = new double[3];
                RunLog.Write("[BoundingBox " + _mode + "] body=" + body);
                NxPartResources.BeforeLoad(null); // Exact query may retrieve geometry internally.
                RunLog.NxCall("AskBoundingBoxExact occurrence=" + body,
                    () => uf.Modl.AskBoundingBoxExact(body, temporaryCsys, corner, directions, distances));
                // Query aligned geometry directly; rotating an ACS AABB is not exact.
                box.Add(corner, directions, distances, origin, axes);
            }
        }
        finally
        {
            // Only delete temporary objects created here; never delete the actual WCS.
            var cleanupErrors = new List<Exception>();
            foreach (Tag tag in new[] { temporaryCsys, temporaryMatrix })
            {
                try { if (tag != Tag.Null) uf.Obj.DeleteObject(tag); }
                catch (Exception ex) { cleanupErrors.Add(ex); }
            }
            if (cleanupErrors.Count > 0)
            {
                AssemblyTraverser.RequestStop();
                NxPartResources.KeepCurrentPartsOpen();
                foreach (Exception ex in cleanupErrors) RunLog.Error("删除临时测量坐标系/矩阵失败", ex);
                throw new InvalidOperationException("临时测量对象未完整清理，已停止。", cleanupErrors[0]);
            }
        }
        double[] dimensions = box.Dimensions();
        for (int i = 0; i < dimensions.Length; ++i) dimensions[i] *= unitScale;
        if (_writer == null)
        {
            _writer = new StreamWriter(Path.Combine(outputFolder, "BoundingBoxResult.csv"), false, new UTF8Encoding(true));
            _writer.WriteLine("零件名,ID,x,y,z,单位,坐标系,范围,实体数");
        }
        _writer.WriteLine(string.Join(",", new[] {
            ExportUtilities.CsvCell(AssemblyTraverserUtils.GetDescriptiveName(comp)),
            ExportUtilities.CsvCell(comp.DisplayName),
            ExportUtilities.Dimension(dimensions[0]), ExportUtilities.Dimension(dimensions[1]), ExportUtilities.Dimension(dimensions[2]),
            ExportUtilities.CsvCell(part.PartUnits.ToString()), _mode, "DisplayedOccurrenceBodies",
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
