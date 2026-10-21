using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using UsageMonitor.Core;

namespace UsageMonitor.Presentation.Tests;

internal static class WidgetDragInputTests
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        ConsumedBubblingDoesNotReachAncestor();
        WidgetContentBeginsDragOnPreview();
        OtherMouseButtonsKeepTheirRoute();
        InteractiveControlsKeepTheirInput();
        TextContentFollowsItsParent();
        Console.WriteLine($"PASS {assertions} widget drag input assertions");
    }

    private static void ConsumedBubblingDoesNotReachAncestor()
    {
        var view = CreateWidget();
        var ancestorCalls = 0;
        view.MouseLeftButtonDown += (_, _) => ancestorCalls++;
        foreach (var source in WidgetSources(view))
        {
            var childCalls = 0;
            source.MouseLeftButtonDown += (_, args) =>
            {
                childCalls++;
                args.Handled = true;
            };
            var input = MouseInput(MouseButton.Left);
            RaiseMouseDown(source, input);
            Check(childCalls == 1, "A child consuming the bubbling mouse event must receive the click.");
            Check(input.Handled, "The child must mark the bubbling event handled.");
        }

        Check(ancestorCalls == 0, "A normal ancestor bubbling handler cannot start dragging after a child consumes the event.");
    }

    private static void WidgetContentBeginsDragOnPreview()
    {
        var view = CreateWidget();
        var dragCalls = 0;
        var childCalls = 0;
        var input = MouseInput(MouseButton.Left);
        WidgetDragInput.Attach(view, () =>
        {
            Check(input.Handled, "The input must be handled before the window drag begins.");
            dragCalls++;
        });
        foreach (var source in WidgetSources(view))
        {
            source.MouseLeftButtonDown += (_, args) =>
            {
                childCalls++;
                args.Handled = true;
            };
            input = MouseInput(MouseButton.Left);
            var previousDragCalls = dragCalls;
            RaiseMouseDown(source, input);
            Check(dragCalls == previousDragCalls + 1, "Each widget text or progress-bar click must request exactly one drag through its ScrollViewer.");
            Check(input.Handled, "A drag click must stay handled through the bubbling route.");
        }

        Check(childCalls == 0, "Preview dragging must run before a child's consuming bubbling handler.");
    }

    private static void OtherMouseButtonsKeepTheirRoute()
    {
        var surface = new Grid();
        var source = new TextBlock { Text = "widget" };
        surface.Children.Add(source);
        var dragCalls = 0;
        WidgetDragInput.Attach(surface, () => dragCalls++);
        foreach (var button in new[] { MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2 })
        {
            var input = MouseInput(button);
            RaiseMouseDown(source, input);
            Check(!input.Handled, "Non-left mouse input must remain available for its existing actions.");
        }

        Check(dragCalls == 0, "Non-left mouse buttons must not request widget dragging.");
    }

    private static void InteractiveControlsKeepTheirInput()
    {
        var buttonLabel = new TextBlock { Text = "settings" };
        CheckInteractive(new Button { Content = buttonLabel }, buttonLabel);
        var toggleLabel = new TextBlock { Text = "pin" };
        CheckInteractive(new ToggleButton { Content = toggleLabel }, toggleLabel);
        var scrollBar = new ScrollBar
        {
            Template = new ControlTemplate(typeof(ScrollBar))
            {
                VisualTree = new FrameworkElementFactory(typeof(Border))
            }
        };
        CheckInteractive(scrollBar, scrollBar);
        CheckInteractive(scrollBar, Descendants<Border>(scrollBar).Single());
        var thumb = new Thumb
        {
            Template = new ControlTemplate(typeof(Thumb))
            {
                VisualTree = new FrameworkElementFactory(typeof(Border))
            }
        };
        CheckInteractive(thumb, thumb);
        CheckInteractive(thumb, Descendants<Border>(thumb).Single());
    }

    private static void TextContentFollowsItsParent()
    {
        var run = new Run("weekly usage");
        var text = new TextBlock(run);
        var surface = new Grid();
        surface.Children.Add(text);
        Arrange(surface);
        var dragCalls = 0;
        WidgetDragInput.Attach(surface, () => dragCalls++);
        var input = MouseInput(MouseButton.Left);
        run.RaiseEvent(input);
        Check(ReferenceEquals(input.OriginalSource, run), "The text-content route must retain its Run original source.");
        Check(dragCalls == 1 && input.Handled, "A Run in ordinary widget text must request dragging without a visual-parent error.");

        var buttonRun = new Run("settings");
        var buttonText = new TextBlock(buttonRun);
        var button = new Button { Content = buttonText };
        var interactiveSurface = new Grid();
        interactiveSurface.Children.Add(button);
        Arrange(interactiveSurface);
        var buttonDragCalls = 0;
        WidgetDragInput.Attach(interactiveSurface, () => buttonDragCalls++);
        var buttonInput = MouseInput(MouseButton.Left);
        buttonRun.RaiseEvent(buttonInput);
        Check(ReferenceEquals(buttonInput.OriginalSource, buttonRun), "The button-content route must retain its Run original source.");
        Check(buttonDragCalls == 0 && !buttonInput.Handled, "A Run inside a button must preserve the button's mouse input.");
    }

    private static void CheckInteractive(FrameworkElement control, UIElement source)
    {
        var surface = new Grid();
        surface.Children.Add(control);
        Arrange(surface);
        var dragCalls = 0;
        var previewCalls = 0;
        WidgetDragInput.Attach(surface, () => dragCalls++);
        source.PreviewMouseLeftButtonDown += (_, _) => previewCalls++;
        var input = MouseInput(MouseButton.Left);
        source.RaiseEvent(input);
        Check(dragCalls == 0, "A button, scrollbar or thumb descendant must not request widget dragging.");
        Check(!input.Handled && previewCalls == 1, "Interactive controls must receive their unconsumed preview mouse input.");
        surface.Children.Remove(control);
    }

    private static WidgetView CreateWidget()
    {
        var provider = new WidgetViewModel();
        provider.Metrics.Add(new UsageMetric(new UsageWindow
        {
            Id = "weekly", Label = "주간", WindowMinutes = 10080, UsedPercent = 37,
            ResetsAt = DateTimeOffset.UtcNow.AddDays(2)
        }, DateTimeOffset.UtcNow, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick));
        provider.Update();
        var model = new WidgetViewModel();
        model.Providers.Add(provider);
        var view = new WidgetView { DataContext = model };
        Arrange(view);
        Check(Descendants<ScrollViewer>(view).Any(), "The widget fixture must contain the production ScrollViewer.");
        return view;
    }

    private static UIElement[] WidgetSources(WidgetView view)
    {
        var progress = Descendants<ProgressBar>(view).Single();
        return
        [
            Descendants<TextBlock>(view).Single(text => text.Text == "주간"),
            progress,
            Descendants<Border>(progress).First()
        ];
    }

    private static MouseButtonEventArgs MouseInput(MouseButton button) => new(Mouse.PrimaryDevice, Environment.TickCount, button)
    {
        RoutedEvent = Mouse.PreviewMouseDownEvent
    };

    private static void RaiseMouseDown(UIElement source, MouseButtonEventArgs input)
    {
        source.RaiseEvent(input);
        input.RoutedEvent = Mouse.MouseDownEvent;
        source.RaiseEvent(input);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, index)))
                yield return child;
        }
    }

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(190, 100));
        element.Arrange(new Rect(0, 0, 190, 100));
        element.UpdateLayout();
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
