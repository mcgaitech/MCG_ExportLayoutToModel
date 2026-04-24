using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Utilities.DetailDesign;
using System;
using System.Collections.Generic;

namespace MCGCadPlugin.Services.LayoutToModel
{
    public class LayoutToModelService : ILayoutToModelService
    {
        private readonly LogHelper _logHelper;

        public LayoutToModelService()
        {
            // Khởi tạo LogHelper khớp với constructor của bạn
            _logHelper = new LogHelper("LayoutToModel", "Export");
        }

        public void ExportAllLayoutsToModel(Database db)
        {
            // Bắt đầu Transaction từ Database Side-load
            using (var trans = db.TransactionManager.StartTransaction())
            {
                try
                {
                    ObjectId modelId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    var modelSpace = (BlockTableRecord)trans.GetObject(modelId, OpenMode.ForWrite);

                    var layoutDict = (DBDictionary)trans.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    double currentOffsetX = 0;
                    double gap = 10000.0;

                    foreach (DBDictionaryEntry entry in layoutDict)
                    {
                        if (entry.Key.ToUpper() == "MODEL") continue;

                        var layout = (Layout)trans.GetObject(entry.Value, OpenMode.ForRead);
                        _logHelper.Info($"Đang xử lý Layout: {layout.LayoutName}");

                        var paperSpace = (BlockTableRecord)trans.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                        Matrix3d moveMat = Matrix3d.Displacement(new Vector3d(currentOffsetX, 0, 0));

                        foreach (ObjectId id in paperSpace)
                        {
                            // Chỉ xử lý nếu đối tượng thuộc đúng database đang mở ngầm
                            if (id.Database != db) continue;

                            Entity ent = trans.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent == null || ent is Viewport) continue;

                            // XỬ LÝ ĐỐI TƯỢNG INVENTOR QUA EXPLODE
                            using (DBObjectCollection explodedObjects = new DBObjectCollection())
                            {
                                try
                                {
                                    ent.Explode(explodedObjects);
                                }
                                catch
                                {
                                    // Nếu không explode được (đối tượng thường), ta Clone thủ công
                                    explodedObjects.Add(ent.Clone() as Entity);
                                }

                                foreach (DBObject obj in explodedObjects)
                                {
                                    Entity expEnt = obj as Entity;
                                    if (expEnt != null)
                                    {
                                        // Rất quan trọng: Gán database mặc định TRƯỚC khi add vào Model
                                        expEnt.SetDatabaseDefaults(db);
                                        expEnt.TransformBy(moveMat);
                                        
                                        modelSpace.AppendEntity(expEnt);
                                        trans.AddNewlyCreatedDBObject(expEnt, true);
                                    }
                                }
                            }
                        }
                        currentOffsetX += gap;
                    }
                    trans.Commit();
                }
                catch (Exception ex)
                {
                    _logHelper.Error($"Lỗi Transaction: {ex.Message}");
                    trans.Abort();
                    throw;
                }
            }
        }

        public void BatchProcessFiles(List<string> filePaths)
        {
            foreach (var path in filePaths)
            {
                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".dwg");
                try
                {
                    System.IO.File.Copy(path, tempPath, true);

                    using (Database db = new Database(false, true))
                    {
                        db.ReadDwgFile(tempPath, FileOpenMode.OpenForReadAndWriteNoShare, true, "");
                        db.CloseInput(true);

                        // CHÌA KHÓA KHẮC PHỤC: Chuyển WorkingDatabase TRƯỚC khi bắt đầu bất cứ thứ gì
                        Database oldDb = HostApplicationServices.WorkingDatabase;
                        HostApplicationServices.WorkingDatabase = db;

                        try
                        {
                            ExportAllLayoutsToModel(db);
                            db.SaveAs(tempPath, DwgVersion.Current);
                        }
                        finally
                        {
                            HostApplicationServices.WorkingDatabase = oldDb;
                        }
                    }

                    System.IO.File.Copy(tempPath, path, true);
                    _logHelper.Info($"Thành công: {System.IO.Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    _logHelper.Error($"Lỗi file {System.IO.Path.GetFileName(path)}: {ex.Message}");
                }
                finally
                {
                    if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
                }
            }
        }
    }
}