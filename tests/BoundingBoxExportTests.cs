// NX doubles test selection, transforms and cleanup, not native exact geometry.
using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

internal static class BoundingBoxExportTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Bounding box regression: " + message); ++_checks; }
    private static void Near(double actual, double expected, string message)
    { Check(Math.Abs(actual - expected) < 1e-8, message); }
    private static void Fails(Action action, string message)
    { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Check(failed, message); }

    public static int Run()
    {
        string output = Path.Combine(Path.GetTempPath(), "nx-box-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            ScopeAndAuxiliary(output);
            CoordinateQueries(output);
            FailureAndCleanup(output);
            DiagnosticEvidence(output);
            TemporaryLifetime(output);
        }
        finally { Directory.Delete(output, true); }
        return _checks;
    }

    private static Session Setup(out Part root, out Component component)
    {
        Session session = ImageViewTests.Setup(out root, out component);
        var uf = UFSession.Instance;
        uf.Assem.Members.Clear(); uf.Modl.Vertices.Clear(); uf.Modl.Queried.Clear();
        uf.Modl.Fail = false; uf.Modl.AfterQuery = null; uf.Obj.Deleted.Clear(); uf.Obj.Fail = false;
        uf.Obj.States.Clear(); uf.Obj.StatusQueries.Clear(); uf.Obj.Refuse = Tag.Null;
        uf.Obj.States[(Tag)9002] = UFConstants.UF_OBJ_CONDEMNED; // Existing, NX-managed orientation support.
        uf.Obj.ErrorTag = Tag.Null; uf.Obj.DeleteBeforeError = false; uf.Obj.StatusError = false;
        uf.Csys.Origin = new double[3]; uf.Csys.Axes = new double[] { 1,0,0,0,1,0,0,0,1 };
        uf.Csys.SourceOrigin = new double[3]; uf.Csys.SourceAxes = (double[])uf.Csys.Axes.Clone();
        uf.Assem.Members[component.Tag] = new NXObject[] { root.Views.WorkView.Scene[0] };
        uf.Modl.Vertices[(Tag)3000] = Box(0, 0, 0, 2, 3, 4);
        return session;
    }

    private static double[][] Box(double x, double y, double z, double dx, double dy, double dz)
    {
        var result = new List<double[]>();
        for (int a=0;a<2;++a) for (int b=0;b<2;++b) for (int c=0;c<2;++c)
            result.Add(new[] { x+a*dx, y+b*dy, z+c*dz });
        return result.ToArray();
    }
    private static void Export(Component comp, Session session, string output, string mode)
    {
        TransactionTestScope.Run(new BoundingBoxExporter(mode), comp, session, output, "target");
    }
    private static string Row(string output) { return File.ReadAllLines(Path.Combine(output, "BoundingBoxResult.csv"))[1]; }

    private static void ScopeAndAuxiliary(string output)
    {
        Part root; Component comp;
        Session session = Setup(out root, out comp);
        var uf = UFSession.Instance;
        var hiddenBody = new Body { Tag=(Tag)3100, OwningComponent=comp, IsBlanked=true };
        var hiddenLayer = new Body { Tag=(Tag)3101, OwningComponent=comp, Layer=20 };
        root.Layers.Hidden.Add(20);
        var rearBody = new Body { Tag=(Tag)3102, OwningComponent=comp, Occluded=true };
        var outOfRefset = new Body { Tag=(Tag)3103, OwningComponent=comp };
        ((Part)comp.Prototype).Bodies.Add(outOfRefset); // Never enumerate prototype bodies.
        var hiddenChild = new Component { Tag=(Tag)3104, Parent=comp, IsBlanked=true }; // Unloaded.
        var suppressed = new Component { Tag=(Tag)3105, Parent=comp, IsSuppressed=true };
        comp.Children.Add(hiddenChild); comp.Children.Add(suppressed);
        var child = new Component { Tag=(Tag)3106, Parent=comp,
            Prototype=session.Add("visible child", PartLoadState.PartiallyLoaded) };
        comp.Children.Add(child);
        var childBody = new Body { Tag=(Tag)3107, OwningComponent=child };
        uf.Assem.Members[comp.Tag] = new NXObject[] { root.Views.WorkView.Scene[0], hiddenBody,
            hiddenLayer, rearBody };
        uf.Assem.Members[child.Tag] = new NXObject[] { childBody };
        uf.Modl.Vertices[rearBody.Tag] = Box(0,0,10,2,3,4);
        uf.Modl.Vertices[childBody.Tag] = Box(2,0,0,3,3,4);
        var auxiliaries = new DisplayableObject[] {
            new CartesianCoordinateSystem { Tag=(Tag)3200, OwningComponent=comp },
            new Point { Tag=(Tag)3201, OwningComponent=comp },
            new Sketch { Tag=(Tag)3202, OwningComponent=child },
            new DatumPlane { Tag=(Tag)3203, OwningComponent=comp },
            new Curve { Tag=(Tag)3204, OwningComponent=comp },
            new Point { Tag=(Tag)3205, OwningComponent=comp, IsBlanked=true }
        };
        root.Views.WorkView.Scene.AddRange(auxiliaries);
        root.Views.WorkView.Scene.Add(new Face { Tag=(Tag)3206, OwningComponent=comp,
            Body=(Body)root.Views.WorkView.Scene[0] });
        var exporter = new BoundingBoxExporter("ACS");
        using (var context = new TransactionTestScope(session, comp, output, "target"))
        {
            context.Process(exporter);
            Check(Row(output).Contains(",5.000,3.000,14.000,"), "Hidden/refset/layer excluded; occluded rear and child bodies included");
            Check(uf.Modl.Queried.Count==3 && !uf.Modl.Queried.Contains(outOfRefset.Tag), "Only occurrence member bodies queried");
            session.CaptureObserver = view => {
                foreach (DisplayableObject aux in auxiliaries) Check(aux.IsBlanked, "Auxiliary object hidden at capture");
                Check(!root.Views.WorkView.Scene[0].IsBlanked, "Face normalized to body and retained");
            };
            context.Process(new ImageExporter(new[] { ImageExporter.SnapViewType.Top }));
            Check(session.Calls.FindAll(c => c=="partial readonly prototype").Count==0,
                "Already-partially-loaded target needs no redundant partial load");
        }
        for(int i=0;i<auxiliaries.Length;++i) Check(auxiliaries[i].IsBlanked==(i==5), "Exact original auxiliary blank states restored");
        Check(hiddenBody.IsBlanked && hiddenChild.IsBlanked, "Hidden descendants stay hidden");
        Check(session.Calls.TrueForAll(c=>c.StartsWith("partial ", StringComparison.Ordinal)), "No activation or full loading in either handler");
        Check(session.Parts.Work==root && session.Parts.Display==root, "Original work and display preserved");
        Check(root.CoordinateSystems.LastTemporary && root.CoordinateSystems.Created.Count==1,
            "Query frame is created as a non-displayed/non-filed temporary in the original work assembly");
        Check(uf.Obj.Deleted.Count==1 && uf.Obj.Deleted[0]==root.CoordinateSystems.Created[0].Tag,
            "Only the new measurement CSYS is deleted; source WCS, auxiliaries and matrix are untouched");
        uf.Obj.Deleted.Clear();

        session.CaptureObserver = view => { throw new IOException("capture fails"); };
        try { TransactionTestScope.Run(new ImageExporter(new[]{ImageExporter.SnapViewType.Top}), comp,session,output,"failed"); }
        catch(IOException) { }
        for(int i=0;i<auxiliaries.Length;++i) Check(auxiliaries[i].IsBlanked==(i==5), "Capture failure restores auxiliaries");
        Check(uf.Obj.Deleted.Count==0 && root.CoordinateSystems.Created.Count==1,
            "Standalone screenshot failure neither creates measurement objects nor deletes auxiliary objects");
    }

    private static void CoordinateQueries(string output)
    {
        Part root; Component comp;
        Session session=Setup(out root,out comp);
        var uf=UFSession.Instance;
        // Target axes rotated 90 degrees about Z, origin translated.
        uf.Assem.Transform = new double[,] { {0,-1,0,100},{1,0,0,200},{0,0,1,300},{0,0,0,1} };
        uf.Modl.Vertices[(Tag)3000] = Box(97,200,300,3,2,4);
        Export(comp,session,output,"ACS");
        Check(Row(output).Contains(",2.000,3.000,4.000,"), "Occurrence bbox aligned to target ACS, not root ACS");
        Near(uf.Csys.Origin[0],100,"Target ACS origin x"); Near(uf.Csys.Axes[1],1,"Target ACS X axis along assembly Y");
        uf.Csys.SourceOrigin = new double[]{1,2,3};
        uf.Csys.SourceAxes = new double[]{0,1,0,-1,0,0,0,0,1};
        Export(comp,session,output,"WCS");
        Check(Row(output).Contains(",3.000,2.000,4.000,"), "Target WCS composed with occurrence rotation");
        Near(uf.Csys.Origin[0],98,"WCS origin transformed x"); Near(uf.Csys.Origin[1],201,"WCS origin transformed y");
        Near(uf.Csys.Origin[2],303,"WCS origin transformed z");
        ((Part)comp.Prototype).PartUnits=Part.Units.Inches;
        uf.Modl.Vertices[(Tag)3000]=Box(100,200,300,76.2,50.8,101.6);
        Export(comp,session,output,"ACS");
        Check(Row(output).Contains(",2.000,3.000,4.000,\"Inches\""), "Assembly millimeters converted to target inches");
        Check(Row(output).Contains("DisplayedOccurrenceBodies"), "CSV scope declares displayed occurrence bodies");
        Near(ExportUtilities.UnitScale("Inches","Millimeters"),25.4,"Reverse unit conversion");
        // Root match uses its own bodies plus occurrence children.
        session=Setup(out root,out comp);
        root.ComponentAssembly.RootComponent.Children[1].IsBlanked = true;
        root.Bodies.Add((Body)root.Views.WorkView.Scene[1]);
        uf.Modl.Vertices[(Tag)3001]=Box(-1,0,0,1,1,1);
        Export(root.ComponentAssembly.RootComponent,session,output,"ACS");
        Check(Row(output).Contains(",3.000,3.000,4.000,"),"Root direct body and visible child combined");
    }

    private static void FailureAndCleanup(string output)
    {
        Part root; Component comp;
        Session session=Setup(out root,out comp);
        var uf=UFSession.Instance;
        File.Delete(Path.Combine(output,"BoundingBoxResult.csv"));
        uf.Modl.Fail=true;
        Fails(()=>Export(comp,session,output,"ACS"),"Exact query failure propagates");
        Check(!File.Exists(Path.Combine(output,"BoundingBoxResult.csv")),"No incomplete CSV row on failed query");
        Check(uf.Obj.Deleted.Count==1 && uf.Obj.Deleted[0]==root.CoordinateSystems.Created[0].Tag &&
            session.Parts.Display==root && !root.ComponentAssembly.RootComponent.Children[1].IsBlanked,
            "Query failure cleans only its own CSYS and restores visibility");
        using(var context=new TransactionTestScope(session,comp,output,"target"))
        {
            Fails(()=>context.Process(new BoundingBoxExporter("ACS")),"Shared context survives failed measurement");
            context.Process(new ImageExporter(new[]{ImageExporter.SnapViewType.Top}));
        }
        Check(session.LastImageBuilder.Destroyed,"Image succeeds after measurement fails without activation fallback");
        uf.Modl.Fail=false;
        var second=new Body {Tag=(Tag)3499,OwningComponent=comp};
        uf.Assem.Members[comp.Tag]=new NXObject[]{root.Views.WorkView.Scene[0],second};
        uf.Modl.Vertices[second.Tag]=Box(5,0,0,1,1,1);
        uf.Modl.AfterQuery=()=>AssemblyTraverser.RequestStop();
        bool canceled=false;
        try{Export(comp,session,output,"ACS");}catch(OperationCanceledException){canceled=true;}
        Check(canceled && !File.Exists(Path.Combine(output,"BoundingBoxResult.csv")),"Cancellation after first body cannot emit a partial box");
        Check(!root.ComponentAssembly.RootComponent.Children[1].IsBlanked,"Measurement cancellation restores visibility");
        uf.Modl.AfterQuery=null;AssemblyTraverser.StopRequested=false;
        var facet=new NXOpen.Facet.FacetedBody {Tag=(Tag)3500,OwningComponent=comp};
        uf.Assem.Members[comp.Tag]=new NXObject[]{facet};
        Fails(()=>Export(comp,session,output,"ACS"),"Lightweight-only data fails exact measurement explicitly");
        Check(!File.Exists(Path.Combine(output,"BoundingBoxResult.csv")),"Facet-only measurement never reports zero or partial success");
        facet.IsAssociatedBodyLoaded=true;facet.AssociatedBody=(Body)root.Views.WorkView.Scene[0];
        Export(comp,session,output,"ACS");
        Check(Row(output).Contains(",2.000,3.000,4.000,"),"Facet with available occurrence body can be measured");
        uf.Obj.Fail=true;
        Fails(()=>Export(comp,session,output,"ACS"),"Temporary object deletion failure propagates");
        Check(AssemblyTraverser.StopRequested,"Cleanup failure stops further loading");
        uf.Obj.Fail=false; AssemblyTraverser.StopRequested=false;
    }

    private static void DiagnosticEvidence(string output)
    {
        Part root; Component comp;
        Session session = Setup(out root, out comp);
        comp.DisplayName = "diagnostic target/003";
        File.Delete(Path.Combine(output, "BoundingBoxResult.csv"));
        string logPath = Path.Combine(output, "Traversal.log");
        UFSession.Instance.Modl.AfterQuery = () =>
        {
            // Evidence must already be on disk while NX is still inside a call;
            // a native non-return cannot rely on catch/finally to record it.
            string during = File.ReadAllText(logPath);
            Check(during.Contains("Body=1/1 tag=3000") && during.Contains("Component=diagnostic target/003(2000)") &&
                during.Contains("Part=readonly prototype State=PartiallyLoaded"),
                "Pending exact call has flushed body index and source-part identity");
            Check(during.Contains("[NX call begin]") && during.Contains("AskBoundingBoxExact occurrence=3000"),
                "Pending exact call is recorded before its completion");
            throw new InvalidOperationException("outer query failure", new IOException("inner query detail"));
        };
        using (var log = new RunLog(session, output))
        {
            RunLog.WriteBuildIdentity();
            try { Export(comp, session, output, "ACS"); }
            catch (InvalidOperationException ex) { RunLog.Error("diagnostic query", ex); }
        }
        string text = File.ReadAllText(logPath);
        Check(text.Contains(RunLog.DiagnosticRevision) && text.Contains("MVID=") && text.Contains("[Assembly] "),
            "Disk log identifies the actual loaded binary");
        Check(text.Contains("[Exception detail]") && text.Contains("inner query detail") &&
            !session.ListingWindow.Lines.Exists(x => x.Contains("inner query detail")),
            "Full inner exception is on disk without flooding the information window");
        Check(text.Contains("Release self-owned measurement CSYS tag=9003") && !text.Contains("temporary matrix") &&
            text.Contains("Completed=False"), "Failed query and only the owned-CSYS cleanup leave paired evidence");
        Check(!File.Exists(Path.Combine(output, "BoundingBoxResult.csv")), "Diagnostic changes cannot emit an incomplete dimension row");
        UFSession.Instance.Modl.AfterQuery = null;
    }

    private static void TemporaryLifetime(string output)
    {
        Part root; Component comp;
        var uf = UFSession.Instance;
        Session session;
        foreach (int matrixState in new[] { UFConstants.UF_OBJ_CONDEMNED, UFConstants.UF_OBJ_ALIVE,
            UFConstants.UF_OBJ_TEMPORARY, UFConstants.UF_OBJ_DELETED })
        {
            session = Setup(out root, out comp);
            uf.Obj.States[(Tag)9002] = matrixState;
            // Several queries share an NX-managed orientation; no query owns it.
            for (int run=0; run<3; ++run) Export(comp, session, output, "ACS");
            Check(uf.Obj.Deleted.Count == 3 && !uf.Obj.Deleted.Contains((Tag)9002) &&
                !uf.Obj.StatusQueries.Contains((Tag)9002) && uf.Obj.States[(Tag)9002] == matrixState,
                "Shared/NX-managed matrix status never authorizes a query to touch or delete it");
            Check(Row(output).Contains(",2.000,3.000,4.000,") && !AssemblyTraverser.StopRequested,
                "Including native Condemned matrix state, only the private CSYS controls cleanup success");
            Check(root.CoordinateSystems.Created.TrueForAll(c => uf.Obj.States[c.Tag] == UFConstants.UF_OBJ_DELETED),
                "Repeated measurements release each private coordinate system");
        }

        // The same stale-parameter code is acceptable only with independent
        // proof of deletion; a live object must still stop the run.
        foreach (bool deleted in new[] { true, false })
        {
            session = Setup(out root, out comp);
            File.Delete(Path.Combine(output, "BoundingBoxResult.csv"));
            uf.Obj.ErrorTag = (Tag)9003; uf.Obj.DeleteBeforeError = deleted;
            if (deleted) Export(comp, session, output, "ACS");
            else Fails(() => Export(comp, session, output, "ACS"), "1105004 with live private CSYS remains a cleanup failure");
            Check(File.Exists(Path.Combine(output, "BoundingBoxResult.csv")) == deleted && AssemblyTraverser.StopRequested != deleted,
                "Status, not error code alone, decides cleanup success");
        }

        session = Setup(out root, out comp);
        File.Delete(Path.Combine(output, "BoundingBoxResult.csv"));
        uf.Obj.Refuse = (Tag)9003;
        Fails(() => Export(comp, session, output, "ACS"), "A successful return without actual deletion is rejected");
        Check(AssemblyTraverser.StopRequested && !File.Exists(Path.Combine(output, "BoundingBoxResult.csv")),
            "Unreleased private CSYS cannot produce a success row");
        Check(!uf.Obj.Deleted.Contains((Tag)9002), "Failed CSYS cleanup does not authorize deleting its matrix");

        session = Setup(out root, out comp);
        root.CoordinateSystems.FailCreate = true;
        Fails(() => Export(comp, session, output, "ACS"), "CSYS creation failure propagates");
        Check(uf.Obj.Deleted.Count == 0 && !AssemblyTraverser.StopRequested &&
            uf.Obj.States[(Tag)9002] == UFConstants.UF_OBJ_CONDEMNED,
            "Creation failure returns no owned CSYS and never deletes an existing matrix");

        session = Setup(out root, out comp);
        root.CoordinateSystems.AfterCreate = AssemblyTraverser.RequestStop;
        bool canceled = false;
        try { Export(comp, session, output, "ACS"); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && uf.Obj.Deleted.Count==1 && uf.Obj.Deleted[0]==root.CoordinateSystems.Created[0].Tag,
            "Cancellation at creation return still releases the just-created private CSYS");

        session = Setup(out root, out comp);
        uf.Obj.StatusError = true;
        Fails(() => Export(comp, session, output, "ACS"), "Unverifiable object status remains a cleanup failure");
        Check(uf.Obj.Deleted.Count == 0 && AssemblyTraverser.StopRequested, "Unknown status never leads to a blind delete");
        Setup(out root, out comp); // Reset test-only faults for the other suites.
    }
}

namespace NXOpen
{
    public sealed class CoordinateSystemCollection
    {
        private readonly Part _part;
        private int _nextTag = 9003;
        public CoordinateSystemCollection(Part part) { _part = part; }
        public readonly List<CartesianCoordinateSystem> Created = new List<CartesianCoordinateSystem>();
        public bool LastTemporary, FailCreate;
        public Action AfterCreate;
        public CartesianCoordinateSystem CreateCoordinateSystem(Point3d origin, Matrix3x3 orientation, bool isTemporary)
        {
            if (FailCreate) throw new InvalidOperationException("CSYS creation failed");
            var uf = UFSession.Instance;
            uf.Csys.Origin = new[] { origin.X, origin.Y, origin.Z };
            uf.Csys.Axes = new[] { orientation.Xx, orientation.Xy, orientation.Xz,
                orientation.Yx, orientation.Yy, orientation.Yz, orientation.Zx, orientation.Zy, orientation.Zz };
            LastTemporary = isTemporary;
            var csys = new CartesianCoordinateSystem { Tag=(Tag)_nextTag++, OwningPart=_part };
            Created.Add(csys);
            uf.Obj.States[csys.Tag] = UFConstants.UF_OBJ_ALIVE; // Same CSYS state as the user's native log.
            if (AfterCreate != null) AfterCreate();
            return csys;
        }
    }
    public class CartesianCoordinateSystem : DisplayableObject { }
    public class Point : DisplayableObject { }
    public class Sketch : DisplayableObject { }
    public class DatumPlane : DisplayableObject { }
    public class Curve : DisplayableObject { }
    public sealed class WCS { public CartesianCoordinateSystem CoordinateSystem = new CartesianCoordinateSystem { Tag=(Tag)9000 }; }
}
namespace NXOpen.Facet
{
    public class FacetedBody : DisplayableObject { public bool IsAssociatedBodyLoaded; public Body AssociatedBody; }
}
namespace NXOpen.Layer
{
    public enum State { Visible, Hidden }
    public struct StateInfo { public int Layer; public State State; }
    public class LayerManager
    {
        public readonly HashSet<int> Hidden=new HashSet<int>();
        public void GetVisibilitiesInView(View view,out StateInfo[] states)
        {
            states=new StateInfo[256];
            for(int i=0;i<256;++i) states[i]=new StateInfo {Layer=i+1,State=Hidden.Contains(i+1)?State.Hidden:State.Visible};
        }
    }
}
namespace NXOpen.Utilities
{
    public static class NXObjectManager
    {
        public static readonly Dictionary<Tag,NXObject> Objects=new Dictionary<Tag,NXObject>();
        public static NXObject Get(Tag tag) { return Objects[tag]; }
    }
}
namespace NXOpen.UF
{
    public class UFCsys
    {
        public double[] Origin,Axes,SourceOrigin,SourceAxes;
        public void AskCsysInfo(Tag tag,out Tag matrix,double[] origin) { matrix=(Tag)9001;SourceOrigin.CopyTo(origin,0); }
        public void AskMatrixValues(Tag tag,double[] axes) { SourceAxes.CopyTo(axes,0); }
    }
    public class UFModl
    {
        public readonly Dictionary<Tag,double[][]> Vertices=new Dictionary<Tag,double[][]>();
        public readonly List<Tag> Queried=new List<Tag>();
        public bool Fail;
        public Action AfterQuery;
        public void AskBoundingBoxExact(Tag body,Tag csys,double[] corner,double[,] directions,double[] distances)
        {
            if(Fail) throw new InvalidOperationException("Simulated exact query failure");
            Queried.Add(body);
            var frame=UFSession.Instance.Csys;
            var min=new[]{double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity};
            var max=new[]{double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};
            foreach(double[] point in Vertices[body]) for(int axis=0;axis<3;++axis)
            {
                double d=0;for(int k=0;k<3;++k)d+=(point[k]-frame.Origin[k])*frame.Axes[axis*3+k];
                min[axis]=Math.Min(min[axis],d);max[axis]=Math.Max(max[axis],d);
            }
            if(AfterQuery!=null)AfterQuery();
            frame.Origin.CopyTo(corner,0);
            for(int axis=0;axis<3;++axis)
            {
                distances[axis]=max[axis]-min[axis];
                for(int k=0;k<3;++k){directions[axis,k]=frame.Axes[axis*3+k];corner[k]+=min[axis]*directions[axis,k];}
            }
        }
    }
    public class UFObj
    {
        public readonly List<Tag> Deleted=new List<Tag>();public bool Fail;
        public readonly Dictionary<Tag,int> States=new Dictionary<Tag,int>();
        public readonly List<Tag> StatusQueries=new List<Tag>();
        public bool DeleteBeforeError, StatusError;
        public Tag Refuse, ErrorTag;
        public int AskStatus(Tag tag)
        {
            StatusQueries.Add(tag);
            if(StatusError)throw new InvalidOperationException("Status query failed");
            int state;return States.TryGetValue(tag,out state)?state:UFConstants.UF_OBJ_DELETED;
        }
        public void DeleteObject(Tag tag)
        {
            Deleted.Add(tag);
            if(Fail)throw new InvalidOperationException("Simulated delete failure");
            if(AskStatus(tag)==UFConstants.UF_OBJ_DELETED)throw new NXException(1105004);
            if(tag==Refuse)return;
            if(tag!=ErrorTag||DeleteBeforeError)States[tag]=UFConstants.UF_OBJ_DELETED;
            if(tag==ErrorTag)throw new NXException(1105004);
        }
    }
    public static class UFConstants
    {
        public const int UF_OBJ_DELETED=0, UF_OBJ_TEMPORARY=1, UF_OBJ_CONDEMNED=2, UF_OBJ_ALIVE=3;
    }
}
