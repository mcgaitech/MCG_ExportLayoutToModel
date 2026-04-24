# DD_TOPOLOGY.md — Topology Engine Rules

> Đọc file này khi làm Phase C (Topology Engine).
> Chứa toàn bộ rules quan hệ topology kèm pseudo-code.

---

## 1. Web–Flange Pairing

```csharp
// TopologyEngine.PairWebsWithFlanges()
// Mỗi WEB_PLATE → tìm FLANGE tiếp xúc

foreach (var web in elements.Where(e => e.Type == WEB_PLATE))
{
    var webOBB = web.OBB; // trục dài của web

    var flanges = elements
        .Where(e => e.Type == StructuralType.Flange)
        .Where(f =>
        {
            // Flange phải có cạnh dài song song với web
            double angleDiff = Math.Abs(f.OBB.AngleRad - webOBB.AngleRad);
            bool parallel = angleDiff < 0.05 || Math.Abs(angleDiff - Math.PI) < 0.05;

            // Flange phải tiếp xúc cạnh dài của web (dist < 1mm)
            double dist = MinDistanceBetweenPolylines(f, web);
            bool touching = dist < TOLERANCE_CONTACT;

            return parallel && touching;
        })
        .ToList();

    // Tạo GirderModel
    var girder = new GirderModel
    {
        WebElemGuid    = web.Guid,
        FlangTopGuid   = flanges.FirstOrDefault(f => IsAbove(f, web))?.Guid,
        FlangBotGuid   = flanges.FirstOrDefault(f => IsBelow(f, web))?.Guid,
        Orientation    = DetectOrientation(web.OBB.AngleRad),
        WebThk         = web.Thickness,
        FlangeWidth    = flanges.FirstOrDefault()?.OBB.Length ?? 0
    };
    girders.Add(girder);
}
```

---

## 2. Stiffener Endpoint Analysis

### Phân loại từng đầu của stiffener

```csharp
// TopologyEngine.AnalyzeStiffenerEndpoints()

foreach (var stiff in stiffeners)
{
    // Tính 2 endpoint từ OBB major axis
    var endA = stiff.OBB.Center - stiff.OBB.MajorAxis * (stiff.OBB.Length / 2);
    var endB = stiff.OBB.Center + stiff.OBB.MajorAxis * (stiff.OBB.Length / 2);

    stiff.EndAType = ClassifyEndpoint(endA, stiff, elements, topPlateBoundary);
    stiff.EndBType = ClassifyEndpoint(endB, stiff, elements, topPlateBoundary);
}

StiffenerEndType ClassifyEndpoint(
    Point2dModel endpoint,
    StructuralElementModel stiff,
    List<StructuralElementModel> elements,
    Polyline topPlateBoundary)
{
    // CHECK 1: Endpoint gần top plate boundary → SNIP
    double distToBoundary = DistanceToPolyline(endpoint, topPlateBoundary);
    if (distToBoundary < TOLERANCE_CONTACT)
        return StiffenerEndType.Snip;

    // CHECK 2: Stiffener cắt qua web (intersection count >= 2) → CUTOUT
    var touchingWebs = FindTouchingWebs(endpoint, stiff, elements);
    foreach (var web in touchingWebs)
    {
        var intersections = CountIntersections(stiff.Vertices, web.Vertices);
        if (intersections >= 2)
        {
            // Stiffener xuyên qua web → ghi nhận cutout
            RecordCutout(stiff, web, endpoint);
            return StiffenerEndType.Cutout;
        }
    }

    // CHECK 3: Endpoint chạm web (không xuyên) → BRACKET
    bool touchesWeb = touchingWebs.Any(w =>
        DistanceToPolyline(endpoint, w) < TOLERANCE_CONTACT);
    if (touchesWeb)
        return StiffenerEndType.Bracket;

    // CHECK 4: Không có gì → cũng là SNIP (free end)
    return StiffenerEndType.Snip;
}
```

---

## 3. Bracket Trigger & Validation

### Điều kiện tạo bracket

```csharp
// Bracket ĐƯỢC TẠO khi:
// AM_0 polyline tiếp xúc STIFFENER (color40) VÀ tiếp xúc WEB_PLATE
// AM_0 polyline KHÔNG tiếp xúc BS (BucklingStiffener)

// Bracket KHÔNG được tạo khi:
// - Stiffener gặp BS (buckling stiffener gặp nhau)
// - Đầu stiffener là SNIP

bool IsBracket(StructuralElementModel candidate,
               List<StructuralElementModel> elements)
{
    bool touchesStiff = elements
        .Where(e => e.Type == StructuralType.Stiffener)
        .Any(s => MinDistance(candidate, s) < TOLERANCE_CONTACT);

    bool touchesBS = elements
        .Where(e => e.Type == StructuralType.BucklingStiffener)
        .Any(s => MinDistance(candidate, s) < TOLERANCE_CONTACT);

    bool touchesWeb = elements
        .Where(e => e.Type == StructuralType.WebPlate)
        .Any(w => MinDistance(candidate, w) < TOLERANCE_CONTACT);

    // Nếu có BS → KHÔNG phải bracket (dù có thể touch web)
    if (touchesBS) return false;

    return touchesStiff && touchesWeb;
}
```

---

## 4. Bracket Leg Calculation

### 4 cạnh của bracket trong plan view

```csharp
// BracketAnalyzer.AnalyzeBracket(BracketModel bracket)

// Tìm 4 cạnh bằng cách kiểm tra từng edge của polyline
foreach (var edge in bracket.Edges)
{
    // Cạnh hàn Web: tiếp xúc WEB_PLATE
    if (TouchesEntity(edge, bracket.HostWeb, tol=1mm))
        bracket.EdgeWeb = edge;         // leg_web = edge.Length

    // Cạnh hàn Stiffener: tiếp xúc STIFFENER
    if (TouchesEntity(edge, bracket.HostStiffener, tol=1mm))
        bracket.EdgeStiff = edge;       // leg_stiff = edge.Length

    // Cạnh Flange/Toe: tiếp xúc FLANGE hoặc top
    if (TouchesEntity(edge, hostFlange, tol=1mm))
    {
        bracket.EdgeToe = edge;
        bracket.HasFlange = true;
        bracket.ToeLength = hostFlange.OBB.Width / 2; // b_f/2
    }

    // Cạnh tự do: không tiếp xúc entity nào
    // → chamfer edge (computed từ 2 điểm còn lại)
}

// Nếu không tìm được flange
if (!bracket.HasFlange)
    bracket.ToeLength = BRACKET_TOE_DEFAULT; // 15mm

// Leg dimensions
bracket.LegWeb       = bracket.EdgeWeb?.Length ?? 0;
bracket.LegStiffener = bracket.EdgeStiff?.Length ?? 0;
```

---

## 5. Closing Box Detection

```csharp
// ClosingBoxDetector.DetectClosingBoxes(List<StructuralElementModel> am0Elements)

// STEP 1: Tìm các nhóm AM_0 share edge với nhau
var groups = new List<List<StructuralElementModel>>();

foreach (var elem in am0Elements.Where(e => e.Type == WebPlate || e.Type == AM0_Unclassified))
{
    var touching = am0Elements
        .Where(other => other != elem)
        .Where(other => SharesEdge(elem, other, TOLERANCE_CONTACT))
        .ToList();

    if (touching.Count > 0)
    {
        // Merge vào group hiện có hoặc tạo group mới
        MergeOrCreateGroup(groups, elem, touching);
    }
}

// STEP 2: Xác định outer boundary của mỗi group
foreach (var group in groups)
{
    var closingBox = new ClosingBoxModel
    {
        MemberGuids   = group.Select(e => e.Guid).ToList(),
        OuterBoundary = ComputeUnionBoundingBox(group), // AABB
        Position      = DetectCornerPosition(group, topPlate) // TL/TR/BL/BR
    };
    closingBoxes.Add(closingBox);
}

// STEP 3: Bracket leg limit = closing box outer boundary
// → Khi tính bracket leg tiếp xúc closing box:
//   leg_length = dist(stiffener_end, closingBox.OuterBoundary.Face)
//   KHÔNG cho phép bracket xâm nhập vào closing box interior
```

---

## 6. Throw Vector & OB/IB

```csharp
// ThrowVectorEngine.ComputeThrowVector(
//     StiffenerModel stiff, PanelContext panel) → Vector2d

Point2dModel stiffCenter = stiff.OBB.Center;
Point2dModel panelCenter = panel.TopPlate.Centroid;

// STEP 1: Edge or Inner?
double distToEdge = panel.TopPlate.MinDistanceToEdge(stiffCenter);
bool isEdge = IsEdgeStiffener(stiff, panel.Stiffeners, panel.TopPlate);

if (isEdge)
{
    // Edge → đổ ra xa tâm top plate
    return Normalize(stiffCenter - panelCenter);
}

// STEP 2: Inner → theo PanelSide
switch (panel.Side)
{
    case PanelSide.Port:
        return STBD_DIR; // (0, -1)

    case PanelSide.Starboard:
        return PORT_DIR; // (0, +1)

    case PanelSide.Center:
        // Kiểm tra nằm trên trục đối xứng (X axis của panel)
        double distToAxis = Math.Abs(stiffCenter.Y - panelCenter.Y);
        if (distToAxis < TOLERANCE_CONTACT)
            return STBD_DIR; // ưu tiên Starboard

        return Normalize(panelCenter - stiffCenter); // về tâm panel
}

// OB/IB determination:
// throwVec = computed above
// V_bracket = normalize(bracket.Centroid - contactPoint)
// dot = Dot(throwVec, V_bracket)
// dot > 0 → throw hướng ra phía bracket → OB
// dot < 0 → throw hướng ngược bracket    → IB
```

---

## 7. Edge Stiffener Detection

```csharp
// IsEdgeStiffener: stiffener có khoảng cách nhỏ nhất tới mép top plate
// trong nhóm stiffeners, không cần tiếp xúc mép

bool IsEdgeStiffener(StiffenerModel stiff,
                     List<StiffenerModel> allStiffeners,
                     TopPlateRegionModel topPlate)
{
    // Tính khoảng cách từ centroid stiffener tới mép top plate gần nhất
    double myDist = topPlate.Boundary.MinDistanceToEdge(stiff.OBB.Center);

    // So sánh với min dist của tất cả stiffeners cùng hướng (trans/long)
    var sameDir = allStiffeners.Where(s => s.Orientation == stiff.Orientation);
    double minDist = sameDir.Min(s =>
        topPlate.Boundary.MinDistanceToEdge(s.OBB.Center));

    // Edge nếu có khoảng cách nhỏ nhất (hoặc bằng min với tolerance)
    return Math.Abs(myDist - minDist) < TOLERANCE_CONTACT;
}
// NOTE: Mỗi cạnh top plate có 1 edge stiffener → 4 edge stiffeners cho rectangle panel
```

---

## 8. Both-Ends Rule

```
NGUYÊN TẮC:
  - Stiffener có 2 đầu → gắn bracket tại CẢ 2 đầu (nếu EndType = BRACKET)
  - Throw direction nhất quán dọc theo stiffener
  - → Cả 2 bracket cùng là OB hoặc cùng là IB
  - Leg length tính độc lập từng đầu (có thể khác nhau)

CUTOUT vs BRACKET:
  Stiffener xuyên qua web A (cutout) + kết thúc tại web B (bracket)
  → Web A: insert HP120x6_Cutout block tại vị trí intersection
  → Web B: bracket tại đầu cuối
  Block name = "{ProfileCode}_Cutout" (ví dụ "HP120x6_Cutout")
  Lookup từ Symbol.dwg: C:\CustomTools\Symbol.dwg
```
