using MccDesigner.Mvvm;
namespace MccDesigner.ViewModels;

public sealed class DependencyChoice(Guid id, string label) : ObservableObject
{
    public Guid Id { get; } = id;
    private string _label = label;
    public string Label { get => _label; set => SetProperty(ref _label, value); }
}
