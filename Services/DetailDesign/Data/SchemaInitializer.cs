using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MCGCadPlugin.Utilities.DetailDesign;

namespace MCGCadPlugin.Services.DetailDesign.Data
{
    /// <summary>
    /// Tạo toàn bộ schema SQLite khi lần đầu chạy plugin.
    /// Gọi ExecuteAll() 1 lần duy nhất — idempotent (CREATE IF NOT EXISTS).
    /// </summary>
    public static class SchemaInitializer
    {
        private const string LOG_PREFIX = "[SchemaInitializer]";

        /// <summary>
        /// Static constructor — set PATH để tìm SQLite.Interop.dll native.
        /// Chạy 1 lần trước mọi method trong class.
        /// </summary>
        static SchemaInitializer()
        {
            try
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var nativeDir = Path.Combine(asmDir, "x64");
                if (Directory.Exists(nativeDir))
                {
                    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!path.Contains(nativeDir))
                    {
                        Environment.SetEnvironmentVariable("PATH", nativeDir + ";" + path);
                        Debug.WriteLine($"{LOG_PREFIX} Added native DLL path: {nativeDir}");
                    }
                }
                else
                {
                    Debug.WriteLine($"{LOG_PREFIX} WARNING: x64 directory not found: {nativeDir}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} ERROR setting native path: {ex.Message}");
            }
        }

        /// <summary>
        /// Tạo DB file (nếu chưa có) và chạy tất cả DDL statements.
        /// </summary>
        public static void ExecuteAll()
        {
            Debug.WriteLine($"{LOG_PREFIX} Starting schema initialization...");

            // Tạo thư mục chứa DB nếu chưa có
            var dbDir = Path.GetDirectoryName(DetailDesignConstants.DB_PATH);
            if (!Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
                Debug.WriteLine($"{LOG_PREFIX} Created directory: {dbDir}");
            }

            var connStr = $"Data Source={DetailDesignConstants.DB_PATH};Version=3;";
            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                Debug.WriteLine($"{LOG_PREFIX} Connected to: {DetailDesignConstants.DB_PATH}");

                ExecuteDDL(conn, DDL_PROJECTS);
                ExecuteDDL(conn, DDL_PANELS);
                ExecuteDDL(conn, DDL_STRUCTURAL_ELEMENTS);
                ExecuteDDL(conn, DDL_TOP_PLATE_REGIONS);
                ExecuteDDL(conn, DDL_PROFILES);
                ExecuteDDL(conn, DDL_GIRDERS);
                ExecuteDDL(conn, DDL_STIFFENERS);
                ExecuteDDL(conn, DDL_STIFFENER_CUTOUTS);
                ExecuteDDL(conn, DDL_BRACKETS);
                ExecuteDDL(conn, DDL_CLOSING_BOXES);
                ExecuteDDL(conn, DDL_CLOSING_BOX_MEMBERS);
                ExecuteDDL(conn, DDL_DOUBLING_PLATES);
                ExecuteDDL(conn, DDL_SECTIONS);
                ExecuteDDL(conn, DDL_SECTION_ELEMENTS);
                ExecuteDDL(conn, DDL_DETAILS);
                ExecuteDDL(conn, DDL_BOM_ITEMS);
                ExecuteDDL(conn, DDL_BLOCK_TRANSFORMS);
                ExecuteDDL(conn, DDL_AMBIGUOUS_ELEMENTS);
                ExecuteDDL(conn, DDL_SCHEMA_MIGRATIONS);

                // Indexes
                ExecuteDDL(conn, IDX_ELEM_PANEL);
                ExecuteDDL(conn, IDX_ELEM_TYPE);
                ExecuteDDL(conn, IDX_STIFF_PANEL);
                ExecuteDDL(conn, IDX_BRACKET_STIFF);

                // Migration: add columns mới nếu chưa có (idempotent)
                MigratePanelsTable(conn);

                // Unique index trên root_block_handle (ngăn duplicate panel khi re-scan)
                ExecuteDDL(conn, IDX_PANELS_HANDLE);

                // Data migration: cleanup duplicate panels (idempotent — chạy 1 lần duy nhất)
                CleanupDuplicatePanelsIfNeeded(conn);
            }

            Debug.WriteLine($"{LOG_PREFIX} Schema initialization COMPLETE.");
        }

        /// <summary>
        /// Xóa duplicate panels (cùng root_block_handle) + elements tương ứng.
        /// Giữ row có MIN(guid) = row cũ nhất. Đánh dấu trong schema_migrations để không chạy lại.
        /// </summary>
        private static void CleanupDuplicatePanelsIfNeeded(SQLiteConnection conn)
        {
            const string migName = "cleanup_duplicate_panels";

            // Check đã chạy chưa
            using (var cmd = new SQLiteCommand(
                "SELECT 1 FROM schema_migrations WHERE migration_name=@n LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@n", migName);
                if (cmd.ExecuteScalar() != null)
                {
                    Debug.WriteLine($"{LOG_PREFIX} [Cleanup] '{migName}' already applied — skip");
                    return;
                }
            }

            int deletedElems, deletedPanels;
            using (var tx = conn.BeginTransaction())
            {
                // Step 1: xóa elements của duplicate panels
                using (var cmd = new SQLiteCommand(@"
                    DELETE FROM structural_elements
                    WHERE panel_guid IN (
                        SELECT guid FROM panels
                        WHERE root_block_handle IS NOT NULL
                          AND guid NOT IN (
                              SELECT MIN(guid) FROM panels
                              WHERE root_block_handle IS NOT NULL
                              GROUP BY root_block_handle
                          )
                    );", conn, tx))
                {
                    deletedElems = cmd.ExecuteNonQuery();
                }

                // Step 2: xóa duplicate panels
                using (var cmd = new SQLiteCommand(@"
                    DELETE FROM panels
                    WHERE root_block_handle IS NOT NULL
                      AND guid NOT IN (
                          SELECT MIN(guid) FROM panels
                          WHERE root_block_handle IS NOT NULL
                          GROUP BY root_block_handle
                      );", conn, tx))
                {
                    deletedPanels = cmd.ExecuteNonQuery();
                }

                // Step 3: mark applied
                using (var cmd = new SQLiteCommand(@"
                    INSERT OR IGNORE INTO schema_migrations (migration_name, applied_at)
                    VALUES (@n, datetime('now'));", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@n", migName);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            Debug.WriteLine($"{LOG_PREFIX} [Cleanup] '{migName}' DONE — deleted {deletedPanels} panels + {deletedElems} elements");
        }

        /// <summary>
        /// Migration idempotent — ALTER TABLE ADD COLUMN chỉ khi column chưa tồn tại.
        /// SQLite không hỗ trợ "IF NOT EXISTS" cho ADD COLUMN → check qua pragma_table_info.
        /// </summary>
        private static void MigratePanelsTable(SQLiteConnection conn)
        {
            var needed = new[]
            {
                ("drawing_filepath", "TEXT"),
                ("created_by",       "TEXT"),
                ("updated_by",       "TEXT"),
                ("revision",         "TEXT"),
            };

            foreach (var (col, type) in needed)
            {
                if (ColumnExists(conn, "panels", col))
                {
                    Debug.WriteLine($"{LOG_PREFIX} [Migrate] panels.{col} already exists — skip");
                    continue;
                }
                var sql = $"ALTER TABLE panels ADD COLUMN {col} {type};";
                using (var cmd = new SQLiteCommand(sql, conn))
                    cmd.ExecuteNonQuery();
                Debug.WriteLine($"{LOG_PREFIX} [Migrate] ADDED panels.{col} {type}");
            }
        }

        private static bool ColumnExists(SQLiteConnection conn, string table, string col)
        {
            var sql = "SELECT 1 FROM pragma_table_info(@t) WHERE name=@c LIMIT 1;";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@t", table);
                cmd.Parameters.AddWithValue("@c", col);
                var o = cmd.ExecuteScalar();
                return o != null && o != DBNull.Value;
            }
        }

        private static void ExecuteDDL(SQLiteConnection conn, string ddl)
        {
            using (var cmd = new SQLiteCommand(ddl, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        #region DDL Statements

        private const string DDL_PROJECTS = @"
CREATE TABLE IF NOT EXISTS projects (
    guid        TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    dwg_path    TEXT,
    ship_name   TEXT,
    created_at  TEXT,
    updated_at  TEXT
);";

        private const string DDL_PANELS = @"
CREATE TABLE IF NOT EXISTS panels (
    guid                TEXT PRIMARY KEY,
    project_guid        TEXT REFERENCES projects(guid),
    name                TEXT NOT NULL,
    side                TEXT,
    input_mode          TEXT,
    root_block_handle   TEXT,
    top_plate_handle    TEXT,
    web_height          REAL,
    top_plate_thk       REAL,
    material            TEXT,
    centroid_x          REAL,
    centroid_y          REAL,
    side_auto_detected  INTEGER DEFAULT 0,
    model_hash          TEXT,
    created_at          TEXT,
    updated_at          TEXT
);";

        private const string DDL_STRUCTURAL_ELEMENTS = @"
CREATE TABLE IF NOT EXISTS structural_elements (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    elem_type       TEXT,
    acad_handle     TEXT,
    layer           TEXT,
    color_index     INTEGER,
    vertices_wcs    TEXT,
    centroid_x      REAL,
    centroid_y      REAL,
    obb_length      REAL,
    obb_width       REAL,
    obb_angle       REAL,
    area_poly       REAL,
    thickness       REAL,
    geometry_hash   TEXT,
    status          TEXT DEFAULT 'PENDING',
    is_flagged      INTEGER DEFAULT 0,
    flag_reason     TEXT,
    source_context  TEXT,
    source_block    TEXT,
    created_at      TEXT,
    updated_at      TEXT
);";

        private const string DDL_TOP_PLATE_REGIONS = @"
CREATE TABLE IF NOT EXISTS top_plate_regions (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    acad_handle     TEXT,
    region_index    INTEGER,
    area            REAL,
    thickness       REAL,
    centroid_x      REAL,
    centroid_y      REAL,
    perimeter       REAL
);";

        private const string DDL_PROFILES = @"
CREATE TABLE IF NOT EXISTS profiles (
    guid            TEXT PRIMARY KEY,
    code            TEXT UNIQUE NOT NULL,
    type            TEXT,
    height          REAL,
    web_thk         REAL,
    flange_w        REAL,
    flange_thk      REAL,
    area            REAL,
    Ix              REAL,
    unit_weight     REAL,
    standard        TEXT,
    block_cutout    TEXT,
    block_section   TEXT
);";

        private const string DDL_GIRDERS = @"
CREATE TABLE IF NOT EXISTS girders (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    web_elem_guid   TEXT REFERENCES structural_elements(guid),
    flange_top_guid TEXT REFERENCES structural_elements(guid),
    flange_bot_guid TEXT REFERENCES structural_elements(guid),
    web_height      REAL,
    web_thk         REAL,
    flange_width    REAL,
    flange_thk      REAL,
    orientation     TEXT,
    span_length     REAL
);";

        private const string DDL_STIFFENERS = @"
CREATE TABLE IF NOT EXISTS stiffeners (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    elem_guid       TEXT REFERENCES structural_elements(guid),
    profile_guid    TEXT REFERENCES profiles(guid),
    stiff_type      TEXT,
    orientation     TEXT,
    throw_vec_x     REAL,
    throw_vec_y     REAL,
    is_edge         INTEGER,
    end_a_type      TEXT,
    end_b_type      TEXT,
    span_length     REAL,
    total_length    REAL
);";

        private const string DDL_STIFFENER_CUTOUTS = @"
CREATE TABLE IF NOT EXISTS stiffener_cutouts (
    guid            TEXT PRIMARY KEY,
    stiffener_guid  TEXT REFERENCES stiffeners(guid),
    web_elem_guid   TEXT REFERENCES structural_elements(guid),
    position_x      REAL,
    position_y      REAL,
    block_name      TEXT,
    acad_handle     TEXT
);";

        private const string DDL_BRACKETS = @"
CREATE TABLE IF NOT EXISTS brackets (
    guid                TEXT PRIMARY KEY,
    panel_guid          TEXT REFERENCES panels(guid),
    elem_guid           TEXT REFERENCES structural_elements(guid),
    bracket_type        TEXT,
    stiffener_guid      TEXT REFERENCES stiffeners(guid),
    web_elem_guid       TEXT REFERENCES structural_elements(guid),
    girder_guid         TEXT REFERENCES girders(guid),
    leg_web             REAL,
    leg_stiffener       REAL,
    thickness           REAL,
    toe_length          REAL,
    has_flange          INTEGER,
    web_height          REAL,
    in_closing_box      INTEGER DEFAULT 0,
    closing_box_guid    TEXT REFERENCES closing_boxes(guid)
);";

        private const string DDL_CLOSING_BOXES = @"
CREATE TABLE IF NOT EXISTS closing_boxes (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    outer_minx      REAL,
    outer_miny      REAL,
    outer_maxx      REAL,
    outer_maxy      REAL,
    corner_position TEXT
);";

        private const string DDL_CLOSING_BOX_MEMBERS = @"
CREATE TABLE IF NOT EXISTS closing_box_members (
    guid             TEXT PRIMARY KEY,
    closing_box_guid TEXT REFERENCES closing_boxes(guid),
    elem_guid        TEXT REFERENCES structural_elements(guid)
);";

        private const string DDL_DOUBLING_PLATES = @"
CREATE TABLE IF NOT EXISTS doubling_plates (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    elem_guid       TEXT REFERENCES structural_elements(guid),
    width           REAL,
    length          REAL,
    thickness       REAL,
    centroid_x      REAL,
    centroid_y      REAL,
    material        TEXT
);";

        private const string DDL_SECTIONS = @"
CREATE TABLE IF NOT EXISTS sections (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    name            TEXT,
    cut_start_x     REAL,
    cut_start_y     REAL,
    cut_end_x       REAL,
    cut_end_y       REAL,
    view_direction  TEXT,
    scale           REAL,
    insert_x        REAL,
    insert_y        REAL,
    block_handle    TEXT,
    is_dirty        INTEGER DEFAULT 0,
    created_at      TEXT,
    updated_at      TEXT
);";

        private const string DDL_SECTION_ELEMENTS = @"
CREATE TABLE IF NOT EXISTS section_elements (
    guid             TEXT PRIMARY KEY,
    section_guid     TEXT REFERENCES sections(guid),
    source_elem_guid TEXT REFERENCES structural_elements(guid),
    elem_type        TEXT,
    local_u          REAL,
    local_v          REAL,
    width            REAL,
    height           REAL,
    rotation         REAL,
    block_name       TEXT,
    acad_handles     TEXT
);";

        private const string DDL_DETAILS = @"
CREATE TABLE IF NOT EXISTS details (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    source_type     TEXT,
    source_guid     TEXT,
    detail_ref      TEXT,
    scale           REAL,
    insert_x        REAL,
    insert_y        REAL,
    is_dirty        INTEGER DEFAULT 0,
    acad_handles    TEXT,
    created_at      TEXT,
    updated_at      TEXT
);";

        private const string DDL_BOM_ITEMS = @"
CREATE TABLE IF NOT EXISTS bom_items (
    guid             TEXT PRIMARY KEY,
    panel_guid       TEXT REFERENCES panels(guid),
    source_elem_guid TEXT REFERENCES structural_elements(guid),
    item_no          TEXT,
    description      TEXT,
    item_type        TEXT,
    quantity         INTEGER,
    length           REAL,
    width            REAL,
    thickness        REAL,
    profile_code     TEXT,
    material         TEXT,
    unit_weight      REAL,
    total_weight     REAL,
    remark           TEXT
);";

        private const string DDL_BLOCK_TRANSFORMS = @"
CREATE TABLE IF NOT EXISTS block_transforms (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    block_handle    TEXT,
    block_name      TEXT,
    parent_handle   TEXT,
    depth_level     INTEGER,
    transform_m44   TEXT,
    is_mirrored     INTEGER DEFAULT 0,
    mirror_axis     TEXT
);";

        private const string DDL_AMBIGUOUS_ELEMENTS = @"
CREATE TABLE IF NOT EXISTS ambiguous_elements (
    guid            TEXT PRIMARY KEY,
    panel_guid      TEXT REFERENCES panels(guid),
    acad_handle     TEXT,
    detected_type   TEXT,
    reason          TEXT,
    user_resolved   INTEGER DEFAULT 0,
    resolved_type   TEXT,
    resolved_at     TEXT
);";

        private const string DDL_SCHEMA_MIGRATIONS = @"
CREATE TABLE IF NOT EXISTS schema_migrations (
    migration_name  TEXT PRIMARY KEY,
    applied_at      TEXT
);";

        // Indexes
        private const string IDX_ELEM_PANEL = "CREATE INDEX IF NOT EXISTS idx_elem_panel ON structural_elements(panel_guid);";
        private const string IDX_ELEM_TYPE = "CREATE INDEX IF NOT EXISTS idx_elem_type ON structural_elements(elem_type);";
        private const string IDX_STIFF_PANEL = "CREATE INDEX IF NOT EXISTS idx_stiff_panel ON stiffeners(panel_guid);";
        private const string IDX_BRACKET_STIFF = "CREATE INDEX IF NOT EXISTS idx_bracket_stiff ON brackets(stiffener_guid);";
        private const string IDX_PANELS_HANDLE = @"
CREATE UNIQUE INDEX IF NOT EXISTS idx_panels_handle
ON panels(root_block_handle)
WHERE root_block_handle IS NOT NULL;";

        #endregion
    }
}
