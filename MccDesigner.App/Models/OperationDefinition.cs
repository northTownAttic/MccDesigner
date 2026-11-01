using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MccDesigner.Models;
public sealed class OperationDefinition : INotifyPropertyChanged
{
    private Guid _predecessorId;
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Guid.Empty uses the legacy previous-row setting. An explicit predecessor replaces it.</summary>
    public Guid PredecessorId
    {
        get => _predecessorId;
        set
        {
            if (_predecessorId == value) return;
            _predecessorId = value;
            PropertyChanged?.Invoke(this, new(nameof(PredecessorId)));
            PropertyChanged?.Invoke(this, new(nameof(HasExplicitPredecessor)));
        }
    }
    [JsonIgnore] public bool HasExplicitPredecessor => PredecessorId != Guid.Empty;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New operation";
    public string Resource { get; set; } = "Stage 1";
    public double DurationSeconds { get; set; } = 1.0;
    // True preserves the sequential behavior of projects saved by earlier versions.
    public bool WaitForPrevious { get; set; } = true;
    // A scanning stage moves on X while exclusively reserving the stationary head.
    public bool RequiresScannerHead { get; set; }
    public bool IsLoading { get; set; }
    public bool IsMotion { get; set; }
    public double StartPosition { get; set; }
    public double EndPosition { get; set; } = 100;
}
