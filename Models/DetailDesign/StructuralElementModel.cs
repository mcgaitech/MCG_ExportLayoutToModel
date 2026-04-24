using MCGCadPlugin.Models.DetailDesign.Enums;

namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Model cơ sở cho mọi structural entity được scan từ bản vẽ.
    /// Chứa dữ liệu geometry đã transform về WCS.
    /// </summary>
    public class StructuralElementModel
    {
        /// <summary>GUID duy nhất — link với XData và SQLite</summary>
        public string Guid { get; set; }

        /// <summary>GUID của panel chứa element này</summary>
        public string PanelGuid { get; set; }

        /// <summary>Loại kết cấu sau phân loại</summary>
        public StructuralType ElemType { get; set; }

        /// <summary>AutoCAD entity handle (hex string)</summary>
        public string AcadHandle { get; set; }

        /// <summary>Tên layer trong bản vẽ</summary>
        public string Layer { get; set; }

        /// <summary>Color index (40=stiffener, 6=BS, 256=ByLayer)</summary>
        public int ColorIndex { get; set; }

        /// <summary>Danh sách đỉnh đã transform về WCS</summary>
        public Point2dModel[] VerticesWCS { get; set; }

        /// <summary>Trọng tâm X (WCS, mm)</summary>
        public double CentroidX { get; set; }

        /// <summary>Trọng tâm Y (WCS, mm)</summary>
        public double CentroidY { get; set; }

        /// <summary>Chiều dài OBB (major axis, mm)</summary>
        public double ObbLength { get; set; }

        /// <summary>Chiều rộng OBB (minor axis = thickness, mm)</summary>
        public double ObbWidth { get; set; }

        /// <summary>Góc OBB so với trục X (rad)</summary>
        public double ObbAngle { get; set; }

        /// <summary>Diện tích polyline (mm²)</summary>
        public double AreaPoly { get; set; }

        /// <summary>Chiều dày — null nếu chưa xác định (hiện ? yellow)</summary>
        public double? Thickness { get; set; }

        /// <summary>Hash MD5 của WCS vertices — dùng cho dirty detection</summary>
        public string GeometryHash { get; set; }

        /// <summary>Trạng thái trong workflow</summary>
        public ElementStatus Status { get; set; }

        /// <summary>Đã bị flag bởi user hoặc system</summary>
        public bool IsFlagged { get; set; }

        /// <summary>Lý do flag (nếu có)</summary>
        public string FlagReason { get; set; }

        /// <summary>Nguồn gốc: STRUCTURE / CORNER / DIRECT</summary>
        public string SourceContext { get; set; }

        /// <summary>Tên block chứa entity (nếu từ block traversal)</summary>
        public string SourceBlock { get; set; }

        /// <summary>Entity này là lỗ khoét (cutout) nằm trong entity khác</summary>
        public bool IsHole { get; set; }

        /// <summary>GUID của entity cha chứa cutout này (nếu IsHole = true)</summary>
        public string ParentGuid { get; set; }

        /// <summary>
        /// Annotation type cho plan view: GE, CG, BS_sym, v.v.
        /// Null nếu không có annotation đặc biệt.
        /// </summary>
        public string AnnotationType { get; set; }

        /// <summary>Diện tích thực (net area = outer - sum(inner cutouts))</summary>
        public double? NetArea { get; set; }

        // ───── Base edge + throw vector (Phase 1 — debug symbols) ─────

        /// <summary>Đầu cạnh base — face ngược chiều throw (PickBase result, dùng cho throw symbol)</summary>
        public Point2dModel BaseStart { get; set; }

        /// <summary>Cuối cạnh base</summary>
        public Point2dModel BaseEnd { get; set; }

        /// <summary>Đầu unified construction line span — chung cho cả group (vẽ bởi DebugSymbolService)</summary>
        public Point2dModel CLSpanStart { get; set; }

        /// <summary>Cuối unified construction line span</summary>
        public Point2dModel CLSpanEnd { get; set; }

        /// <summary>Unit vector X của throw direction</summary>
        public double ThrowVecX { get; set; }

        /// <summary>Unit vector Y của throw direction</summary>
        public double ThrowVecY { get; set; }

        /// <summary>True nếu stiffener/BS nằm trong 150mm từ top plate boundary</summary>
        public bool IsEdgeElement { get; set; }

        /// <summary>"LONG" (length dọc X) hoặc "TRANS" (length dọc Y). Null nếu không xác định.</summary>
        public string OrientationClass { get; set; }

        // ───── Pass 1.5 — Bracket ↔ Stiffener linking ─────

        /// <summary>
        /// GUID của stiffener/BS được link với bracket này (Pass 1.5).
        /// Null nếu chưa tìm được hoặc element không phải Bracket.
        /// </summary>
        public string LinkedStiffenerGuid { get; set; }

        /// <summary>Đầu cạnh bracket tiếp xúc với stiffener (contact edge từ Pass 1.5).</summary>
        public Point2dModel StiffenerContactEdgeStart { get; set; }

        /// <summary>Cuối cạnh bracket tiếp xúc với stiffener.</summary>
        public Point2dModel StiffenerContactEdgeEnd { get; set; }

        /// <summary>Trạng thái trong workflow: AutoDetected / UserModified / Confirmed / Warning / HashChanged.</summary>
        public DataState DataState { get; set; }

        /// <summary>
        /// Bracket sub-type: "OB" / "IB" / "BF" / "B" — sync từ BracketAnalyzer sau scan.
        /// "B" = không có stiff contact → part vô hình theo rule set, KHÔNG insert ThrowThickness.
        /// Null/"" cho non-bracket.
        /// </summary>
        public string BracketSubType { get; set; }
    }
}
