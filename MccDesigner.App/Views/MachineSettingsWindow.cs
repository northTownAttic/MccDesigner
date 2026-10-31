using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MccDesigner.Models;

namespace MccDesigner.Views;

/// <summary>Edits a private copy; cancellation leaves the current project intact.</summary>
public sealed class MachineSettingsWindow : Window
{
    public MachineSettings Settings { get; }
    private readonly List<TextBox> inputs = new();
    public MachineSettingsWindow(MachineSettings current)
    {
        Settings = current.Copy();
        Title = "설비 위치·크기 설정 (mm)"; Width = 540; Height = 690;
        MinWidth = 450; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = "적용", IsDefault = true };
        var cancel = new Button { Content = "취소", IsCancel = true };
        apply.Click += (_, _) => Apply(); buttons.Children.Add(apply); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var description = new TextBlock { Text = "모든 값은 mm입니다. Rail 1·2와 갠트리는 고정입니다. Stage 1·2는 각각의 레일 위에서 X축 이동하며 입력 X는 스테이지 중심입니다. 범위 변경 시 작업 위치도 확인하세요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(description, Dock.Top); panel.Children.Add(description);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new StackPanel(); scroll.Content = form; panel.Children.Add(scroll);
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
            var box = new TextBox { MinWidth = 100 };
            box.SetBinding(TextBox.TextProperty, new Binding(field.Property) { Source = Settings,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true, StringFormat = "0.###" });
            inputs.Add(box); row.Children.Add(box); form.Children.Add(row);
        }
    }
    private void Apply()
    {
        foreach (var box in inputs) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (inputs.Any(Validation.GetHasError)) { MessageBox.Show(this, "숫자 입력 오류를 수정하세요."); return; }
        try { Settings.Validate(); DialogResult = true; }
        catch (InvalidOperationException error) { MessageBox.Show(this, error.Message, "설비 설정 확인"); }
    }
}
