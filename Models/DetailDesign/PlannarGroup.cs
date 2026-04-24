using System.Collections.Generic;

namespace MCGCadPlugin.Models.DetailDesign
{
    /// <summary>
    /// Nhóm mặt phẳng chứa các construction lines collinear.
    /// Mặt phẳng ⊥ xOy: T (y=b), L (x=a), A (xiên).
    /// </summary>
    public class PlannarGroup
    {
        public string GroupType { get; set; }   // T, L, A, ST, SL, BS
        public int Index { get; set; }          // 1, 2, 3...
        public string Label => $"{GroupType}-{Index}";
        public double Position { get; set; }    // Y for T/ST, X for L/SL, angle for A
        public double AngleDeg { get; set; }    // 0=LONG, 90=TRANS, else arbitrary
        public List<string> MemberGuids { get; set; } = new List<string>();
        public int MemberCount => MemberGuids.Count;
        public string ThrowText { get; set; }   // "(0,-1)" display
    }
}
