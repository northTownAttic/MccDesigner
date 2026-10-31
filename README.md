# MCC Designer MVP
- .NET 8 Windows WPF, Visual Studio 2022
- Edit operation names, resources, decimal durations, motion flags, start/end positions and target lane.
- MCC chart, basic play/pause animation, JSON save/load.
- Sketch-inspired machine view: Stage 1 and Stage 2 are horizontal lanes; gantry position moves along X; scanner head is displayed on selected lane.
- Scheduling currently follows the entered operation order. The timeline is a planning visualization, not a machine controller or safety system.
- Build: open `MccDesigner.sln`, restore, then press F5.

## MVP reliability update
- The application now opens MainWindow on startup.
- Add/remove/edit recalculates the chart. Invalid operations clear stale results and show an error in the status bar.
- Resource/lane dropdowns use explicit options. Decimal inputs commit after editing; fix numeric binding errors before using toolbar commands.
- JSON is validated before replacing the current project, and invalid projects cannot be saved.
- Playback uses a monotonic clock, stops on schedule changes/reset, and retains the last head position during idle time and after completion.
- Timeline width follows the scroll viewport (minimum 900 px), so playback redraws do not accumulate width or stretch the time scale.
- Position units remain the MVP's normalized X range 0–100; lane changes remain instantaneous. Scheduling remains sequential.

## Validation
Windows: install .NET 8 SDK and build `MccDesigner.sln`, then run MccDesigner.App.
Linux compile check: `dotnet build MccDesigner.App/MccDesigner.App.csproj -p:EnableWindowsTargeting=true` (WPF UI execution requires Windows).
Dependency-free engine regression checks: `dotnet run --project MccDesigner.Tests/MccDesigner.Tests.csproj`.
The regression executable checks decimal timing, invalid input, overflow, and idle/completed motion poses; any failed check exits with an error.
Existing tracked bin/obj files were left untouched; future generated outputs are ignored.
