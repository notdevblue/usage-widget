using System.Windows.Controls;
using UsageMonitor.Layout;

namespace UsageMonitor;

public partial class WidgetView : UserControl
{
    public WidgetView() => InitializeComponent();

    public UsageLayoutCanvas LayoutCanvas => CustomLayoutCanvas;
}
