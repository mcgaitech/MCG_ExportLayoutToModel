using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.DebugSymbols
{
    /// <summary>
    /// Chèn debug symbols (ThrowThickness, BS_symbol, CS_symbol, text labels)
    /// lên plan view để user verify trực quan kết quả scan + classification.
    ///
    /// Bước 1 hiện tại chỉ implement: ThrowThickness cho Web/Bracket/CB-Web.
    /// </summary>
    public static class DebugSymbolService
    {
        private const string LOG_PREFIX = "[DebugSymbolService]";

        // XData app name riêng cho debug symbols — dùng cleanup
        public const string DEBUG_XDATA_APP = "MCG_DEBUG_SYM";

        // Block names trong Symbol.dwg
        private const string BLOCK_THROW_THICKNESS = "ThrowThickness";

        // Layer targets
        private const string LAYER_DEBUG_BLOCK = "Mechanical-AM_2";

        // Scale hệ số — block size trong Symbol.dwg đã được vẽ đúng size yêu cầu.
        private const double SYMBOL_SCALE = 1.0;

        /// <summary>
        /// Erase tất cả debug symbols do plugin tạo (XData app = MCG_DEBUG_SYM).
        /// Trả về số entity đã erase.
        /// </summary>
        public static int EraseAll()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;

            var db = doc.Database;
            int erased = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    EnsureDebugAppRegistered(db, tr);
                    erased = CleanupOldSymbols(db, tr);
                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} EraseAll ERROR: {ex.Message}");
                    tr.Abort();
                }
            }
            // Force viewport redraw ngay
            if (erased > 0) doc.Editor.Regen();
            Debug.WriteLine($"{LOG_PREFIX} EraseAll — {erased} symbols erased.");
            return erased;
        }

        /// <summary>
        /// Cleanup tất cả debug symbols cũ + insert mới theo data hiện có.
        /// </summary>
        public static void Refresh(List<StructuralElementModel> elements, PanelContext panel)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) { Debug.WriteLine($"{LOG_PREFIX} No active document."); return; }
            if (elements == null || elements.Count == 0)
            {
                doc.Editor.WriteMessage("\nChưa có dữ liệu scan. Hãy SELECT PANEL trước.");
                return;
            }

            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    EnsureLayer(db, tr, LAYER_DEBUG_BLOCK);
                    EnsureDebugAppRegistered(db, tr);

                    int erased = CleanupOldSymbols(db, tr);
                    Debug.WriteLine($"{LOG_PREFIX} Cleaned up {erased} old debug symbols.");

                    if (!EnsureBlockLoaded(db, tr, BLOCK_THROW_THICKNESS, DetailDesignConstants.SYMBOL_DWG))
                    {
                        doc.Editor.WriteMessage($"\nKhông load được block {BLOCK_THROW_THICKNESS} từ {DetailDesignConstants.SYMBOL_DWG}");
                        tr.Abort();
                        return;
                    }

                    int insertedTT = InsertThrowThicknessSymbols(db, tr, elements);

                    tr.Commit();
                    // Force viewport redraw để user thấy update ngay (không cần click ra drawing)
                    doc.Editor.Regen();
                    doc.Editor.WriteMessage($"\nDebug: xóa {erased}, {insertedTT} ThrowThickness.");
                    Debug.WriteLine($"{LOG_PREFIX} Done — erased {erased}, TT={insertedTT}");
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} ERROR: {ex.Message}\n{ex.StackTrace}");
                    doc.Editor.WriteMessage($"\nLỗi: {ex.Message}");
                    tr.Abort();
                }
            }
        }

        // ─────────── Cleanup ───────────

        /// <summary>Erase mọi entity trong Model Space có XData app = MCG_DEBUG_SYM.</summary>
        private static int CleanupOldSymbols(Database db, Transaction tr)
        {
            int count = 0;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                var rb = ent.GetXDataForApplication(DEBUG_XDATA_APP);
                if (rb != null)
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                    count++;
                }
            }
            return count;
        }

        // ─────────── Block library loading ───────────

        /// <summary>Ensure block definition với name đã load vào current DB từ Symbol.dwg.</summary>
        private static bool EnsureBlockLoaded(Database db, Transaction tr, string blockName, string sourceDwgPath)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(blockName))
            {
                Debug.WriteLine($"{LOG_PREFIX} Block '{blockName}' already in DB.");
                return true;
            }

            if (!File.Exists(sourceDwgPath))
            {
                Debug.WriteLine($"{LOG_PREFIX} Symbol.dwg not found: {sourceDwgPath}");
                return false;
            }

            using (var sourceDb = new Database(false, true))
            {
                try
                {
                    sourceDb.ReadDwgFile(sourceDwgPath, FileOpenMode.OpenForReadAndAllShare, true, null);
                    var idMap = new IdMapping();
                    using (var sourceTr = sourceDb.TransactionManager.StartTransaction())
                    {
                        var sourceBt = (BlockTable)sourceTr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                        if (!sourceBt.Has(blockName))
                        {
                            Debug.WriteLine($"{LOG_PREFIX} Block '{blockName}' not in source DWG.");
                            sourceTr.Abort();
                            return false;
                        }

                        var sourceBtrId = sourceBt[blockName];
                        var ids = new ObjectIdCollection(new[] { sourceBtrId });
                        sourceDb.WblockCloneObjects(ids, db.BlockTableId, idMap,
                            DuplicateRecordCloning.Ignore, false);
                        sourceTr.Commit();
                    }
                    Debug.WriteLine($"{LOG_PREFIX} Block '{blockName}' imported from Symbol.dwg.");
                    return true;
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} Load block failed: {ex.Message}");
                    return false;
                }
            }
        }

        // ─────────── Layer ───────────

        private static void EnsureLayer(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName)) return;

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = layerName };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void EnsureDebugAppRegistered(Database db, Transaction tr)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(DEBUG_XDATA_APP)) return;

            rat.UpgradeOpen();
            var newApp = new RegAppTableRecord { Name = DEBUG_XDATA_APP };
            rat.Add(newApp);
            tr.AddNewlyCreatedDBObject(newApp, true);
        }

        // ─────────── Insertion ───────────

        /// <summary>
        /// Insert ThrowThickness block cho mỗi WebPlate/Bracket/ClosingBoxWeb.
        /// Position = midpoint base segment, Rotation = throw direction angle.
        /// </summary>
        private static int InsertThrowThicknessSymbols(Database db, Transaction tr,
            List<StructuralElementModel> elements)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BLOCK_THROW_THICKNESS)) return 0;
            var btrId = bt[BLOCK_THROW_THICKNESS];

            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            int count = 0;
            foreach (var elem in elements)
            {
                if (elem.IsHole) continue;
                if (elem.BaseStart == null || elem.BaseEnd == null) continue;

                bool applyThrow = elem.ElemType == StructuralType.WebPlate
                               || elem.ElemType == StructuralType.Bracket
                               || elem.ElemType == StructuralType.ClosingBoxWeb
                               || elem.ElemType == StructuralType.Stiffener
                               || elem.ElemType == StructuralType.BucklingStiffener;
                if (!applyThrow) continue;

                // Skip bracket type "B" — part vô hình theo rule set (không có stiff contact)
                if (elem.ElemType == StructuralType.Bracket && elem.BracketSubType == "B")
                    continue;

                // Position: midpoint of base segment
                double mx = (elem.BaseStart.X + elem.BaseEnd.X) / 2.0;
                double my = (elem.BaseStart.Y + elem.BaseEnd.Y) / 2.0;

                // Rotation: block có arrow default chỉ +Y (block local).
                // Muốn arrow chỉ theo throw=(vx,vy) → rotation = atan2(-vx, vy).
                double rotation = Math.Atan2(-elem.ThrowVecX, elem.ThrowVecY);

                var br = new BlockReference(new Point3d(mx, my, 0), btrId)
                {
                    Layer = LAYER_DEBUG_BLOCK,
                    Rotation = rotation,
                    ScaleFactors = new Scale3d(SYMBOL_SCALE, SYMBOL_SCALE, SYMBOL_SCALE)
                };

                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                // Attach XData for cleanup
                AttachDebugXData(br, elem.Guid, "ThrowThickness");
                count++;

                // Verbose log để đối chiếu với element thật
                Debug.WriteLine($"{LOG_PREFIX} ins TT guid={elem.Guid?.Substring(0, 8)} " +
                    $"type={elem.ElemType} pos=({mx:F0},{my:F0}) rot={rotation * 180.0 / Math.PI:F1}° " +
                    $"base=({elem.BaseStart.X:F0},{elem.BaseStart.Y:F0})→({elem.BaseEnd.X:F0},{elem.BaseEnd.Y:F0}) " +
                    $"throw=({elem.ThrowVecX:F2},{elem.ThrowVecY:F2})");
            }
            return count;
        }

        private static void AttachDebugXData(Entity ent, string elemGuid, string kind)
        {
            var rb = new ResultBuffer(
                new TypedValue(1001, DEBUG_XDATA_APP),
                new TypedValue(1000, kind),
                new TypedValue(1000, elemGuid ?? "")
            );
            ent.XData = rb;
        }
    }
}
