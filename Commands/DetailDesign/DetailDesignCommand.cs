using System;
using System.Diagnostics;
using System.Text;
using Autodesk.AutoCAD.Runtime;
using MCGCadPlugin.Services.DetailDesign.DebugSymbols;
using MCGCadPlugin.Views.DetailDesign.ViewModels;

namespace MCGCadPlugin.Commands.DetailDesign
{
    /// <summary>
    /// Các lệnh AutoCAD cho module Detail Design.
    /// Gọi qua PaletteManager hoặc trực tiếp từ command line.
    /// </summary>
    public class DetailDesignCommand
    {
        private const string LOG_PREFIX = "[DetailDesignCommand]";

        /// <summary>ViewModel reference — set bởi View khi khởi tạo</summary>
        public static DetailDesignViewModel ActiveViewModel { get; set; }

        /// <summary>
        /// Lệnh MCG_SelectPanel — chạy trên document context.
        /// Gọi từ View code-behind qua SendStringToExecute.
        /// </summary>
        [CommandMethod("MCG_SelectPanel", CommandFlags.Modal)]
        public void SelectPanel()
        {
            Debug.WriteLine($"{LOG_PREFIX} MCG_SelectPanel called.");

            if (ActiveViewModel == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: ActiveViewModel is null.");
                return;
            }

            ActiveViewModel.ExecuteSelectPanel();
        }

        /// <summary>
        /// Lệnh MCG_ShowDebugSymbols — chèn symbols debug lên plan view.
        /// </summary>
        [CommandMethod("MCG_ShowDebugSymbols", CommandFlags.Modal)]
        public void ShowDebugSymbols()
        {
            Debug.WriteLine($"{LOG_PREFIX} MCG_ShowDebugSymbols called.");

            if (ActiveViewModel == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: ActiveViewModel is null.");
                return;
            }

            ActiveViewModel.ExecuteToggleDebugSymbols();
        }

        /// <summary>
        /// Lệnh MCG_IProperties — pick 1 hoặc nhiều entities để xem properties.
        /// Output hiển thị command line + copy vào clipboard (paste ra Notepad).
        /// </summary>
        [CommandMethod("MCG_IProperties", CommandFlags.Modal)]
        public void IProperties()
        {
            Debug.WriteLine($"{LOG_PREFIX} MCG_IProperties called.");

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            // Prompt multi-selection
            var selOpts = new Autodesk.AutoCAD.EditorInput.PromptSelectionOptions();
            selOpts.MessageForAdding = "\nSelect entities to inspect: ";
            var selResult = ed.GetSelection(selOpts);

            if (selResult.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
            {
                Debug.WriteLine($"{LOG_PREFIX} IProperties cancelled.");
                return;
            }

            var ids = selResult.Value.GetObjectIds();
            var sb = new StringBuilder();
            sb.AppendLine($"═══ MCG iProperties — {ids.Length} entities ═══");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    for (int i = 0; i < ids.Length; i++)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"─── [{i + 1}/{ids.Length}] ───");
                        AppendEntityProperties(ids[i], tr, sb);
                    }
                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} IProperties ERROR: {ex.Message}");
                    sb.AppendLine($"Error: {ex.Message}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════");

            var output = sb.ToString();
            ed.WriteMessage("\n" + output);
            Debug.WriteLine($"{LOG_PREFIX} iProperties: {ids.Length} entities inspected");

            // Copy to clipboard (STA thread in AutoCAD command context)
            try
            {
                System.Windows.Clipboard.SetText(output);
                ed.WriteMessage("\n[Copied to clipboard — Ctrl+V vào Notepad để xem đầy đủ]");
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} Clipboard error: {ex.Message}");
                ed.WriteMessage($"\n[Clipboard copy failed: {ex.Message}]");
            }
        }

        /// <summary>Append properties của 1 entity vào StringBuilder</summary>
        private static void AppendEntityProperties(Autodesk.AutoCAD.DatabaseServices.ObjectId entId,
            Autodesk.AutoCAD.DatabaseServices.Transaction tr, StringBuilder sb)
        {
            var ent = tr.GetObject(entId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead)
                as Autodesk.AutoCAD.DatabaseServices.Entity;
            if (ent == null) { sb.AppendLine("  (not an entity)"); return; }

            var handle = ent.Handle.ToString();
            var layer = ent.Layer;
            var color = ent.ColorIndex;
            var entType = ent.GetType().Name;

            string guid = "—", panelGuid = "—", elemType = "—", status = "—",
                   geoHash = "—", thickness = "—", profileCode = "—", bracketType = "—", source = "";
            Models.DetailDesign.StructuralElementModel modelElem = null;

            string diag = null;  // diagnostic message nếu không match
            var rb = ent.GetXDataForApplication(Utilities.DetailDesign.DetailDesignConstants.XDATA_APP_NAME);
            if (rb != null)
            {
                var values = rb.AsArray();
                if (values.Length >= 10)
                {
                    guid = values[1].Value as string ?? "—";
                    panelGuid = values[2].Value as string ?? "—";
                    elemType = values[3].Value as string ?? "—";
                    status = values[4].Value as string ?? "—";
                    geoHash = values[5].Value as string ?? "—";
                    thickness = values[7].Value?.ToString() ?? "—";
                    profileCode = values[8].Value as string ?? "—";
                    bracketType = values[9].Value as string ?? "—";
                }
                source = "from XData";
                rb.Dispose();
                if (ActiveViewModel != null && guid != "—" && !string.IsNullOrEmpty(guid))
                    modelElem = ActiveViewModel.GetElementByGuid(guid);
            }
            else
            {
                var dbgRb = ent.GetXDataForApplication(DebugSymbolService.DEBUG_XDATA_APP);
                if (dbgRb != null)
                {
                    string dbgGuid = null, dbgKind = null;
                    foreach (var tv in dbgRb.AsArray())
                    {
                        if (tv.TypeCode == 1000 && tv.Value is string s)
                        {
                            if (s.Length >= 32 && s.Contains("-")) dbgGuid = s;
                            else if (dbgKind == null) dbgKind = s;
                        }
                    }
                    dbgRb.Dispose();

                    if (!string.IsNullOrEmpty(dbgGuid))
                    {
                        if (ActiveViewModel != null)
                        {
                            var elem = ActiveViewModel.GetElementByGuid(dbgGuid);
                            if (elem != null)
                            {
                                guid = elem.Guid;
                                panelGuid = elem.PanelGuid ?? "—";
                                elemType = elem.ElemType.ToString();
                                status = elem.Status.ToString();
                                geoHash = elem.GeometryHash ?? "—";
                                thickness = elem.Thickness?.ToString("F1") ?? "—";
                                bracketType = string.IsNullOrEmpty(elem.BracketSubType) ? "—" : elem.BracketSubType;
                                entType = $"{entType} → {elem.ElemType} [{dbgKind}]";
                                source = "from Debug block → model";
                                modelElem = elem;
                            }
                            else
                            {
                                guid = dbgGuid;
                                diag = $"Debug XData found (kind={dbgKind}, guid={dbgGuid}) but element NOT in ActiveViewModel — stale TT or VM reset";
                            }
                        }
                        else
                        {
                            guid = dbgGuid;
                            diag = $"Debug XData found (kind={dbgKind}, guid={dbgGuid}) but ActiveViewModel is null";
                        }
                    }
                    else
                    {
                        diag = $"Debug XData present but no GUID extracted (kind={dbgKind ?? "—"})";
                    }
                }
                else
                {
                    // Dump raw XData để xem có XData app nào khác không
                    var allXd = ent.XData;
                    if (allXd != null)
                    {
                        var tvs = allXd.AsArray();
                        var apps = new System.Collections.Generic.List<string>();
                        foreach (var tv in tvs)
                        {
                            if (tv.TypeCode == 1001 && tv.Value is string app)
                                apps.Add(app);
                        }
                        allXd.Dispose();
                        diag = apps.Count > 0
                            ? $"No MCG XData. Other XData apps present: {string.Join(", ", apps)}"
                            : "Entity has no XData at all";
                    }
                    else
                    {
                        diag = "Entity has no XData at all";
                    }
                }
            }

            string areaStr = "—";
            if (ent is Autodesk.AutoCAD.DatabaseServices.Polyline pline)
                areaStr = $"{pline.Area:F0} mm²";

            string typeDisplay = elemType;
            if (elemType == "Bracket" && bracketType != "—" && !string.IsNullOrEmpty(bracketType))
                typeDisplay = $"Bracket ({bracketType})";

            sb.AppendLine($"  Handle:    {handle}");
            sb.AppendLine($"  GUID:      {guid}");
            sb.AppendLine($"  Type:      {typeDisplay}");
            sb.AppendLine($"  Entity:    {entType}");
            sb.AppendLine($"  Layer:     {layer}");
            sb.AppendLine($"  Color:     {color}");
            sb.AppendLine($"  Status:    {status}");
            sb.AppendLine($"  Area:      {areaStr}");
            sb.AppendLine($"  Thickness: {thickness}");
            sb.AppendLine($"  Profile:   {profileCode}");
            sb.AppendLine($"  SubType:   {bracketType}");
            sb.AppendLine($"  Panel:     {panelGuid}");
            if (!string.IsNullOrEmpty(source))
                sb.AppendLine($"  Source:    {source}");

            // Vertices + OBB từ model (nếu tìm được)
            if (modelElem != null)
            {
                sb.AppendLine($"  Polyline:  {modelElem.AcadHandle ?? "—"}");
                // Lookup polyline entity trực tiếp qua handle → đọc Layer/Linetype/Color
                if (!string.IsNullOrEmpty(modelElem.AcadHandle))
                {
                    try
                    {
                        var pHandle = new Autodesk.AutoCAD.DatabaseServices.Handle(
                            long.Parse(modelElem.AcadHandle, System.Globalization.NumberStyles.HexNumber));
                        var pdb = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database;
                        var pid = pdb.GetObjectId(false, pHandle, 0);
                        if (pid.IsValid)
                        {
                            var poly = tr.GetObject(pid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead)
                                as Autodesk.AutoCAD.DatabaseServices.Entity;
                            if (poly != null)
                            {
                                sb.AppendLine($"  PolyLayer: {poly.Layer}");
                                sb.AppendLine($"  PolyLType: {poly.Linetype}");
                                sb.AppendLine($"  PolyColor: {poly.ColorIndex}");
                                sb.AppendLine($"  PolyClass: {poly.GetType().Name}");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        sb.AppendLine($"  [Polyline lookup failed: {ex.Message}]");
                    }
                }
                sb.AppendLine($"  OBB:       {modelElem.ObbLength:F1} x {modelElem.ObbWidth:F1} @ {(modelElem.ObbAngle * 180.0 / Math.PI):F1}°");
                sb.AppendLine($"  Centroid:  ({modelElem.CentroidX:F1}, {modelElem.CentroidY:F1})");
                if (modelElem.VerticesWCS != null)
                {
                    sb.AppendLine($"  Vertices:  {modelElem.VerticesWCS.Length}");
                    for (int v = 0; v < modelElem.VerticesWCS.Length; v++)
                    {
                        var pt = modelElem.VerticesWCS[v];
                        sb.AppendLine($"    [{v}] ({pt.X:F1}, {pt.Y:F1})");
                    }
                }
            }
            else if (string.IsNullOrEmpty(source))
            {
                sb.AppendLine($"  [Diag]     {diag ?? "No data"}");
            }
        }
    }
}
