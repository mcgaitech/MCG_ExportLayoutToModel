using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using MCGCadPlugin.Models.DetailDesign;
using MCGCadPlugin.Models.DetailDesign.Enums;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Data
{
    /// <summary>
    /// SQLite repository — CRUD cho DetailDesign module.
    /// Connection string dùng DB_PATH từ constants.
    /// </summary>
    public class DetailDesignRepository : IDetailDesignRepository
    {
        private const string LOG_PREFIX = "[DetailDesignRepository]";
        private readonly string _connStr;

        public DetailDesignRepository()
        {
            _connStr = $"Data Source={DetailDesignConstants.DB_PATH};Version=3;";
            Debug.WriteLine($"{LOG_PREFIX} Initialized — DB: {DetailDesignConstants.DB_PATH}");
        }

        /// <summary>Khởi tạo schema + seed profiles</summary>
        public void InitializeDatabase()
        {
            Debug.WriteLine($"{LOG_PREFIX} Starting InitializeDatabase...");
            SchemaInitializer.ExecuteAll();
            ProfileCatalogSeeder.Seed();
            Debug.WriteLine($"{LOG_PREFIX} InitializeDatabase COMPLETE.");
        }

        /// <summary>
        /// Thêm hoặc cập nhật panel.
        /// Duplicate detection:
        ///   1. root_block_handle NOT NULL → SELECT guid WHERE root_block_handle=@h
        ///   2. Fallback: name + side + drawing_filepath
        /// Found → UPDATE (giữ guid, created_at, created_by).
        /// Not found → INSERT new guid.
        /// Trả về guid final + mutate panel.Guid in-place.
        /// </summary>
        public string UpsertPanel(PanelContext panel)
        {
            Debug.WriteLine($"{LOG_PREFIX} UpsertPanel: {panel.Name}...");
            string loginName = GetLoginName();
            string drawingFilepath = GetActiveDrawingFilepath();
            string now = DateTime.Now.ToString("o");

            using (var conn = new SQLiteConnection(_connStr))
            {
                conn.Open();

                // Step 1 — Lookup existing panel
                string existingGuid = null;
                if (!string.IsNullOrEmpty(panel.RootBlockHandle))
                {
                    using (var cmd = new SQLiteCommand(
                        "SELECT guid FROM panels WHERE root_block_handle=@h LIMIT 1;", conn))
                    {
                        cmd.Parameters.AddWithValue("@h", panel.RootBlockHandle);
                        existingGuid = cmd.ExecuteScalar() as string;
                    }
                }
                if (existingGuid == null && !string.IsNullOrEmpty(panel.Name))
                {
                    using (var cmd = new SQLiteCommand(
                        @"SELECT guid FROM panels
                          WHERE name=@n AND side=@s
                            AND IFNULL(drawing_filepath,'')=IFNULL(@fp,'')
                          LIMIT 1;", conn))
                    {
                        cmd.Parameters.AddWithValue("@n", panel.Name);
                        cmd.Parameters.AddWithValue("@s", panel.Side.ToString());
                        cmd.Parameters.AddWithValue("@fp", (object)drawingFilepath ?? DBNull.Value);
                        existingGuid = cmd.ExecuteScalar() as string;
                    }
                }

                if (existingGuid != null)
                {
                    // Step 2 — UPDATE (preserve guid + created_*)
                    panel.Guid = existingGuid;
                    using (var cmd = new SQLiteCommand(@"
                        UPDATE panels SET
                            project_guid        = @project_guid,
                            name                = @name,
                            side                = @side,
                            input_mode          = @input_mode,
                            root_block_handle   = @root_block_handle,
                            top_plate_handle    = @top_plate_handle,
                            web_height          = @web_height,
                            top_plate_thk       = @top_plate_thk,
                            material            = @material,
                            centroid_x          = @centroid_x,
                            centroid_y          = @centroid_y,
                            side_auto_detected  = @side_auto_detected,
                            drawing_filepath    = @drawing_filepath,
                            revision            = @revision,
                            updated_at          = @updated_at,
                            updated_by          = @updated_by
                        WHERE guid = @guid;", conn))
                    {
                        cmd.Parameters.AddWithValue("@guid", existingGuid);
                        BindCommonPanelParams(cmd, panel, drawingFilepath, now);
                        cmd.Parameters.AddWithValue("@updated_by", loginName);
                        cmd.ExecuteNonQuery();
                    }
                    Debug.WriteLine($"{LOG_PREFIX} UpsertPanel UPDATE: {panel.Name} guid={existingGuid}");
                    return existingGuid;
                }
                else
                {
                    // Step 3 — INSERT new
                    if (string.IsNullOrEmpty(panel.Guid))
                        panel.Guid = Guid.NewGuid().ToString();
                    using (var cmd = new SQLiteCommand(@"
                        INSERT INTO panels
                            (guid, project_guid, name, side, input_mode, root_block_handle,
                             top_plate_handle, web_height, top_plate_thk, material,
                             centroid_x, centroid_y, side_auto_detected,
                             drawing_filepath, revision,
                             created_at, created_by, updated_at, updated_by)
                        VALUES (@guid, @project_guid, @name, @side, @input_mode, @root_block_handle,
                                @top_plate_handle, @web_height, @top_plate_thk, @material,
                                @centroid_x, @centroid_y, @side_auto_detected,
                                @drawing_filepath, @revision,
                                @created_at, @created_by, @updated_at, @updated_by);", conn))
                    {
                        cmd.Parameters.AddWithValue("@guid", panel.Guid);
                        BindCommonPanelParams(cmd, panel, drawingFilepath, now);
                        cmd.Parameters.AddWithValue("@created_at", now);
                        cmd.Parameters.AddWithValue("@created_by", loginName);
                        cmd.Parameters.AddWithValue("@updated_by", loginName);
                        cmd.ExecuteNonQuery();
                    }
                    Debug.WriteLine($"{LOG_PREFIX} UpsertPanel INSERT: {panel.Name} guid={panel.Guid} by={loginName}");
                    return panel.Guid;
                }
            }
        }

        private static void BindCommonPanelParams(SQLiteCommand cmd, PanelContext panel,
            string drawingFilepath, string now)
        {
            cmd.Parameters.AddWithValue("@project_guid", (object)panel.ProjectGuid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", panel.Name);
            cmd.Parameters.AddWithValue("@side", panel.Side.ToString());
            cmd.Parameters.AddWithValue("@input_mode", panel.Mode.ToString());
            cmd.Parameters.AddWithValue("@root_block_handle", (object)panel.RootBlockHandle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@top_plate_handle", (object)panel.TopPlateHandle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@web_height", (object)panel.WebHeight ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@top_plate_thk", (object)panel.TopPlateThickness ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@material", (object)panel.Material ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@centroid_x", (object)panel.CentroidX ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@centroid_y", (object)panel.CentroidY ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@side_auto_detected", panel.SideAutoDetected ? 1 : 0);
            cmd.Parameters.AddWithValue("@drawing_filepath", (object)drawingFilepath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@revision", (object)panel.Revision ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@updated_at", now);
        }

        private static string GetLoginName()
        {
            try
            {
                var v = Autodesk.AutoCAD.ApplicationServices.Application
                    .GetSystemVariable("LOGINNAME") as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { /* AutoCAD not available (unit test etc) */ }
            return System.Environment.UserName;
        }

        private static string GetActiveDrawingFilepath()
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application
                    .DocumentManager?.MdiActiveDocument;
                return doc?.Name;
            }
            catch { return null; }
        }

        /// <summary>Lấy panel theo GUID</summary>
        public PanelContext GetPanel(string guid)
        {
            using (var conn = new SQLiteConnection(_connStr))
            {
                conn.Open();
                var sql = "SELECT * FROM panels WHERE guid = @guid";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@guid", guid);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        return new PanelContext
                        {
                            Guid = reader["guid"] as string,
                            ProjectGuid = reader["project_guid"] as string,
                            Name = reader["name"] as string,
                            Side = Enum.TryParse<PanelSide>(reader["side"] as string, out var s) ? s : PanelSide.Center,
                            Mode = Enum.TryParse<InputMode>(reader["input_mode"] as string, out var m) ? m : InputMode.Block,
                            RootBlockHandle = reader["root_block_handle"] as string,
                            TopPlateHandle = reader["top_plate_handle"] as string,
                            WebHeight = reader["web_height"] as double?,
                            TopPlateThickness = reader["top_plate_thk"] as double?,
                            Material = reader["material"] as string,
                            CentroidX = reader["centroid_x"] as double?,
                            CentroidY = reader["centroid_y"] as double?,
                            SideAutoDetected = Convert.ToInt32(reader["side_auto_detected"]) == 1
                        };
                    }
                }
            }
        }

        /// <summary>Thêm hoặc cập nhật structural element</summary>
        public void UpsertElement(StructuralElementModel elem)
        {
            using (var conn = new SQLiteConnection(_connStr))
            {
                conn.Open();
                var sql = @"INSERT OR REPLACE INTO structural_elements
                    (guid, panel_guid, elem_type, acad_handle, layer, color_index,
                     centroid_x, centroid_y, obb_length, obb_width, obb_angle, area_poly,
                     thickness, geometry_hash, status, is_flagged, flag_reason,
                     source_context, source_block, updated_at)
                    VALUES (@guid, @panel_guid, @elem_type, @acad_handle, @layer, @color_index,
                            @centroid_x, @centroid_y, @obb_length, @obb_width, @obb_angle, @area_poly,
                            @thickness, @geometry_hash, @status, @is_flagged, @flag_reason,
                            @source_context, @source_block, @updated_at)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@guid", elem.Guid);
                    cmd.Parameters.AddWithValue("@panel_guid", elem.PanelGuid);
                    cmd.Parameters.AddWithValue("@elem_type", elem.ElemType.ToString());
                    cmd.Parameters.AddWithValue("@acad_handle", (object)elem.AcadHandle ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@layer", (object)elem.Layer ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@color_index", elem.ColorIndex);
                    cmd.Parameters.AddWithValue("@centroid_x", elem.CentroidX);
                    cmd.Parameters.AddWithValue("@centroid_y", elem.CentroidY);
                    cmd.Parameters.AddWithValue("@obb_length", elem.ObbLength);
                    cmd.Parameters.AddWithValue("@obb_width", elem.ObbWidth);
                    cmd.Parameters.AddWithValue("@obb_angle", elem.ObbAngle);
                    cmd.Parameters.AddWithValue("@area_poly", elem.AreaPoly);
                    cmd.Parameters.AddWithValue("@thickness", (object)elem.Thickness ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@geometry_hash", (object)elem.GeometryHash ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", elem.Status.ToString());
                    cmd.Parameters.AddWithValue("@is_flagged", elem.IsFlagged ? 1 : 0);
                    cmd.Parameters.AddWithValue("@flag_reason", (object)elem.FlagReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@source_context", (object)elem.SourceContext ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@source_block", (object)elem.SourceBlock ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Lấy element theo GUID</summary>
        public StructuralElementModel GetElement(string guid)
        {
            using (var conn = new SQLiteConnection(_connStr))
            {
                conn.Open();
                var sql = "SELECT * FROM structural_elements WHERE guid = @guid";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@guid", guid);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        return ReadElement(reader);
                    }
                }
            }
        }

        /// <summary>Lấy tất cả elements của 1 panel</summary>
        public List<StructuralElementModel> GetElementsByPanel(string panelGuid)
        {
            var result = new List<StructuralElementModel>();
            using (var conn = new SQLiteConnection(_connStr))
            {
                conn.Open();
                var sql = "SELECT * FROM structural_elements WHERE panel_guid = @panel_guid";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@panel_guid", panelGuid);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            result.Add(ReadElement(reader));
                    }
                }
            }
            return result;
        }

        /// <summary>Lấy tất cả profiles từ catalog</summary>
        public List<ProfileModel> GetAllProfiles()
        {
            var result = new List<ProfileModel>();
            using (var conn = new SQLiteConnection(_connStr))
            {
                conn.Open();
                var sql = "SELECT * FROM profiles ORDER BY type, code";
                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new ProfileModel
                        {
                            Guid = reader["guid"] as string,
                            Code = reader["code"] as string,
                            Type = reader["type"] as string,
                            Height = reader["height"] as double?,
                            WebThickness = reader["web_thk"] as double?,
                            FlangeWidth = reader["flange_w"] as double?,
                            FlangeThickness = reader["flange_thk"] as double?,
                            Area = reader["area"] as double?,
                            Ix = reader["Ix"] as double?,
                            UnitWeight = reader["unit_weight"] as double?,
                            Standard = reader["standard"] as string,
                            BlockCutout = reader["block_cutout"] as string,
                            BlockSection = reader["block_section"] as string
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>Lấy profile theo code</summary>
        public ProfileModel GetProfileByCode(string code)
        {
            using (var conn = new SQLiteConnection(_connStr))
            {
                conn.Open();
                var sql = "SELECT * FROM profiles WHERE code = @code";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@code", code);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        return new ProfileModel
                        {
                            Guid = reader["guid"] as string,
                            Code = reader["code"] as string,
                            Type = reader["type"] as string,
                            Height = reader["height"] as double?,
                            WebThickness = reader["web_thk"] as double?,
                            UnitWeight = reader["unit_weight"] as double?,
                            Standard = reader["standard"] as string
                        };
                    }
                }
            }
        }

        #region Private Helpers

        private static StructuralElementModel ReadElement(SQLiteDataReader reader)
        {
            return new StructuralElementModel
            {
                Guid = reader["guid"] as string,
                PanelGuid = reader["panel_guid"] as string,
                ElemType = Enum.TryParse<StructuralType>(reader["elem_type"] as string, out var t) ? t : StructuralType.AM0_Unclassified,
                AcadHandle = reader["acad_handle"] as string,
                Layer = reader["layer"] as string,
                ColorIndex = Convert.ToInt32(reader["color_index"]),
                CentroidX = Convert.ToDouble(reader["centroid_x"]),
                CentroidY = Convert.ToDouble(reader["centroid_y"]),
                ObbLength = Convert.ToDouble(reader["obb_length"]),
                ObbWidth = Convert.ToDouble(reader["obb_width"]),
                ObbAngle = Convert.ToDouble(reader["obb_angle"]),
                AreaPoly = Convert.ToDouble(reader["area_poly"]),
                Thickness = reader["thickness"] as double?,
                GeometryHash = reader["geometry_hash"] as string,
                Status = Enum.TryParse<ElementStatus>(reader["status"] as string, out var s) ? s : ElementStatus.Pending,
                IsFlagged = Convert.ToInt32(reader["is_flagged"]) == 1,
                FlagReason = reader["flag_reason"] as string,
                SourceContext = reader["source_context"] as string,
                SourceBlock = reader["source_block"] as string
            };
        }

        #endregion
    }
}
