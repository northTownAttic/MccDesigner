using MccDesigner.Models;
namespace MccDesigner.Services;

/// <summary>Numeric fallback is for drawing only; never modifies or validates away the editable draft.</summary>
public sealed record MachinePreviewDraft(MachineSettings Drawing, IReadOnlyList<string> FallbackFields)
{
    public static MachinePreviewDraft Create(MachineSettings input, MachineSettings? previousDrawing = null)
    {
        var drawing = input.Copy();
        var fallback = previousDrawing ?? new MachineSettings();
        var replaced = new List<string>();
        foreach (var property in MachineSettings.NumericProperties)
        {
            var value = (double)property.GetValue(input)!;
            if (!double.IsFinite(value) || Math.Abs(value) > 1e9)
            {
                property.SetValue(drawing, property.GetValue(fallback));
                replaced.Add(property.Name);
            }
        }
        return new(drawing, replaced.AsReadOnly());
    }
}
