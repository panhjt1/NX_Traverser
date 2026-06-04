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
    
    public TraversalConfig()
    {
        filePath = string.Empty;
        namePatterns = new string[0];
        idPatterns = new string[0];
        flagImageOn = false;
        flagXYZOn = false;
        viewList = new ImageExporter.SnapViewType[0];
        coordinateSelection = "ACS";
    }
}