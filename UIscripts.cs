using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NXOpen;
using NXOpen.BlockStyler;

// Hand-maintained controller. Generate Block UI Styler templates elsewhere.
public sealed class NX_Traverser : IDisposable
{
    private readonly UI _ui;
    private readonly BlockDialog _dialog;
    private TraversalConfig _acceptedConfig;

    public NX_Traverser()
    {
        _ui = UI.GetUI();
        string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string localDlx = Path.Combine(directory ?? string.Empty, "NX_Traverser.dlx");
        _dialog = _ui.CreateDialog(File.Exists(localDlx) ? localDlx : "NX_Traverser.dlx");
        _dialog.AddApplyHandler(apply_cb);
        _dialog.AddOkHandler(ok_cb);
        _dialog.AddInitializeHandler(initialize_cb);
        _dialog.AddDialogShownHandler(dialogShown_cb);
        _dialog.AddUpdateHandler(update_cb);
    }

    public static void Main()
    {
        try
        {
            TraversalConfig config;
            using (var controller = new NX_Traverser())
            {
                BlockDialog.DialogResponse response = controller._dialog.Launch();
                config = response == BlockDialog.DialogResponse.Ok ? controller._acceptedConfig : null;
            }
            // Dispose the Block Styler transaction before touching the assembly.
            // Only OK accepts a configuration; Cancel never runs pending work.
            if (config != null) AssemblyTraverser.Run(config);
        }
        catch (Exception ex)
        {
            UI.GetUI().NXMessageBox.Show("NX Traverser", NXMessageBox.DialogType.Error, ex.ToString());
        }
    }

    public static int GetUnloadOption(string arg)
    {
        return (int)(TraversalStopWindow.HasLiveWindow
            ? Session.LibraryUnloadOption.AtTermination : Session.LibraryUnloadOption.Immediately);
    }

    public void Dispose() { _dialog.Dispose(); }

    public void initialize_cb()
    {
        foreach (string id in new[] { "filePath", "namePatterns", "IDPattern", "flagImageOn",
                                     "viewList", "flagXYZOn", "coordinateSelection" })
            if (_dialog.TopBlock.FindBlock(id) == null)
                throw new InvalidOperationException("DLX 缺少控件: " + id);
    }

    public void dialogShown_cb() { UpdateEnabledBlocks(); }

    public int update_cb(UIBlock block)
    {
        try { UpdateEnabledBlocks(); return 0; }
        catch (Exception ex) { ShowInputError(ex); return 1; }
    }

    private void UpdateEnabledBlocks()
    {
        SetEnabled("viewList", ReadToggle("flagImageOn"));
        SetEnabled("coordinateSelection", ReadToggle("flagXYZOn"));
    }

    private void SetEnabled(string id, bool enabled)
    {
        using (PropertyList properties = _dialog.GetBlockProperties(id))
            properties.SetLogical("Enable", enabled);
    }

    private bool ReadToggle(string id)
    {
        using (PropertyList properties = _dialog.GetBlockProperties(id))
            return properties.GetLogical("Value");
    }

    // NX requires Apply even for an OK/Cancel dialog. It only validates.
    public int apply_cb()
    {
        try { ReadConfig(); return 0; }
        catch (Exception ex) { ShowInputError(ex); return 1; }
    }

    public int ok_cb()
    {
        _acceptedConfig = null;
        try { _acceptedConfig = ReadConfig(); return 0; }
        catch (Exception ex) { ShowInputError(ex); return 1; }
    }

    private TraversalConfig ReadConfig()
    {
        var config = new TraversalConfig();
        config.filePath = ((FolderSelection)_dialog.TopBlock.FindBlock("filePath")).Path;
        using (PropertyList p = _dialog.GetBlockProperties("namePatterns"))
            config.namePatterns = ExportUtilities.NormalizePatterns(p.GetStrings("Value"));
        using (PropertyList p = _dialog.GetBlockProperties("IDPattern"))
            config.idPatterns = ExportUtilities.NormalizePatterns(p.GetStrings("Value"));
        config.flagImageOn = ReadToggle("flagImageOn");
        config.flagXYZOn = ReadToggle("flagXYZOn");
        if (config.flagImageOn)
        {
            using (PropertyList p = _dialog.GetBlockProperties("viewList"))
            {
                string[] items = p.GetStrings("ListItems") ?? new string[0];
                int[] indices = p.GetIntegerVector("SelectedItems") ?? new int[0];
                var views = new List<ImageExporter.SnapViewType>();
                foreach (int index in indices)
                {
                    ImageExporter.SnapViewType view;
                    if (index < 0 || index >= items.Length ||
                        !Enum.TryParse(items[index], out view) || !Enum.IsDefined(typeof(ImageExporter.SnapViewType), view))
                        throw new InvalidOperationException("DLX 视角列表与程序不匹配。");
                    if (!views.Contains(view)) views.Add(view);
                }
                config.viewList = views.ToArray();
            }
        }
        if (config.flagXYZOn)
        {
            using (PropertyList p = _dialog.GetBlockProperties("coordinateSelection"))
                config.coordinateSelection = ExportUtilities.NormalizeCoordinateMode(p.GetEnumAsString("Value"));
        }
        config.Validate();
        return config;
    }

    private void ShowInputError(Exception ex)
    {
        _ui.NXMessageBox.Show("NX Traverser", NXMessageBox.DialogType.Error, ex.Message);
    }
}
