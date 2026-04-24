using System.Diagnostics;
using System.Windows.Controls;

namespace MCGCadPlugin.Views.TableOfContent
{
    /// <summary>View placeholder cho module Table of Content.</summary>
    public partial class TableOfContentView : UserControl
    {
        private const string LOG_PREFIX = "[TableOfContentView]";

        public TableOfContentView()
        {
            InitializeComponent();
            Debug.WriteLine($"{LOG_PREFIX} View initialized (empty placeholder).");
        }
    }
}
