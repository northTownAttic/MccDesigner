using MccDesigner.Models;
namespace MccDesigner.Services;

public static class AxisScheduleValidator
{
    public static void Validate(IReadOnlyList<ScheduledOperation> operations, MachineSettings? settings = null)
    {
        settings ??= new MachineSettings();
        const double tolerance = 1e-8;
        foreach (var resource in MachineAxes.Resources)
        {
            var position = settings.InitialPosition(resource);
            foreach (var op in operations.Where(o => o.Resource == resource && o.IsMotion).OrderBy(o => o.StartTime))
            {
                if (Math.Abs(op.StartPosition - position) > tolerance)
                    throw new InvalidOperationException($"'{op.Name}' 시작 위치가 {resource}의 이전 종료 위치 {position:0.###}와 다릅니다. 복귀/이동 작업을 추가하세요.");
                position = op.EndPosition;
            }
        }
        foreach (var scan in operations.Where(o => o.RequiresScannerHead))
        {
            var headY = MachineStateEvaluator.PositionAt(operations, MachineAxes.Head, scan.StartTime, settings);
            if (Math.Abs(headY - settings.ScanY(scan.Resource)) > tolerance)
                throw new InvalidOperationException($"'{scan.Name}' 시작 전에 헤드를 {scan.Resource}의 Y 위치로 이동해야 합니다 (Y={settings.ScanY(scan.Resource):0.###} mm).");
        }
    }
}
