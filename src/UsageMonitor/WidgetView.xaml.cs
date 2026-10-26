using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UsageMonitor.Layout;
using UsageMonitor.Presentation;

namespace UsageMonitor;

public partial class WidgetView : UserControl
{
    private readonly Storyboard _resetReveal = new();

    public static readonly DependencyProperty ResetNoticeVisibleProperty = DependencyProperty.Register(
        nameof(ResetNoticeVisible), typeof(bool), typeof(WidgetView), new PropertyMetadata(false, ResetNoticeVisibilityChanged));

    public bool ResetNoticeVisible
    {
        get => (bool)GetValue(ResetNoticeVisibleProperty);
        set => SetValue(ResetNoticeVisibleProperty, value);
    }

    public event Action<string> ResetNoticeRequested = delegate { };

    public WidgetView()
    {
        InitializeComponent();
        SetBinding(ResetNoticeVisibleProperty, new Binding(nameof(WidgetViewModel.ResetNoticeVisible)));
    }

    public UsageLayoutCanvas LayoutCanvas => CustomLayoutCanvas;

    private static void ResetNoticeVisibilityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((WidgetView)sender).ShowResetNotice((bool)args.NewValue);

    private void ShowResetNotice(bool visible)
    {
        _resetReveal.Remove(this);
        _resetReveal.Children.Clear();
        if (visible == false)
        {
            ResetNoticeRow.Height = 0;
            ResetNoticeRow.Opacity = 0;
            ResetNoticeRow.Visibility = Visibility.Collapsed;
            ResetNoticeTranslation.Y = -4;
            return;
        }

        ResetNoticeRow.Height = 20;
        ResetNoticeRow.Opacity = 1;
        ResetNoticeTranslation.Y = 0;
        ResetNoticeRow.Visibility = Visibility.Visible;
        if (SystemParameters.ClientAreaAnimation == false)
            return;
        AddResetReveal(ResetNoticeRow, FrameworkElement.HeightProperty, 0, 20);
        AddResetReveal(ResetNoticeRow, OpacityProperty, 0, 1);
        AddResetReveal(ResetNoticeTranslation, TranslateTransform.YProperty, -4, 0);
        _resetReveal.Begin(this, true);
    }

    private void AddResetReveal(DependencyObject target, DependencyProperty property, double from, double to)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        _resetReveal.Children.Add(animation);
    }

    private void ResetNotice_Click(object sender, RoutedEventArgs args)
    {
        if (DataContext is WidgetViewModel model && model.ResetNoticeVisible && model.ResetNoticeSourceUrl.Length > 0)
            ResetNoticeRequested(model.ResetNoticeSourceUrl);
    }
}
