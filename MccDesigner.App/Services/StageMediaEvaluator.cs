using MccDesigner.Models;
namespace MccDesigner.Services;

public enum StageMediaState { Empty, Loading, Loaded, Unloading }

public static class StageMediaEvaluator
{
    public static string Label(StageMediaState state) => state switch
    {
        StageMediaState.Loading => "로딩 중", StageMediaState.Loaded => "미디어 있음",
        StageMediaState.Unloading => "언로딩 중", _ => "미디어 없음"
    };
    /// <summary>State is sampled independently of playback, so seeking backward restores it too.</summary>
    public static StageMediaState Evaluate(IReadOnlyList<ScheduledOperation> schedule, string stage, double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        var jobs = schedule.Where(x => x.Resource == stage).OrderBy(x => x.StartTime).ToList();
        var loaded = false;
        foreach (var job in jobs)
        {
            if (time < job.StartTime) break;
            if (time < job.EndTime)
            {
                if (job.IsLoading) return StageMediaState.Loading;
                if (job.IsUnloading) return loaded ? StageMediaState.Unloading : StageMediaState.Empty;
                return loaded ? StageMediaState.Loaded : StageMediaState.Empty;
            }
            if (job.IsLoading) loaded = true;
            if (job.IsUnloading) loaded = false;
        }
        // Older projects have no unloading flag. Their stage tail remains the compatibility boundary.
        if (jobs.Count > 0 && !jobs.Any(x => x.IsUnloading) && !jobs[^1].IsLoading && time >= jobs[^1].EndTime) loaded = false;
        return loaded ? StageMediaState.Loaded : StageMediaState.Empty;
    }
}
