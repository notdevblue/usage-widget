using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UsageMonitor.Services;

namespace UsageMonitor.Layout;

public sealed partial class LayoutEditorWindow
{
    private void RebuildFrames()
    {
        overlay.Children.Clear();
        frames.Clear();
        foreach (var block in blocks)
        {
            var id = block.Id;
            var inner = new Grid();
            if (block.Kind is LayoutBlockKind.Usage or LayoutBlockKind.Reset &&
                !UsageLayoutCanvas.TryGetWindow(context, block, out _))
            {
                var missing = new TextBlock
                {
                    Text = $"{ProviderName(block.ProviderId)} · {WindowName(block)} 한도\n현재 데이터 없음",
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, FontSize = 10,
                    Margin = new Thickness(5)
                };
                missing.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
                inner.SetResourceReference(Panel.BackgroundProperty, "HoverBrush");
                inner.Children.Add(missing);
            }

            var drag = new Thumb { Cursor = Cursors.SizeAll, Template = ThumbTemplate(false), Focusable = false };
            drag.PreviewMouseLeftButtonDown += (_, _) =>
            {
                SelectBlock(id);
                Keyboard.Focus(previewHost);
            };
            drag.DragStarted += (_, _) => BeginGesture(id);
            drag.DragDelta += (_, e) =>
            {
                gestureX += e.HorizontalChange;
                gestureY += e.VerticalChange;
                UpdateSelected(value => value with
                {
                    X = Math.Round(Math.Clamp(gestureBlock.X + gestureX, 0, profile.Width - value.Width)),
                    Y = Math.Round(Math.Clamp(gestureBlock.Y + gestureY, 0, profile.Height - value.Height))
                });
            };
            inner.Children.Add(drag);
            var resize = new Thumb
            {
                Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 2, 2),
                Cursor = Cursors.SizeNWSE, Template = ThumbTemplate(true), Focusable = false,
                ToolTip = "끌어서 크기 조절"
            };
            resize.DragStarted += (_, _) => BeginGesture(id);
            resize.DragDelta += (_, e) =>
            {
                gestureX += e.HorizontalChange;
                gestureY += e.VerticalChange;
                UpdateSelected(value => value with
                {
                    Width = Math.Round(Math.Clamp(gestureBlock.Width + gestureX, 60, profile.Width - value.X)),
                    Height = Math.Round(Math.Clamp(gestureBlock.Height + gestureY, 32, profile.Height - value.Y))
                });
            };
            inner.Children.Add(resize);
            var border = new Border
            {
                Child = inner, Background = Brushes.Transparent,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4)
            };
            frames.Add(id, new(border, resize));
            overlay.Children.Add(border);
        }
    }

    private void BeginGesture(string id)
    {
        SelectBlock(id);
        TryGetSelectedBlock(out gestureBlock);
        gestureX = 0;
        gestureY = 0;
        Keyboard.Focus(previewHost);
    }

    private static ControlTemplate ThumbTemplate(bool resize)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(resize ? 3 : 0));
        if (resize)
            border.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = border };
    }
}
