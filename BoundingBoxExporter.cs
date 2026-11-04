using System;
using System.IO;
using System.Text;

public sealed class BoundingBoxExporter : ITransactionHandler
{
    private readonly string _mode;
    private StreamWriter _writer;

    public BoundingBoxExporter(string mode)
    {
        _mode = ExportUtilities.NormalizeCoordinateMode(mode);
    }

    public void Process(TransactionContext context)
    {
        BoundingBoxResult result = context.MeasureBoundingBox(_mode);
        context.CheckCancellation();
        if (_writer == null)
        {
            _writer = new StreamWriter(Path.Combine(context.OutputFolder, "BoundingBoxResult.csv"), false, new UTF8Encoding(true));
            _writer.WriteLine("零件名,ID,x,y,z,单位,坐标系,范围,实体数");
        }
        _writer.WriteLine(string.Join(",", new[] {
            ExportUtilities.CsvCell(context.Name),
            ExportUtilities.CsvCell(context.Id),
            ExportUtilities.Dimension(result.X), ExportUtilities.Dimension(result.Y), ExportUtilities.Dimension(result.Z),
            ExportUtilities.CsvCell(result.Units), result.CoordinateMode, "DisplayedOccurrenceBodies",
            result.BodyCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        _writer.Flush();
        context.Log("[Dimensions] " + context.Id + " " + string.Join(" x ", new[] {
            ExportUtilities.Dimension(result.X), ExportUtilities.Dimension(result.Y), ExportUtilities.Dimension(result.Z)
        }) + " " + result.Units + " " + _mode);
    }

    public void Finish()
    {
        if (_writer == null) return;
        try { _writer.Dispose(); }
        finally { _writer = null; }
    }
}
