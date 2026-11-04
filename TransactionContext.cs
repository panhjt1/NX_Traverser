using System;
using NXOpen;
using NXOpen.Assemblies;

/// <summary>
/// Borrowed per-target input and shared services. Created/closed by the driver,
/// never by a transaction. Display preparation is lazy and shared per target.
/// </summary>
public sealed class TransactionContext
{
    private NxAssemblyExportContext _assembly;
    private bool _closed;
    private string _fileStem;
    private readonly Part _work, _display;
    private readonly Component _workComponent;
    public Session Session { get; private set; }
    public Component Target { get; private set; }
    public Part TargetPart { get; private set; }
    public string OutputFolder { get; private set; }
    public string Id { get; private set; }
    public string Name { get; private set; }
    public string DescriptiveName { get; private set; }
    public string FileStem
    {
        get
        {
            if (_closed) throw new ObjectDisposedException("TransactionContext");
            if (_fileStem == null)
                _fileStem = ExportUtilities.SafeFileStem(DescriptiveName, TargetPart.FullPath + "|" + DescriptiveName);
            return _fileStem;
        }
    }

    internal TransactionContext(Session session, Component target, Part part,
        string outputFolder, string id, string name)
    {
        Session = session; Target = target; TargetPart = part;
        _work = session.Parts.Work; _display = session.Parts.Display; _workComponent = session.Parts.WorkComponent;
        OutputFolder = outputFolder; Id = id; Name = name;
        DescriptiveName = id + "_" + name;
    }

    private NxAssemblyExportContext Assembly
    {
        get
        {
            CheckCancellation();
            if (_assembly == null) _assembly = new NxAssemblyExportContext(Session, Target);
            return _assembly;
        }
    }

    public void CheckCancellation()
    {
        if (_closed) throw new ObjectDisposedException("TransactionContext");
        TraversalCancellation.Checkpoint();
    }

    public void Log(string message) { RunLog.Write(message); }

    // Long custom transactions use this for NX operations and CheckCancellation
    // between their own work units. Cleanup belongs in their own finally blocks.
    public void NxCall(string stage, Action action)
    {
        CheckCancellation();
        TraversalCancellation.NxCall(stage, action);
    }

    public void PrepareImageView(View.Canned view) { Assembly.PrepareImageView(view); }

    public BoundingBoxResult MeasureBoundingBox(string mode)
    {
        string normalized = ExportUtilities.NormalizeCoordinateMode(mode);
        return NxGeometry.Measure(Assembly, normalized);
    }

    internal void Execute(ITransactionHandler handler)
    {
        Exception failure = null;
        try { CheckCancellation(); handler.Process(this); }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            try
            {
                if (_assembly != null) { _assembly.RestoreFrame(); _assembly.AssertContext(); }
                if (Session.Parts.Work != _work || Session.Parts.Display != _display || Session.Parts.WorkComponent != _workComponent)
                    throw new InvalidOperationException("事务改变了原工作/显示上下文，已停止后续处理。");
            }
            catch
            {
                AssemblyTraverser.RequestStop();
                NxPartResources.KeepCurrentPartsOpen();
                if (failure != null) RunLog.Error("上下文恢复失败前的事务异常", failure);
                throw;
            }
        }
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        if (_assembly != null) _assembly.Dispose();
    }
}

/// <summary>Exact displayed-body dimensions in the target part's units.</summary>
public sealed class BoundingBoxResult
{
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Z { get; private set; }
    public string Units { get; private set; }
    public string CoordinateMode { get; private set; }
    public int BodyCount { get; private set; }
    internal BoundingBoxResult(double[] dimensions, string units, string mode, int bodyCount)
    {
        X = dimensions[0]; Y = dimensions[1]; Z = dimensions[2];
        Units = units; CoordinateMode = mode; BodyCount = bodyCount;
    }
}
