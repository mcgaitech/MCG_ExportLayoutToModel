using System.Diagnostics;
using System.Windows.Controls;

namespace MCGCadPlugin.Views.FittingManagement
{
    /// <summary>
    /// View placeholder cho module Fitting Management — tab empty.
    /// </summary>
    public partial class FittingManagementView : UserControl
    {
        private const string LOG_PREFIX = "[FittingManagementView]";

        public FittingManagementView()
        {
            InitializeComponent();
            Debug.WriteLine($"{LOG_PREFIX} View initialized (empty placeholder).");
        }
    }
}
