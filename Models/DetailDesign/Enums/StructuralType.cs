namespace MCGCadPlugin.Models.DetailDesign.Enums
{
    /// <summary>
    /// Loại kết cấu của entity sau khi phân loại.
    /// Gán bởi PrimaryClassifier (layer/color/ratio) và TopologyEngine (AM0).
    /// </summary>
    public enum StructuralType
    {
        /// <summary>Vùng top plate (layer "0")</summary>
        TopPlateRegion,

        /// <summary>Web plate (layer AM_0, sau topology)</summary>
        WebPlate,

        /// <summary>Flange (layer AM_5)</summary>
        Flange,

        /// <summary>Stiffener (layer AM_3, color 40, ratio > 5)</summary>
        Stiffener,

        /// <summary>Buckling stiffener (layer AM_3, color 6)</summary>
        BucklingStiffener,

        /// <summary>Doubling plate (layer AM_3, color 40, ratio ≤ 3)</summary>
        DoublingPlate,

        /// <summary>Bracket (AM_0, touches stiff AND web — topology)</summary>
        Bracket,

        /// <summary>Closing box web (AM_0, share edge tại góc — topology)</summary>
        ClosingBoxWeb,

        /// <summary>Girder End — WebPlate có SE→web, LE→web, SE→stiff/BS (topology)</summary>
        GirderEnd,

        /// <summary>AM_0 chưa phân loại — chờ TopologyEngine</summary>
        AM0_Unclassified,

        /// <summary>Không rõ loại — ratio 3.0–5.0, cần user resolve</summary>
        Ambiguous
    }
}
