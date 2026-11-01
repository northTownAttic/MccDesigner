using System.Text.Json;
using MccDesigner.Models;
namespace MccDesigner.Services;

public static class ProjectSerializer
{
    private sealed class ProjectDocument
    {
        public int FormatVersion { get; set; }
        public string? AxisModel { get; set; }
        public string? Units { get; set; }
        public MediaSupplySettings? Supply { get; set; }
        public PlaybackOptions? Playback { get; set; }
        public MachineSettings? Machine { get; set; }
        public List<OperationDefinition>? Operations { get; set; }
    }
    private const string AxisModel = "FixedGantry_StageX_HeadY";
    public static string Serialize(IReadOnlyList<OperationDefinition> operations, MachineSettings? settings = null, PlaybackOptions? playback = null, MediaSupplySettings? supply = null)
    {
        settings ??= new MachineSettings();
        playback ??= new PlaybackOptions();
        playback.Validate();
        supply ??= new MediaSupplySettings(); supply.Validate();
        if (supply.Enabled) new MediaProductionSession(operations, settings, supply, new PlaybackOptions());
        new Scheduler().Calculate(operations, settings);
        return JsonSerializer.Serialize(new ProjectDocument { FormatVersion = 3, AxisModel = AxisModel, Units = "mm", Machine = settings, Playback = playback, Supply = supply,
            Operations = operations.ToList() }, new JsonSerializerOptions { WriteIndented = true });
    }
    public static List<OperationDefinition> Deserialize(string json) => DeserializeProject(json).Operations;

    public static LoadedProject DeserializeProject(string json)
    {
        using var document = JsonDocument.Parse(json);
        List<OperationDefinition> operations;
        var settings = new MachineSettings();
        var legacyUnits = true;
        var playback = new PlaybackOptions();
        var supply = new MediaSupplySettings();
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            // Old head-X data has a different physical meaning; never reinterpret it as head-Y.
            var legacy = JsonSerializer.Deserialize<List<OperationDefinition>>(json)
                ?? throw new InvalidOperationException("작업 데이터가 없습니다.");
            if (legacy.Any(o => o != null && o.Resource is "Gantry / Head" or MachineAxes.Head))
                throw new InvalidOperationException("이전 갠트리 X축 프로젝트입니다. 헤드 작업을 스테이지 X 스캔과 헤드 Y 이동으로 다시 구성해야 합니다. 원본 파일과 현재 프로젝트는 유지됩니다.");
            operations = legacy;
        }
        else
        {
            var project = JsonSerializer.Deserialize<ProjectDocument>(json)
                ?? throw new InvalidOperationException("프로젝트 데이터가 없습니다.");
            if (project.FormatVersion is not (2 or 3) || project.AxisModel != AxisModel)
                throw new InvalidOperationException("지원하지 않는 프로젝트 버전 또는 축 모델입니다.");
            if (project.FormatVersion == 3)
            {
                if (project.Units != "mm" || project.Machine is null)
                    throw new InvalidOperationException("mm 단위 또는 설비 설정이 누락된 프로젝트입니다.");
                settings = project.Machine;
                legacyUnits = false;
            }
            supply = project.Supply ?? new MediaSupplySettings(); supply.Validate();
            playback = project.Playback ?? new PlaybackOptions();
            playback.Validate();
            operations = project.Operations ?? throw new InvalidOperationException("작업 데이터가 없습니다.");
        }
        new Scheduler().Calculate(operations, settings);
        if (supply.Enabled) new MediaProductionSession(operations, settings, supply, new PlaybackOptions());
        return new LoadedProject(operations, settings, legacyUnits, playback, supply);
    }
}

public sealed record LoadedProject(List<OperationDefinition> Operations, MachineSettings Machine, bool ImportedLegacyUnits, PlaybackOptions Playback, MediaSupplySettings Supply);
