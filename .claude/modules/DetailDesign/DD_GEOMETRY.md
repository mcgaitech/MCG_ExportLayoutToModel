# DD_GEOMETRY.md — Geometry Algorithms

> Đọc file này khi làm Phase A (OBBCalculator, GeometryHasher)
> và Phase B (WCSTransformer).

---

## 1. OBB Calculator — PCA Algorithm

### OBBCalculator.Compute(Point2dModel[] vertices) → OBBResult

```csharp
// BƯỚC 1: Convex Hull
// Dùng ConvexHullHelper.Compute(vertices) → hull[]
// Graham scan hoặc Jarvis march

// BƯỚC 2: PCA trên convex hull points
double meanX = hull.Average(p => p.X);
double meanY = hull.Average(p => p.Y);

// Covariance matrix
double cxx = 0, cxy = 0, cyy = 0;
foreach (var p in hull)
{
    double dx = p.X - meanX;
    double dy = p.Y - meanY;
    cxx += dx * dx;
    cxy += dx * dy;
    cyy += dy * dy;
}
int n = hull.Length;
cxx /= n; cxy /= n; cyy /= n;

// Eigenvalues của 2×2 symmetric matrix [cxx cxy; cxy cyy]
double trace = cxx + cyy;
double det   = cxx * cyy - cxy * cxy;
double disc  = Math.Sqrt(Math.Max(0, trace * trace / 4 - det));
double lambda1 = trace / 2 + disc; // major eigenvalue
double lambda2 = trace / 2 - disc; // minor eigenvalue

// Eigenvector cho lambda1 (major axis)
Vector2d majorAxis;
if (Math.Abs(cxy) > 1e-10)
{
    majorAxis = Normalize(new Vector2d(lambda1 - cyy, cxy));
}
else
{
    // Already axis-aligned
    majorAxis = cxx >= cyy
        ? new Vector2d(1, 0)
        : new Vector2d(0, 1);
}
Vector2d minorAxis = new Vector2d(-majorAxis.Y, majorAxis.X);

// BƯỚC 3: Project lên 2 trục → extent
double minMaj = double.MaxValue, maxMaj = double.MinValue;
double minMin = double.MaxValue, maxMin = double.MinValue;
foreach (var p in hull)
{
    double pmaj = Dot(new Vector2d(p.X - meanX, p.Y - meanY), majorAxis);
    double pmin = Dot(new Vector2d(p.X - meanX, p.Y - meanY), minorAxis);
    minMaj = Math.Min(minMaj, pmaj); maxMaj = Math.Max(maxMaj, pmaj);
    minMin = Math.Min(minMin, pmin); maxMin = Math.Max(maxMin, pmin);
}

double length = maxMaj - minMaj;   // major dimension
double width  = maxMin - minMin;   // minor dimension = thickness

// BƯỚC 4: Validate với Area method
double areaOBB  = length * width;
double areaPoly = ComputePolygonArea(vertices);
double ratio    = areaPoly / areaOBB;

// Nếu ratio < 0.85 → non-rectangular → dùng area method cho thickness
double finalThickness = ratio >= 0.85
    ? width
    : areaPoly / length;

return new OBBResult
{
    Center     = new Point2dModel(meanX, meanY),
    Length     = length,
    Width      = finalThickness,
    AngleRad   = Math.Atan2(majorAxis.Y, majorAxis.X),
    MajorAxis  = majorAxis,
    MinorAxis  = minorAxis,
    AspectRatio = length / Math.Max(finalThickness, 0.001)
};
```

---

## 2. WCS Transformer

### WCSTransformer — Block transform accumulation

```csharp
// WCSTransformer.TransformVertices(
//     Polyline pline, Matrix3d M_total) → Point2dModel[]

var result = new Point2dModel[pline.NumberOfVertices];
for (int i = 0; i < pline.NumberOfVertices; i++)
{
    // Polyline vertex là Point2d (trong block space)
    var pt2d = pline.GetPoint2dAt(i);
    // Elevate to 3D (Z=0 vì polyline nằm trong XY plane)
    var pt3d = new Point3d(pt2d.X, pt2d.Y, 0);
    // Apply accumulated transform
    var transformed = pt3d.TransformBy(M_total);
    result[i] = new Point2dModel(transformed.X, transformed.Y);
}
return result;

// Accumulate transform khi traverse:
// Level 0: M0 = rootBlockRef.BlockTransform
// Level 1: M1 = M0 * subBlockRef.BlockTransform
// Level N: MN = M(N-1) * nestedRef.BlockTransform

// Mirror detection:
// var rotation = M_total.Inverse().Transpose();
// double det = rotation.GetDeterminant();
// bool isMirrored = det < 0;
```

---

## 3. Convex Hull — Graham Scan

```csharp
// ConvexHullHelper.Compute(Point2dModel[] pts) → Point2dModel[]

// BƯỚC 1: Tìm điểm thấp nhất (min Y, tie-break min X)
var pivot = pts.OrderBy(p => p.Y).ThenBy(p => p.X).First();

// BƯỚC 2: Sort theo góc polar từ pivot
var sorted = pts
    .Where(p => p != pivot)
    .OrderBy(p => Math.Atan2(p.Y - pivot.Y, p.X - pivot.X))
    .ThenBy(p => Distance(pivot, p))
    .ToList();

// BƯỚC 3: Graham scan
var stack = new Stack<Point2dModel>();
stack.Push(pivot);
stack.Push(sorted[0]);

for (int i = 1; i < sorted.Count; i++)
{
    while (stack.Count > 1 &&
           CrossProduct(stack.ElementAt(1), stack.Peek(), sorted[i]) <= 0)
        stack.Pop();
    stack.Push(sorted[i]);
}

return stack.ToArray();

// CrossProduct(O, A, B) = (A-O) × (B-O)
// = (A.X-O.X)*(B.Y-O.Y) - (A.Y-O.Y)*(B.X-O.X)
// > 0 → counter-clockwise (keep)
// ≤ 0 → clockwise or collinear (pop)
```

---

## 4. Intersection Detection

```csharp
// IntersectionEngine.CountIntersections(
//     Point2dModel[] poly1, Point2dModel[] poly2) → int
// Đếm số lần poly1 (stiffener axis) cắt poly2 (web boundary)

// Dùng line segment intersection cho mỗi cặp edges
// Stiffener axis = major axis line của OBB stiffener
// Web boundary = edges của web polyline

// Segment intersection test:
bool SegmentsIntersect(Point2dModel p1, Point2dModel p2,
                       Point2dModel p3, Point2dModel p4)
{
    double d1 = CrossProduct(p3, p4, p1);
    double d2 = CrossProduct(p3, p4, p2);
    double d3 = CrossProduct(p1, p2, p3);
    double d4 = CrossProduct(p1, p2, p4);

    if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
        ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        return true;

    // Collinear cases...
    return false;
}
```

---

## 5. MinDistance Between Polylines

```csharp
// MinDistance(StructuralElementModel a, StructuralElementModel b) → double
// Tính khoảng cách tối thiểu giữa 2 closed polylines

double minDist = double.MaxValue;
var vertsA = a.VerticesWCS;
var vertsB = b.VerticesWCS;

for (int i = 0; i < vertsA.Length; i++)
{
    var a1 = vertsA[i];
    var a2 = vertsA[(i + 1) % vertsA.Length];

    for (int j = 0; j < vertsB.Length; j++)
    {
        var b1 = vertsB[j];
        var b2 = vertsB[(j + 1) % vertsB.Length];

        double d = SegmentToSegmentDistance(a1, a2, b1, b2);
        minDist = Math.Min(minDist, d);
    }
}
return minDist;
// < TOLERANCE_CONTACT (1mm) → touching
```

---

## 6. Drawing Units Validation

```csharp
// DrawingUnitsValidator.Validate(Database db) → bool

// INSUNITS values: 1=inches, 2=feet, 4=mm, 5=cm, 6=meters
var insunits = (UnitsValue)(int)db.Insunits;
if (insunits != UnitsValue.Millimeters)
{
    // Hiển thị lỗi tiếng Việt cho user
    Application.ShowAlertDialog(
        "Đơn vị bản vẽ không phải Millimeter.\n" +
        $"Đơn vị hiện tại: {insunits}\n\n" +
        "Vui lòng gõ lệnh UNITS trong AutoCAD\n" +
        "và đổi về Millimeters trước khi scan.");
    return false;
}
return true;
// Gọi hàm này trước MỌI scan operation
```
