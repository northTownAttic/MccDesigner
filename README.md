# MCC Designer MVP
- .NET 8 Windows WPF, Visual Studio 2022
- Edit operation names, resources, decimal durations, motion flags, start/end positions and target lane.
- MCC chart, basic play/pause animation, JSON save/load.
- Sketch-inspired machine view: Stage 1 and Stage 2 are horizontal lanes; gantry position moves along X; scanner head is displayed on selected lane.
- Scheduling supports previous-row dependencies and parallel work with exclusive resource reservations. List order sets scheduling priority. The timeline is a planning visualization, not a machine controller or safety system.
- Build: open `MccDesigner.sln`, restore, then press F5.

## MVP reliability update
- The application now opens MainWindow on startup.
- Add/remove/edit recalculates the chart. Invalid operations clear stale results and show an error in the status bar.
- Resource/lane dropdowns use explicit options. Decimal inputs commit after editing; fix numeric binding errors before using toolbar commands.
- JSON is validated before replacing the current project, and invalid projects cannot be saved.
- Playback uses a monotonic clock, stops on schedule changes/reset, and retains the last head position during idle time and after completion.
- Timeline width follows the scroll viewport (minimum 900 px), so playback redraws do not accumulate width or stretch the time scale.
- Position units remain the MVP's normalized X range 0–100; lane changes remain instantaneous.

## Validation
Windows: install .NET 8 SDK and build `MccDesigner.sln`, then run MccDesigner.App.
Linux compile check: `dotnet build MccDesigner.App/MccDesigner.App.csproj -p:EnableWindowsTargeting=true` (WPF UI execution requires Windows).
Dependency-free engine regression checks: `dotnet run --project MccDesigner.Tests/MccDesigner.Tests.csproj`.
The regression executable checks decimal timing, parallel scheduling, shared-resource conflicts, idle gap insertion, legacy JSON compatibility, invalid input, overflow, and idle/completed motion poses; any failed check exits with an error.
Build outputs in bin/obj are excluded from Git tracking and ignored. Restore/build regenerates them locally; do not commit generated output files.

## Parallel scheduling and layout
- Operation editor: upper left; machine simulator: upper right; MCC timeline: full width below.
- **이전 완료 대기** (wait for previous): checked by default. The row waits for the immediately preceding row to finish. Uncheck to allow the operation to start at time zero or its earliest available resource slot. This is not a barrier waiting for all earlier rows.
- **열 점유** (occupy target lane): applies to Gantry / Head operations. Keep checked for scans; they reserve both the shared gantry/head and selected stage. Clear for a pure head move that does not occupy the stage. Stage operations always reserve their own resource.
- All required resources are reserved together for the entire operation; intervals use [start, end), so touching endpoints are allowed. Independent work may fill gaps before already-scheduled work. The result follows deterministic list priority, not global cycle-time optimization.
- Use **위로 / 아래로** to change list priority and previous-row relationships. Moving/removing a row changes what the following row waits for.
- Pale blue stage bars represent occupation by head operations; tooltips list all reserved resources. The simulator shows each stage's current operation.
- The default example overlaps Stage 2 loading with Stage 1 work: cycle time is 37 s versus a sequential sum of 43.25 s.
- Older JSON files without the new flags still run sequentially. New files persist both flags.
- This version has no arbitrary dependency graph, automatic lane-transfer duration, position-continuity checks or acceleration-based motion profile yet. Specify intended preparation order explicitly; resource exclusion alone does not establish process readiness.
