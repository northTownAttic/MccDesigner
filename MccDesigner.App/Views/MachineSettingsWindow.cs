using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MccDesigner.Models;
using MccDesigner.Services;

namespace MccDesigner.Views;

/// <summary>Edits a private copy; cancellation leaves the current project intact.</summary>
public sealed class MachineSettingsWindow : Window
{
    public MachineSettings Settings { get; }
    private readonly List<TextBox> inputs = new();
    private readonly MachineSettingsPreview preview = new();
    private readonly ScrollViewer previewScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false };
    private readonly TextBlock validationMessage = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock selectionLabel = new() { Text = "입력 항목을 선택하면 해당 부품을 강조합니다.", Margin = new Thickness(0, 5, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Button apply = new() { Content = "적용", IsDefault = true };
    private double previewZoom = 1;
    public MachineSettingsWindow(MachineSettings current)
    {
        Settings = current.Copy();
        Title = "설비 위치·크기 설정 (mm) · 실시간 미리보기";
        Width = Math.Min(1440, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = Math.Min(900, Width); MinHeight = Math.Min(560, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        refresh.Tick += (_, _) => { refresh.Stop(); UpdatePreview(); };
        Closed += (_, _) => refresh.Stop();
        Loaded += (_, _) => { ResizePreview(); UpdatePreview(); };
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "취소", IsCancel = true };
        apply.Click += (_, _) => Apply(); buttons.Children.Add(apply); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var description = new TextBlock { Text = "모든 값은 mm입니다. Rail 1·2와 갠트리는 고정입니다. Stage 1·2는 각각의 레일 위에서 X축 이동하며 입력 X는 스테이지 중심입니다. 범위 변경 시 작업 위치도 확인하세요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(description, Dock.Top); panel.Children.Add(description);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(columns);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new StackPanel(); scroll.Content = form; columns.Children.Add(scroll);
        var visualPanel = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(visualPanel, 1); columns.Children.Add(visualPanel);
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "초기 위치 미리보기 · 적용 전", FontSize = 20, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "회색: 레일/갠트리 · 청록: Stage 1 · 주황: Stage 2 · 파랑: Head", TextWrapping = TextWrapping.Wrap });
        header.Children.Add(selectionLabel);
        var zoomControls = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var fit = new Button { Content = "화면 맞춤" };
        DockPanel.SetDock(fit, Dock.Right); zoomControls.Children.Add(fit);
        zoomControls.Children.Add(new TextBlock { Text = "확대", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var zoom = new Slider { Minimum = 100, Maximum = 300, Value = 100, TickFrequency = 25, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        zoom.ValueChanged += (_, _) => { previewZoom = zoom.Value / 100; ResizePreview(); };
        fit.Click += (_, _) => { zoom.Value = 100; previewScroll.ScrollToHorizontalOffset(0); previewScroll.ScrollToVerticalOffset(0); };
        zoomControls.Children.Add(zoom); header.Children.Add(zoomControls);
        DockPanel.SetDock(header, Dock.Top); visualPanel.Children.Add(header);
        var warnings = new ScrollViewer { Content = validationMessage, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(warnings, Dock.Bottom); visualPanel.Children.Add(warnings);
        preview.HorizontalAlignment = HorizontalAlignment.Left; preview.VerticalAlignment = VerticalAlignment.Top;
        previewScroll.Content = preview;
        previewScroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) ResizePreview(); };
        visualPanel.Children.Add(previewScroll);
        (string Label, string Property)[] fields =
        [
            ("Rail 1 X 시작", nameof(MachineSettings.Rail1XStart)), ("Rail 1 X 끝", nameof(MachineSettings.Rail1XEnd)),
            ("Rail 2 X 시작", nameof(MachineSettings.Rail2XStart)), ("Rail 2 X 끝", nameof(MachineSettings.Rail2XEnd)),
            ("Rail 폭 (Y, 공통)", nameof(MachineSettings.RailWidthY)),
            ("Stage 1 길이 (X)", nameof(MachineSettings.Stage1LengthX)), ("Stage 1 폭 (Y)", nameof(MachineSettings.Stage1WidthY)),
            ("Stage 2 길이 (X)", nameof(MachineSettings.Stage2LengthX)), ("Stage 2 폭 (Y)", nameof(MachineSettings.Stage2WidthY)),
            ("Stage X 최소", nameof(MachineSettings.StageXMin)), ("Stage X 최대", nameof(MachineSettings.StageXMax)),
            ("Stage 1 초기 X", nameof(MachineSettings.Stage1InitialX)), ("Stage 2 초기 X", nameof(MachineSettings.Stage2InitialX)),
            ("Head Y 최소", nameof(MachineSettings.HeadYMin)), ("Head Y 최대", nameof(MachineSettings.HeadYMax)),
            ("Rail 1 / Stage 1 Y 위치", nameof(MachineSettings.Stage1Y)), ("Rail 2 / Stage 2 Y 위치", nameof(MachineSettings.Stage2Y)),
            ("Head 초기 Y", nameof(MachineSettings.HeadInitialY)),
            ("갠트리 X 시작", nameof(MachineSettings.GantryXStart)), ("갠트리 X 끝", nameof(MachineSettings.GantryXEnd)),
            ("갠트리 Y 시작", nameof(MachineSettings.GantryYStart)), ("갠트리 Y 끝", nameof(MachineSettings.GantryYEnd)),
            ("Head 고정 X (중심)", nameof(MachineSettings.HeadX)),
            ("Head 폭 (X)", nameof(MachineSettings.HeadWidth)), ("Head 높이 (Y)", nameof(MachineSettings.HeadHeight))
        ];
        foreach (var field in fields)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 8, 2) };
            row.Children.Add(new TextBlock { Text = field.Label + " (mm)", Width = 240, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { MinWidth = 100, Tag = field.Label };
            box.SetBinding(TextBox.TextProperty, new Binding(field.Property) { Source = Settings,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true, StringFormat = "0.###" });
            box.TextChanged += (_, _) => { refresh.Stop(); refresh.Start(); };
            box.GotKeyboardFocus += (_, _) => {
                preview.SelectedProperty = field.Property;
                selectionLabel.Text = $"선택: {field.Label} · 분홍 테두리로 강조";
                preview.Render();
            };
            inputs.Add(box); row.Children.Add(box); form.Children.Add(row);
        }
    }
    private void ResizePreview()
    {
        if (previewScroll.ViewportWidth <= 1 || previewScroll.ViewportHeight <= 1) return;
        preview.Zoom = previewZoom;
        preview.Width = previewScroll.ViewportWidth * previewZoom;
        preview.Height = previewScroll.ViewportHeight * previewZoom;
        preview.Render();
    }
    private bool UpdatePreview()
    {
        foreach (var box in inputs) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        var errors = inputs.Where(Validation.GetHasError).Select(box => $"{box.Tag}: 숫자를 입력하세요.").ToList();
        errors.AddRange(Settings.GetValidationErrors());
        var draft = MachinePreviewDraft.Create(Settings, preview.Settings);
        preview.Settings = draft.Drawing;
        preview.Render();
        validationMessage.Foreground = errors.Count == 0 ? Brushes.DarkGreen : Brushes.Firebrick;
        validationMessage.Text = errors.Count == 0
            ? "입력값이 미리보기에 반영되었습니다. 적용하면 본 시뮬레이터에도 반영됩니다. 취소하면 기존 설정을 유지합니다."
            : $"제약 위반 {errors.Count}개 — 미리보기는 계속 표시하며 적용은 차단합니다.\n• " + string.Join("\n• ", errors);
        if (draft.FallbackFields.Count > 0 || inputs.Any(Validation.GetHasError))
            validationMessage.Text += "\n숫자가 아니거나 표시 한계를 넘은 항목은 마지막으로 표시 가능한 값을 사용합니다. 입력값은 변경하지 않습니다.";
        if (errors.Count > 0)
            validationMessage.Text += "\n미리보기에서는 역전된 범위와 음수 크기를 외곽의 절댓값으로, 0 크기를 작은 표시로 그립니다. 실제 입력값은 그대로이며 적용할 수 없습니다.";
        return errors.Count == 0;
    }
    private void Apply()
    {
        refresh.Stop();
        if (!UpdatePreview())
        {
            MessageBox.Show(this, "제약 위반 또는 숫자 입력 오류가 있어 적용할 수 없습니다. 미리보기 아래의 위반 내용을 수정하세요.", "설비 설정 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // The simulator and saved project remain strict even though drawing tolerates invalid drafts.
        Settings.Validate();
        DialogResult = true;
    }
}
