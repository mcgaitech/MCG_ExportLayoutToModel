using System.Diagnostics;
using System.Windows.Controls;

namespace MCGCadPlugin.Views.Weight
{
    /// <summary>View placeholder cho module Weight.</summary>
    public partial class WeightView : UserControl
    {
        private const string LOG_PREFIX = "[WeightView]";

        public WeightView()
        {
            InitializeComponent();
            Debug.WriteLine($"{LOG_PREFIX} View initialized (empty placeholder).");
        }
    }
}
