using System;

public class TraversalConfig
{
    public string filePath { get; set; }
    
    public string[] namePatterns { get; set; }
    
    public string[] idPatterns { get; set; }
    
    public bool flagImageOn { get; set; }
    
    public bool flagXYZOn { get; set; }
    
    public ImageExporter.SnapViewType[] viewList { get; set; }
    
    public string coordinateSelection { get; set; }

    // Default screenshot path avoids making read-only prototypes active.
    // False explicitly selects the legacy prototype-display path (may prompt).
    public bool useIsolatedImageView { get; set; }

    // Root is level 0. Retain the original limit, now configurable.
    public int maxDepth { get; set; }
    
    public TraversalConfig()
    {
        filePath = string.Empty;
        namePatterns = new string[0];
        idPatterns = new string[0];
        flagImageOn = false;
        flagXYZOn = false;
        viewList = new ImageExporter.SnapViewType[0];
        coordinateSelection = "ACS";
        useIsolatedImageView = true;
        maxDepth = 5;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("请选择输出文件夹。");
        filePath = System.IO.Path.GetFullPath(filePath.Trim());
        if (!flagImageOn && !flagXYZOn)
            throw new ArgumentException("请至少启用截图或包围盒测量。");
        if (flagImageOn && (viewList == null || viewList.Length == 0))
            throw new ArgumentException("启用截图时必须选择至少一个视角。");
        if (viewList != null)
            foreach (ImageExporter.SnapViewType view in viewList)
                if (!Enum.IsDefined(typeof(ImageExporter.SnapViewType), view))
                    throw new ArgumentException("无效的截图视角。");
        if (maxDepth < 0 || maxDepth > 100)
            throw new ArgumentOutOfRangeException("maxDepth", "最大层级必须在 0 到 100 之间。");
        namePatterns = ExportUtilities.NormalizePatterns(namePatterns);
        idPatterns = ExportUtilities.NormalizePatterns(idPatterns);
        coordinateSelection = ExportUtilities.NormalizeCoordinateMode(coordinateSelection);
    }
}
