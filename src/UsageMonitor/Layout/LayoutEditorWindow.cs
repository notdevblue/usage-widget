using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UsageMonitor.Services;

namespace UsageMonitor.Layout;

public sealed partial class LayoutEditorWindow : Window
{
    private readonly LayoutContext context;
    private readonly List<LayoutBlock> blocks = [];
    private readonly Dictionary<string, BlockFrame> frames = new(StringComparer.Ordinal);
    private readonly UsageLayoutCanvas preview = new();
    private readonly Canvas overlay = new();
    private readonly Grid previewHost = new() { Focusable = true };
    private readonly ListBox blockList = new();
    private readonly Grid propertyPanel = new();
    private readonly ComboBox kindInput = new();
    private readonly ComboBox providerInput = new();
    private readonly ComboBox sourceInput = new();
    private readonly ComboBox visualizationInput = new();
    private readonly TextBox xInput = new();
    private readonly TextBox yInput = new();
    private readonly TextBox widthInput = new();
    private readonly TextBox heightInput = new();
    private readonly TextBox canvasWidthInput = new();
    private readonly TextBox canvasHeightInput = new();
    private readonly CheckBox autoHeightInput = new();
    private readonly CheckBox labelInput = new();
    private readonly CheckBox resetInput = new();
    private readonly TextBlock selectionTitle = new();
    private readonly TextBlock hint = new();
    private readonly Button removeButton = new();
    private readonly Button forwardButton = new();
    private readonly Button backwardButton = new();
    private LayoutProfile profile = new();
    private LayoutBlock gestureBlock = new();
    private string selectedId = "";
    private double gestureX;
    private double gestureY;
    private bool synchronizing;

    public LayoutProfile Result { get; private set; } = new();

    internal void RefreshTheme() => RenderPreview();

    public LayoutEditorWindow(LayoutProfile current, LayoutContext context)
    {
        this.context = context;
        Title = "위젯 레이아웃 편집";
        Width = Math.Min(1080, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(900, Width);
        MinHeight = Math.Min(580, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        SetResourceReference(BackgroundProperty, "CardBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        Content = BuildWindow();
        ApplyProfile(current.Blocks.Count > 0 ? current with { Enabled = true } : LayoutPresetFactory.Create(0, context));
        PreviewKeyDown += HandleKeyDown;
    }

    private void ApplyProfile(LayoutProfile value)
    {
        profile = LayoutProfile.Normalize(value);
        blocks.Clear();
        blocks.AddRange(profile.Blocks);
        selectedId = blocks.Count > 0 ? blocks[0].Id : "";
        RefreshAll();
    }

    private void RefreshAll()
    {
        synchronizing = true;
        canvasWidthInput.Text = Number(profile.Width);
        canvasHeightInput.Text = Number(profile.Height);
        autoHeightInput.IsChecked = profile.AutoHeight;
        blockList.Items.Clear();
        foreach (var block in blocks)
        {
            var item = new ListBoxItem
            {
                Content = Describe(block), Tag = block.Id,
                Padding = new Thickness(8, 9, 8, 9),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            item.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            blockList.Items.Add(item);
            if (block.Id == selectedId)
                blockList.SelectedItem = item;
        }

        synchronizing = false;
        RebuildFrames();
        RenderPreview();
        RefreshSelection();
    }

    private void RenderPreview()
    {
        preview.Render(profile with { Enabled = true, AutoHeight = false, Blocks = blocks.ToArray() }, context);
        overlay.Width = profile.Width;
        overlay.Height = profile.Height;
        previewHost.Width = profile.Width;
        previewHost.Height = profile.Height;
        foreach (var block in blocks)
        {
            if (!frames.TryGetValue(block.Id, out var frame))
                continue;
            Canvas.SetLeft(frame.Border, block.X);
            Canvas.SetTop(frame.Border, block.Y);
            frame.Border.Width = block.Width;
            frame.Border.Height = block.Height;
        }
    }

    private void SelectBlock(string id)
    {
        if (id != selectedId && Keyboard.FocusedElement is TextBox)
            CommitInputs();
        selectedId = id;
        synchronizing = true;
        foreach (ListBoxItem item in blockList.Items)
        {
            if (item.Tag is string itemId && itemId == id)
            {
                blockList.SelectedItem = item;
                break;
            }
        }

        synchronizing = false;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        synchronizing = true;
        var selected = TryGetSelectedBlock(out var block);
        propertyPanel.IsEnabled = selected;
        removeButton.IsEnabled = selected;
        var selectedIndex = blocks.FindIndex(candidate => candidate.Id == selectedId);
        forwardButton.IsEnabled = selected && selectedIndex < blocks.Count - 1;
        backwardButton.IsEnabled = selected && selectedIndex > 0;
        selectionTitle.Text = selected ? Describe(block) : "블록을 선택해 주세요";
        xInput.Text = Number(block.X);
        yInput.Text = Number(block.Y);
        widthInput.Text = Number(block.Width);
        heightInput.Text = Number(block.Height);
        kindInput.SelectedIndex = (int)block.Kind;
        providerInput.SelectedIndex = block.ProviderId switch { "claude" => 1, "grok" => 2, _ => 0 };
        RefreshSources(block);
        visualizationInput.SelectedIndex = (int)block.Visualization;
        labelInput.IsChecked = block.ShowLabel;
        resetInput.IsChecked = block.ShowReset;
        sourceInput.IsEnabled = block.Kind is LayoutBlockKind.Usage or LayoutBlockKind.Reset;
        visualizationInput.IsEnabled = block.Kind == LayoutBlockKind.Usage;
        labelInput.IsEnabled = block.Kind is LayoutBlockKind.Usage or LayoutBlockKind.Reset;
        resetInput.IsEnabled = block.Kind == LayoutBlockKind.Usage;
        foreach (var pair in frames)
        {
            var active = pair.Key == selectedId;
            pair.Value.Border.SetResourceReference(Border.BorderBrushProperty, active ? "AccentBrush" : "LineBrush");
            pair.Value.Border.BorderThickness = new Thickness(active ? 1.5 : 1);
            pair.Value.Resize.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            Panel.SetZIndex(pair.Value.Border, active ? 100 : 0);
        }

        hint.Text = selected && (block.Kind is LayoutBlockKind.Usage or LayoutBlockKind.Reset) &&
                    !UsageLayoutCanvas.TryGetWindow(context, block, out _)
            ? "이 한도는 현재 제공되지 않아 실제 위젯에서 숨겨져요."
            : "블록을 끌어서 이동 · 오른쪽 아래 점으로 크기 조절";
        synchronizing = false;
    }

    private void RefreshSources(LayoutBlock block)
    {
        sourceInput.Items.Clear();
        foreach (var label in new[] { "첫 번째 한도", "두 번째 한도", "5시간 한도", "주간 한도" })
            sourceInput.Items.Add(new ComboBoxItem { Content = label });
        var selectedIndex = block.WindowId.Length == 0 ? (int)block.Source : -1;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in context.ResolveProvider(block.ProviderId).Windows)
        {
            if (window.Id.Length == 0 || !ids.Add(window.Id))
                continue;
            var item = new ComboBoxItem { Content = $"직접 선택 · {window.Label}", Tag = window.Id, ToolTip = window.Label };
            var index = sourceInput.Items.Add(item);
            if (window.Id == block.WindowId)
                selectedIndex = index;
        }

        if (selectedIndex < 0)
            selectedIndex = sourceInput.Items.Add(new ComboBoxItem { Content = "선택한 한도 · 현재 데이터 없음", Tag = block.WindowId });
        sourceInput.SelectedIndex = selectedIndex;
    }

    private void UpdateSelected(Func<LayoutBlock, LayoutBlock> update, bool rebuild = false)
    {
        var index = blocks.FindIndex(block => block.Id == selectedId);
        if (index < 0)
            return;
        blocks[index] = update(blocks[index]);
        NormalizeGeometry();
        if (rebuild)
            RefreshAll();
        else
        {
            RenderPreview();
            RefreshSelection();
        }
    }

    private void NormalizeGeometry()
    {
        profile = LayoutProfile.Normalize(profile with { Enabled = true, Blocks = blocks.ToArray() });
        blocks.Clear();
        blocks.AddRange(profile.Blocks);
    }

    private bool TryGetSelectedBlock(out LayoutBlock block)
    {
        block = new();
        foreach (var candidate in blocks)
        {
            if (candidate.Id != selectedId)
                continue;
            block = candidate;
            return true;
        }

        return false;
    }

    private void AddBlock(LayoutBlockKind kind)
    {
        if (blocks.Count >= 32)
        {
            hint.Text = "블록은 최대 32개까지 추가할 수 있어요.";
            return;
        }

        var height = kind switch { LayoutBlockKind.Usage => 112d, LayoutBlockKind.Account => 48d, _ => 38d };
        var y = blocks.Count == 0 ? 0 : blocks.Max(block => block.Y + block.Height) + 12;
        profile = profile with { Height = Math.Clamp(Math.Max(profile.Height, y + height), 80, 1000) };
        var added = new LayoutBlock
        {
            Kind = kind, Width = profile.Width, Height = height,
            Y = Math.Min(y, profile.Height - height)
        };
        blocks.Add(added);
        selectedId = added.Id;
        NormalizeGeometry();
        RefreshAll();
    }

    private void RemoveSelected()
    {
        var index = blocks.FindIndex(block => block.Id == selectedId);
        if (index < 0)
            return;
        blocks.RemoveAt(index);
        selectedId = blocks.Count == 0 ? "" : blocks[Math.Min(index, blocks.Count - 1)].Id;
        NormalizeGeometry();
        RefreshAll();
    }

    private void ChangeOrder(int direction)
    {
        var index = blocks.FindIndex(block => block.Id == selectedId);
        if (index < 0)
            return;
        var target = Math.Clamp(index + direction, 0, blocks.Count - 1);
        if (target == index)
            return;
        var block = blocks[index];
        blocks.RemoveAt(index);
        blocks.Insert(target, block);
        NormalizeGeometry();
        RefreshAll();
    }

    private void CommitInputs()
    {
        if (synchronizing)
            return;
        profile = profile with
        {
            Width = ReadNumber(canvasWidthInput, profile.Width),
            Height = ReadNumber(canvasHeightInput, profile.Height),
            AutoHeight = autoHeightInput.IsChecked == true
        };
        if (TryGetSelectedBlock(out var block))
        {
            var index = blocks.FindIndex(candidate => candidate.Id == selectedId);
            blocks[index] = block with
            {
                X = ReadNumber(xInput, block.X), Y = ReadNumber(yInput, block.Y),
                Width = ReadNumber(widthInput, block.Width), Height = ReadNumber(heightInput, block.Height)
            };
        }

        NormalizeGeometry();
        synchronizing = true;
        canvasWidthInput.Text = Number(profile.Width);
        canvasHeightInput.Text = Number(profile.Height);
        autoHeightInput.IsChecked = profile.AutoHeight;
        synchronizing = false;
        RenderPreview();
        RefreshSelection();
    }

    private void Save()
    {
        CommitInputs();
        Result = LayoutProfile.Normalize(profile with { Enabled = true, Blocks = blocks.ToArray() });
        DialogResult = true;
    }

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement != previewHost && Keyboard.FocusedElement != blockList)
            return;
        if (e.Key == Key.Delete)
        {
            RemoveSelected();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement != previewHost)
            return;
        var step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
        var delta = e.Key switch
        {
            Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step), Key.Down => new Vector(0, step), _ => new Vector()
        };
        if (delta.LengthSquared == 0)
            return;
        UpdateSelected(block => block with { X = block.X + delta.X, Y = block.Y + delta.Y });
        e.Handled = true;
    }

    private string Describe(LayoutBlock block)
    {
        var description = block.Kind switch
        {
            LayoutBlockKind.Account => "계정",
            LayoutBlockKind.Status => "상태",
            _ => $"{(block.Kind == LayoutBlockKind.Reset ? "초기화" : "사용량")} · {WindowName(block)}"
        };
        return $"{ProviderName(block.ProviderId)} · {description}";
    }

    private string WindowName(LayoutBlock block)
    {
        if (block.WindowId.Length == 0)
            return SourceName(block.Source);
        return UsageLayoutCanvas.TryGetWindow(context, block, out var window) ? window.Label : "지정 한도";
    }

    private static string ProviderName(string providerId) => providerId switch
    {
        "claude" => "Claude",
        "grok" => "Grok",
        _ => "Codex"
    };

    private static string SourceName(UsageWindowSource source) => source switch
    {
        UsageWindowSource.Second => "두 번째", UsageWindowSource.FiveHour => "5시간",
        UsageWindowSource.Weekly => "주간", _ => "첫 번째"
    };

    private static string Number(double value) => Math.Round(value).ToString("0", CultureInfo.CurrentCulture);

    private static double ReadNumber(TextBox input, double fallback)
    {
        var valid = double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                    double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return valid && double.IsFinite(value) ? Math.Round(value) : fallback;
    }

    private sealed record BlockFrame(Border Border, Thumb Resize);
}
