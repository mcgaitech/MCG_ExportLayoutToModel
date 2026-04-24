using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using System.Collections.Generic;
using System.Linq;
using MCGCadPlugin.Services.DetailDesign.Classification;
using MCGCadPlugin.Services.DetailDesign.Collection;
using MCGCadPlugin.Services.DetailDesign.Data;
using MCGCadPlugin.Services.DetailDesign.Geometry;
using MCGCadPlugin.Services.DetailDesign.Parameters;
using MCGCadPlugin.Services.DetailDesign.XData;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign
{
    /// <summary>
    /// Entry point cho panel scan workflow.
    /// Step 4: SelectPanel — chọn block, parse tên, xác định side.
    /// </summary>
    public class PanelScanService : IPanelScanService
    {
        private const string LOG_PREFIX = "[PanelScanService]";

        /// <summary>Bracket models từ lần scan gần nhất — dùng cho tree building</summary>
        public List<BracketModel> LastBracketModels { get; private set; } = new List<BracketModel>();
        public List<StiffenerModel> LastStiffenerModels { get; private set; } = new List<StiffenerModel>();

        /// <summary>
        /// Prompt user chọn Assy BlockReference → parse tên + side.
        /// </summary>
        /// <returns>PanelContext nếu thành công, null nếu user cancel hoặc chọn sai entity</returns>
        public PanelContext SelectPanel()
        {
            Debug.WriteLine($"{LOG_PREFIX} SelectPanel started...");

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: No active document.");
                return null;
            }

            var db = doc.Database;
            var ed = doc.Editor;

            // Kiểm tra đơn vị bản vẽ
            if (!DrawingUnitsValidator.Validate(db))
            {
                Debug.WriteLine($"{LOG_PREFIX} ABORTED: Drawing units not Millimeters.");
                return null;
            }

            // Prompt user chọn BlockReference — filter INSERT only
            var opts = new PromptEntityOptions("\nSelect Assy block: ");
            opts.SetRejectMessage("\nOnly BlockReference allowed. Try again.");
            opts.AddAllowedClass(typeof(BlockReference), exactMatch: false);

            var result = ed.GetEntity(opts);
            if (result.Status != PromptStatus.OK)
            {
                Debug.WriteLine($"{LOG_PREFIX} User cancelled selection.");
                return null;
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var blockRef = tr.GetObject(result.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (blockRef == null)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} ERROR: Selected entity is not a BlockReference.");
                        ed.WriteMessage("\nSelected entity is not a block.");
                        return null;
                    }

                    var blockName = blockRef.Name;
                    Debug.WriteLine($"{LOG_PREFIX} Selected block: {blockName}");

                    // Kiểm tra có phải Assy block không
                    var rootCategory = SubBlockClassifier.Classify(blockName);
                    if (rootCategory != SubBlockClassifier.SubBlockCategory.ASSY_ROOT)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} WARNING: Block '{blockName}' is not an Assy block (category: {rootCategory}).");
                        ed.WriteMessage($"\nBlock '{blockName}' is not an Assy block. Please select a block ending with '_Assy'.");
                        tr.Commit();
                        return null;
                    }

                    // Phân loại sub-blocks
                    var subBlocks = SubBlockClassifier.ClassifySubBlocks(result.ObjectId, tr);

                    // Đọc revision từ CAS_HEAD block's ARAS_DOCREVISION attribute
                    var revision = SubBlockClassifier.ReadRevisionFromAssy(result.ObjectId, tr);

                    // Parse tên panel + side
                    var parsed = PanelNameParser.Parse(blockName);

                    // Tạo PanelContext
                    var panel = new PanelContext
                    {
                        Guid = Guid.NewGuid().ToString(),
                        Name = parsed.Name,
                        Side = parsed.Side,
                        SideAutoDetected = parsed.AutoDetected,
                        Mode = InputMode.Block,
                        RootBlockHandle = blockRef.Handle.ToString(),
                        Revision = revision
                    };

                    Debug.WriteLine($"{LOG_PREFIX} Panel: {panel.Name} | Side: {panel.Side} | Handle: {panel.RootBlockHandle}");
                    ed.WriteMessage($"\nPanel: {panel.Name} | Side: {panel.Side}" +
                                   (parsed.AutoDetected ? " (auto)" : " (default)"));

                    tr.Commit();
                    return panel;
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} ERROR: {ex.Message}");
                    ed.WriteMessage($"\nError: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Scan panel đã chọn — thu thập + phân loại entities.
        /// </summary>
        /// <param name="panel">PanelContext từ SelectPanel()</param>
        /// <returns>Danh sách StructuralElementModel đã phân loại</returns>
        public List<StructuralElementModel> ScanPanel(PanelContext panel)
        {
            Debug.WriteLine($"{LOG_PREFIX} ScanPanel started — {panel.Name}...");

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR: No active document.");
                return null;
            }

            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Tìm BlockReference từ handle
                    var handle = new Handle(long.Parse(panel.RootBlockHandle, System.Globalization.NumberStyles.HexNumber));
                    var blockRefId = db.GetObjectId(false, handle, 0);

                    // Thu thập entities
                    IEntityCollector collector;
                    if (panel.Mode == InputMode.Block)
                        collector = new BlockEntityCollector();
                    else
                        collector = new DirectEntityCollector();

                    var rawSet = collector.Collect(blockRefId, tr);

                    ed.WriteMessage($"\nCollected: {rawSet.TotalCount} entities");

                    // Phân loại — mỗi entity dùng transform TÍCH LŨY riêng (từ RawEntitySet.EntityRef)
                    Debug.WriteLine($"{LOG_PREFIX} Starting classification...");
                    var classifier = new PrimaryClassifier();
                    var allElements = new List<StructuralElementModel>();

                    // TopPlate
                    var topPlateElems = classifier.ClassifyBatch(
                        rawSet.TopPlateEntities, tr, panel.Guid, "TOP_PLATE");
                    allElements.AddRange(topPlateElems);

                    // Structure
                    var structureElems = classifier.ClassifyBatch(
                        rawSet.StructureEntities, tr, panel.Guid, "STRUCTURE");
                    allElements.AddRange(structureElems);

                    // Corner
                    var cornerElems = classifier.ClassifyBatch(
                        rawSet.CornerEntities, tr, panel.Guid, "CORNER");
                    allElements.AddRange(cornerElems);

                    // Box — entities từ sub-block BX/BOX/CB/CBOX → sourceContext="BOX"
                    var boxElems = classifier.ClassifyBatch(
                        rawSet.BoxEntities, tr, panel.Guid, "BOX");
                    allElements.AddRange(boxElems);

                    // Log classification summary
                    var nonHoles = allElements.Where(e => !e.IsHole);
                    int holeCount = allElements.Count(e => e.IsHole);
                    var summary = nonHoles.GroupBy(e => e.ElemType)
                                          .OrderBy(g => g.Key)
                                          .Select(g => $"{g.Key}: {g.Count()}");
                    var summaryText = string.Join(" | ", summary);
                    Debug.WriteLine($"{LOG_PREFIX} Classification COMPLETE — {nonHoles.Count()} elements + {holeCount} holes: {summaryText}");
                    ed.WriteMessage($"\nClassified: {summaryText} (holes: {holeCount})");

                    // Topology analysis — AM0 → WebPlate/Bracket/ClosingBox
                    Debug.WriteLine($"{LOG_PREFIX} Starting topology analysis...");
                    var topologyEngine = new TopologyEngine();
                    var topoResult = topologyEngine.Analyze(allElements);
                    allElements = topoResult.Elements;

                    var topoNonHoles = allElements.Where(e => !e.IsHole);
                    var topoSummary = topoNonHoles.GroupBy(e => e.ElemType)
                                                   .OrderBy(g => g.Key)
                                                   .Select(g => $"{g.Key}: {g.Count()}");
                    ed.WriteMessage($"\nTopology: {string.Join(" | ", topoSummary)}");

                    // Throw vector + OB/IB classification
                    Debug.WriteLine($"{LOG_PREFIX} Computing throw vectors + OB/IB...");
                    var throwEngine = new ThrowVectorEngine();
                    var stiffenerModels = throwEngine.ComputeThrowVectors(allElements, panel);

                    var bracketAnalyzer = new BracketAnalyzer();
                    var bracketModels = bracketAnalyzer.ClassifyBrackets(allElements, stiffenerModels, panel);
                    LastBracketModels = bracketModels;
                    LastStiffenerModels = stiffenerModels;

                    // Sync BracketSubType vào StructuralElementModel TRƯỚC BaseEdgeEngine
                    var bracketSubTypeMap = new System.Collections.Generic.Dictionary<string, string>();
                    foreach (var bm in bracketModels)
                        if (!string.IsNullOrEmpty(bm.ElemGuid))
                            bracketSubTypeMap[bm.ElemGuid] = bm.SubType ?? "";
                    foreach (var elem in allElements)
                        if (bracketSubTypeMap.TryGetValue(elem.Guid, out var st))
                            elem.BracketSubType = st;

                    Debug.WriteLine($"{LOG_PREFIX} Throw vectors: {stiffenerModels.Count} stiffeners | Brackets: {bracketModels.Count}");

                    // Tính thickness + apply defaults
                    Debug.WriteLine($"{LOG_PREFIX} Calculating thickness + applying defaults...");
                    // Tính web_height thực = building_height - top_plate_thk - flange_thk
                    double tpThk = panel.DefaultTopPlateThk ?? 6;
                    double flThk = panel.DefaultFlangeThk ?? 20;
                    double buildingH = panel.WebHeight ?? 0;
                    double webPlateHeight = buildingH > 0 ? (buildingH - tpThk - flThk) : 0;

                    foreach (var elem in allElements)
                    {
                        if (elem.IsHole) continue;

                        switch (elem.ElemType)
                        {
                            case StructuralType.WebPlate:
                                // Thickness: midpoint method
                                elem.Thickness = ThicknessCalculator.Calculate(elem.VerticesWCS);
                                // Area = length × web_height (nếu có web_height)
                                if (webPlateHeight > 0 && elem.ObbLength > 0)
                                    elem.AreaPoly = elem.ObbLength * webPlateHeight;
                                break;

                            case StructuralType.Bracket:
                            case StructuralType.ClosingBoxWeb:
                                // Auto-compute: midpoint method, area từ polyline
                                elem.Thickness = ThicknessCalculator.Calculate(elem.VerticesWCS);
                                break;

                            case StructuralType.Flange:
                                // Thickness = default
                                elem.Thickness = panel.DefaultFlangeThk ?? 20;
                                break;

                            case StructuralType.TopPlateRegion:
                                // Thickness = default
                                elem.Thickness = panel.DefaultTopPlateThk ?? 6;
                                break;
                        }
                    }

                    // Phase A — Detect bended plates (log only, không split)
                    MCGCadPlugin.Services.DetailDesign.Geometry.BendedPlateAnalyzer.DetectAndLog(allElements);

                    // Compute base edge + throw vector (Phase 1 — debug symbols prep)
                    MCGCadPlugin.Services.DetailDesign.Parameters.BaseEdgeEngine.ComputeAll(allElements, panel);

                    // Tính geometry hash + ghi XData lên mỗi entity
                    Debug.WriteLine($"{LOG_PREFIX} Writing XData + geometry hash...");
                    var xdataManager = new XDataManager();
                    int xdataCount = 0;

                    foreach (var elem in allElements)
                    {
                        // Tính hash
                        elem.GeometryHash = GeometryHasher.Compute(elem.VerticesWCS);

                        // Ghi XData lên entity
                        var acadHandle = new Handle(long.Parse(elem.AcadHandle, System.Globalization.NumberStyles.HexNumber));
                        var entId = db.GetObjectId(false, acadHandle, 0);
                        if (entId.IsValid)
                        {
                            var payload = new XDataPayload
                            {
                                ElemGuid = elem.Guid,
                                PanelGuid = elem.PanelGuid,
                                ElemType = elem.ElemType.ToString(),
                                Status = elem.Status.ToString(),
                                GeometryHash = elem.GeometryHash,
                                DbVersion = DateTime.Now.ToString("o"),
                                Thickness = elem.Thickness ?? 0,
                                ProfileCode = "",
                                BracketType = elem.BracketSubType ?? ""
                            };
                            xdataManager.Write(entId, tr, payload);
                            xdataCount++;
                        }
                    }
                    Debug.WriteLine($"{LOG_PREFIX} XData written: {xdataCount} entities.");

                    // Save vào SQLite
                    Debug.WriteLine($"{LOG_PREFIX} Saving to SQLite...");
                    var repo = new DetailDesignRepository();
                    string finalPanelGuid = repo.UpsertPanel(panel);
                    // Re-sync elem.PanelGuid — nếu duplicate detect đổi panel.Guid → elements phải match
                    foreach (var elem in allElements)
                    {
                        elem.PanelGuid = finalPanelGuid;
                        repo.UpsertElement(elem);
                    }
                    Debug.WriteLine($"{LOG_PREFIX} SQLite saved: 1 panel + {allElements.Count} elements.");

                    ed.WriteMessage($"\nSaved: {xdataCount} XData + {allElements.Count} DB records");

                    tr.Commit();
                    return allElements;
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} ScanPanel ERROR: {ex.Message}");
                    Debug.WriteLine($"{LOG_PREFIX} Stack: {ex.StackTrace}");
                    ed.WriteMessage($"\nScan error: {ex.Message}");
                    return null;
                }
            }
        }
    }
}
