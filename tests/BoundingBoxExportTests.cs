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
        var exporter = new BoundingBoxExporter(mode);
        try { exporter.Process(comp, session, output, "target"); }
        finally { exporter.Finish(); }
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
        using (var context = new NxAssemblyExportContext(session, comp))
        {
            try { exporter.Process(context, output, "target"); } finally { exporter.Finish(); }
            Check(Row(output).Contains(",5.000,3.000,14.000,"), "Hidden/refset/layer excluded; occluded rear and child bodies included");
            Check(uf.Modl.Queried.Count==3 && !uf.Modl.Queried.Contains(outOfRefset.Tag), "Only occurrence member bodies queried");
            session.CaptureObserver = view => {
                foreach (DisplayableObject aux in auxiliaries) Check(aux.IsBlanked, "Auxiliary object hidden at capture");
                Check(!root.Views.WorkView.Scene[0].IsBlanked, "Face normalized to body and retained");
            };
            new ImageExporter(new[] { ImageExporter.SnapViewType.Top }).Process(context, output, "clean");
            Check(session.Calls.FindAll(c => c=="partial readonly prototype").Count==1,
                "Combined export prepares target only once");
        }
        for(int i=0;i<auxiliaries.Length;++i) Check(auxiliaries[i].IsBlanked==(i==5), "Exact original auxiliary blank states restored");
        Check(hiddenBody.IsBlanked && hiddenChild.IsBlanked, "Hidden descendants stay hidden");
        Check(session.Calls.TrueForAll(c=>c.StartsWith("partial ", StringComparison.Ordinal)), "No activation or full loading in either handler");
        Check(session.Parts.Work==root && session.Parts.Display==root, "Original work and display preserved");
        Check(uf.Obj.Deleted.Count==2, "Temporary matrix and CSYS both deleted");

        session.CaptureObserver = view => { throw new IOException("capture fails"); };
        try { new ImageExporter(new[]{ImageExporter.SnapViewType.Top}).Process(comp,session,output,"failed"); }
        catch(IOException) { }
        for(int i=0;i<auxiliaries.Length;++i) Check(auxiliaries[i].IsBlanked==(i==5), "Capture failure restores auxiliaries");
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
        Check(uf.Obj.Deleted.Count==2 && session.Parts.Display==root && !root.ComponentAssembly.RootComponent.Children[1].IsBlanked,
            "Query failure cleans temporary objects and restores visibility");
        using(var context=new NxAssemblyExportContext(session,comp))
        {
            Fails(()=>new BoundingBoxExporter("ACS").Process(context,output,"fail"),"Shared context survives failed measurement");
            new ImageExporter(new[]{ImageExporter.SnapViewType.Top}).Process(context,output,"still image");
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
        uf.Modl.AfterQuery=null;AssemblyTraverser.Stopped=false;
        var facet=new NXOpen.Facet.FacetedBody {Tag=(Tag)3500,OwningComponent=comp};
        uf.Assem.Members[comp.Tag]=new NXObject[]{facet};
        Fails(()=>Export(comp,session,output,"ACS"),"Lightweight-only data fails exact measurement explicitly");
        Check(!File.Exists(Path.Combine(output,"BoundingBoxResult.csv")),"Facet-only measurement never reports zero or partial success");
        facet.IsAssociatedBodyLoaded=true;facet.AssociatedBody=(Body)root.Views.WorkView.Scene[0];
        Export(comp,session,output,"ACS");
        Check(Row(output).Contains(",2.000,3.000,4.000,"),"Facet with available occurrence body can be measured");
        uf.Obj.Fail=true;
        Fails(()=>Export(comp,session,output,"ACS"),"Temporary object deletion failure propagates");
        Check(AssemblyTraverser.Stopped,"Cleanup failure stops further loading");
        uf.Obj.Fail=false; AssemblyTraverser.Stopped=false;
    }
}

namespace NXOpen
{
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
        public void CreateMatrix(double[] axes,out Tag matrix) { Axes=(double[])axes.Clone();matrix=(Tag)9002; }
        public void CreateTempCsys(double[] origin,Tag matrix,out Tag csys) { Origin=(double[])origin.Clone();csys=(Tag)9003; }
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
        public void DeleteObject(Tag tag){Deleted.Add(tag);if(Fail)throw new InvalidOperationException("Simulated delete failure");}
    }
}
internal static class AssemblyTraverserUtils
{
    public static string GetDescriptiveName(Component comp){return comp.DisplayName;}
}
