using System.Diagnostics;
using System.Windows.Controls;

namespace MCGCadPlugin.Views.PanelData
{
    /// <summary>View placeholder cho module Panel Data.</summary>
    public partial class PanelDataView : UserControl
    {
        private const string LOG_PREFIX = "[PanelDataView]";

        public PanelDataView()
        {
            InitializeComponent();
            Debug.WriteLine($"{LOG_PREFIX} View initialized (empty placeholder).");
        }
    }
}
