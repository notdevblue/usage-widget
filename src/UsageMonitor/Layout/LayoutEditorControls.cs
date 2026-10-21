using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UsageMonitor.Services;

namespace UsageMonitor.Layout;

public sealed partial class LayoutEditorWindow
{
    private Grid BuildWindow()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 16) };
        root.SetResourceReference(Panel.BackgroundProperty, "CardBrush");
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(BuildHeader());
        var body = new Grid { Margin = new Thickness(0, 18, 0, 14) };
        body.ColumnDefinitions.Add(new() { Width = new GridLength(160) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(220) });
        body.Children.Add(BuildBlockList());
        var workArea = BuildWorkArea();
        Grid.SetColumn(workArea, 1);
        body.Children.Add(workArea);
        var properties = BuildProperties();
        Grid.SetColumn(properties, 2);
        body.Children.Add(properties);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private Grid BuildHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(Label("나만의 작은 위젯", 23, false));
        var subtitle = Label("필요한 블록만 놓고, 위치와 크기를 직접 맞춰보세요.", 11);
        subtitle.Margin = new Thickness(0, 5, 0, 0);
        title.Children.Add(subtitle);
        grid.Children.Add(title);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var caption = Label("프리셋", 11);
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.Margin = new Thickness(0, 0, 9, 0);
        presets.Children.Add(caption);
        var choices = new ComboBox { Width = 128 };
        choices.Items.Add(new ComboBoxItem { Content = "프리셋 선택", IsEnabled = false });
        foreach (var name in new[] { "카드", "미니", "링", "가로형" })
            choices.Items.Add(new ComboBoxItem { Content = name });
        choices.SelectedIndex = 0;
        AutomationProperties.SetName(choices, "레이아웃 프리셋");
        choices.SelectionChanged += (_, _) =>
        {
            if (choices.SelectedIndex > 0)
                ApplyProfile(LayoutPresetFactory.Create(choices.SelectedIndex - 1, context));
        };
        presets.Children.Add(choices);
        Grid.SetColumn(presets, 1);
        grid.Children.Add(presets);
        return grid;
    }

    private Grid BuildBlockList()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 14, 0) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var add = new StackPanel();
        var heading = Label("블록 추가", 12, false);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, 8);
        add.Children.Add(heading);
        var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(-2, 0, -2, 12) };
        var names = new[] { "+ 사용량", "+ 초기화", "+ 계정", "+ 상태" };
        for (var index = 0; index < names.Length; index++)
        {
            var kind = (LayoutBlockKind)index;
            var button = ActionButton(names[index], () => AddBlock(kind));
            button.Margin = new Thickness(2);
            button.Padding = new Thickness(5, 8, 5, 8);
            buttons.Children.Add(button);
        }

        add.Children.Add(buttons);
        add.Children.Add(Label("배치한 블록", 11));
        grid.Children.Add(add);
        blockList.Margin = new Thickness(0, 7, 0, 8);
        blockList.BorderThickness = new Thickness(0);
        blockList.SetResourceReference(Control.BackgroundProperty, "CardBrush");
        blockList.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && blockList.SelectedItem is ListBoxItem { Tag: string id })
                SelectBlock(id);
        };
        AutomationProperties.SetName(blockList, "배치한 블록 목록");
        Grid.SetRow(blockList, 1);
        grid.Children.Add(blockList);
        var actions = new StackPanel();
        var order = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 7) };
        backwardButton.Content = "뒤로";
        backwardButton.ToolTip = "겹친 블록보다 한 단계 뒤로 보내기";
        backwardButton.Margin = new Thickness(0, 0, 3, 0);
        backwardButton.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        backwardButton.Click += (_, _) => ChangeOrder(-1);
        forwardButton.Content = "앞으로";
        forwardButton.ToolTip = "겹친 블록보다 한 단계 앞으로 가져오기";
        forwardButton.Margin = new Thickness(3, 0, 0, 0);
        forwardButton.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        forwardButton.Click += (_, _) => ChangeOrder(1);
        order.Children.Add(backwardButton);
        order.Children.Add(forwardButton);
        actions.Children.Add(order);
        removeButton.Content = "선택 블록 삭제";
        removeButton.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        removeButton.Click += (_, _) => RemoveSelected();
        actions.Children.Add(removeButton);
        Grid.SetRow(actions, 2);
        grid.Children.Add(actions);
        return grid;
    }

    private Grid BuildWorkArea()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 18, 0) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var caption = Label("실제 크기 미리보기", 11);
        caption.Margin = new Thickness(0, 0, 0, 9);
        grid.Children.Add(caption);
        previewHost.Children.Add(preview);
        previewHost.Children.Add(overlay);
        previewHost.SetResourceReference(Panel.BackgroundProperty, "CardBrush");
        var frame = new Border
        {
            Child = previewHost, BorderThickness = new Thickness(1),
            Margin = new Thickness(22), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var scroll = new ScrollViewer
        {
            Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };
        scroll.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        var workSurface = new Border { Child = scroll, CornerRadius = new CornerRadius(12), ClipToBounds = true };
        workSurface.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        Grid.SetRow(workSurface, 1);
        grid.Children.Add(workSurface);
        return grid;
    }

    private ScrollViewer BuildProperties()
    {
        var stack = new StackPanel();
        var canvasTitle = Label("캔버스", 12, false);
        canvasTitle.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(canvasTitle);
        var sizeRow = new Grid { Margin = new Thickness(0, 10, 0, 11) };
        sizeRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        sizeRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        sizeRow.Children.Add(NumericField("너비", canvasWidthInput));
        var canvasHeight = NumericField("높이", canvasHeightInput);
        canvasHeight.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(canvasHeight, 1);
        sizeRow.Children.Add(canvasHeight);
        AutomationProperties.SetName(canvasWidthInput, "캔버스 너비");
        AutomationProperties.SetName(canvasHeightInput, "캔버스 높이");
        stack.Children.Add(sizeRow);
        autoHeightInput.Content = "보이는 블록에 높이 맞추기";
        autoHeightInput.Margin = new Thickness(0, 0, 0, 7);
        autoHeightInput.Checked += (_, _) => { if (!synchronizing) CommitInputs(); };
        autoHeightInput.Unchecked += (_, _) => { if (!synchronizing) CommitInputs(); };
        stack.Children.Add(autoHeightInput);
        var explanation = Label("없는 한도는 숨기고, 아래 빈 공간은 줄여요.", 10);
        explanation.TextWrapping = TextWrapping.Wrap;
        explanation.Margin = new Thickness(0, 0, 0, 17);
        stack.Children.Add(explanation);
        var separator = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 17) };
        separator.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        stack.Children.Add(separator);
        selectionTitle.FontSize = 12;
        selectionTitle.FontWeight = FontWeights.SemiBold;
        selectionTitle.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(selectionTitle);
        var fields = new StackPanel();
        fields.Children.Add(ChoiceField("블록", kindInput, ["사용량", "초기화", "계정", "상태"]));
        fields.Children.Add(ChoiceField("제공자", providerInput, ["Codex", "Claude"]));
        fields.Children.Add(ChoiceField("한도 선택", sourceInput, []));
        fields.Children.Add(ChoiceField("표현", visualizationInput, ["막대", "링", "숫자"]));
        var geometry = new Grid { Margin = new Thickness(0, 3, 0, 12) };
        geometry.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        geometry.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        geometry.RowDefinitions.Add(new() { Height = GridLength.Auto });
        geometry.RowDefinitions.Add(new() { Height = GridLength.Auto });
        AddGeometryField(geometry, "X", xInput, 0, 0);
        AddGeometryField(geometry, "Y", yInput, 0, 1);
        AddGeometryField(geometry, "너비", widthInput, 1, 0);
        AddGeometryField(geometry, "높이", heightInput, 1, 1);
        fields.Children.Add(geometry);
        labelInput.Content = "한도 이름 표시";
        labelInput.Margin = new Thickness(0, 0, 0, 10);
        resetInput.Content = "초기화 시간 표시";
        fields.Children.Add(labelInput);
        fields.Children.Add(resetInput);
        propertyPanel.Children.Add(fields);
        stack.Children.Add(propertyPanel);
        kindInput.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && kindInput.SelectedIndex >= 0)
                UpdateSelected(block => block with { Kind = (LayoutBlockKind)kindInput.SelectedIndex }, true);
        };
        providerInput.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && providerInput.SelectedIndex >= 0)
                UpdateSelected(block => block with { ProviderId = providerInput.SelectedIndex == 1 ? "claude" : "codex", WindowId = "" }, true);
        };
        sourceInput.SelectionChanged += (_, _) =>
        {
            if (synchronizing || sourceInput.SelectedIndex < 0)
                return;
            if (sourceInput.SelectedIndex < 4)
                UpdateSelected(block => block with { Source = (UsageWindowSource)sourceInput.SelectedIndex, WindowId = "" }, true);
            else if (sourceInput.SelectedItem is ComboBoxItem { Tag: string windowId })
                UpdateSelected(block => block with { WindowId = windowId }, true);
        };
        visualizationInput.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && visualizationInput.SelectedIndex >= 0)
                UpdateSelected(block => block with { Visualization = (UsageVisualization)visualizationInput.SelectedIndex });
        };
        labelInput.Checked += ChangeDisplayOptions;
        labelInput.Unchecked += ChangeDisplayOptions;
        resetInput.Checked += ChangeDisplayOptions;
        resetInput.Unchecked += ChangeDisplayOptions;
        return new ScrollViewer
        {
            Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private Grid BuildFooter()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        hint.FontSize = 10;
        hint.TextWrapping = TextWrapping.Wrap;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(0, 0, 12, 0);
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        grid.Children.Add(hint);
        var cancel = ActionButton("취소", () => DialogResult = false);
        cancel.IsCancel = true;
        cancel.MinWidth = 75;
        Grid.SetColumn(cancel, 1);
        grid.Children.Add(cancel);
        var save = ActionButton("위젯에 적용", Save);
        save.MinWidth = 112;
        save.Margin = new Thickness(8, 0, 0, 0);
        save.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        save.SetResourceReference(Control.ForegroundProperty, "ActionTextBrush");
        Grid.SetColumn(save, 2);
        grid.Children.Add(save);
        return grid;
    }

    private void ChangeDisplayOptions(object sender, RoutedEventArgs e)
    {
        if (!synchronizing)
            UpdateSelected(block => block with { ShowLabel = labelInput.IsChecked == true, ShowReset = resetInput.IsChecked == true });
    }

    private StackPanel NumericField(string title, TextBox input)
    {
        var stack = new StackPanel();
        var label = Label(title, 10);
        label.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(label);
        input.Padding = new Thickness(7, 5, 7, 5);
        input.BorderThickness = new Thickness(1);
        input.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        input.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        input.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
        input.LostKeyboardFocus += (_, _) => CommitInputs();
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            CommitInputs();
            e.Handled = true;
        };
        AutomationProperties.SetName(input, title);
        stack.Children.Add(input);
        return stack;
    }

    private void AddGeometryField(Grid parent, string title, TextBox input, int row, int column)
    {
        var field = NumericField(title, input);
        field.Margin = new Thickness(column == 1 ? 9 : 0, row == 1 ? 9 : 0, 0, 0);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, column);
        parent.Children.Add(field);
    }

    private static StackPanel ChoiceField(string title, ComboBox input, string[] choices)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var label = Label(title, 10);
        label.Margin = new Thickness(0, 0, 0, 5);
        stack.Children.Add(label);
        foreach (var choice in choices)
            input.Items.Add(new ComboBoxItem { Content = choice });
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(input, title);
        stack.Children.Add(input);
        return stack;
    }

    private static TextBlock Label(string text, double size, bool secondary = true)
    {
        var label = new TextBlock { Text = text, FontSize = size };
        label.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "SecondaryBrush" : "TextBrush");
        return label;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text };
        button.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        button.Click += (_, _) => action();
        return button;
    }
}
