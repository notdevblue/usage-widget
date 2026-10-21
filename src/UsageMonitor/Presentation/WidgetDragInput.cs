using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace UsageMonitor.Presentation;

public static class WidgetDragInput
{
    public static void Attach(UIElement surface, Action beginDrag)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(beginDrag);
        surface.PreviewMouseLeftButtonDown += (sender, args) =>
        {
            if (args.ChangedButton != MouseButton.Left || IsInteractive(args.OriginalSource))
                return;
            args.Handled = true;
            beginDrag();
        };
    }

    private static bool IsInteractive(object source)
    {
        if (source is not DependencyObject current)
            return false;
        while (true)
        {
            if (current is ButtonBase or ScrollBar or Thumb)
                return true;
            if (current is ContentElement content)
            {
                if (ContentOperations.GetParent(content) is DependencyObject contentParent)
                    current = contentParent;
                else if (content is FrameworkContentElement { Parent: DependencyObject frameworkParent })
                    current = frameworkParent;
                else
                    return false;
            }
            else
            {
                if (VisualTreeHelper.GetParent(current) is not DependencyObject visualParent)
                    return false;
                current = visualParent;
            }
        }
    }
}
