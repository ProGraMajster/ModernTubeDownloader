namespace ModernTubeDownloader.Services;

public interface IDownloadTiming
{
    DateTimeOffset UtcNow { get; }
    TimeSpan NextAdmissionDelay(int minimumSeconds, int maximumSeconds);
    TimeSpan RetryDelay(int failedAttempt, DownloadFailureCategory category);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class DownloadTiming : IDownloadTiming
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeSpan NextAdmissionDelay(int minimumSeconds, int maximumSeconds) =>
        TimeSpan.FromSeconds(Random.Shared.Next(minimumSeconds, maximumSeconds + 1));

    public TimeSpan RetryDelay(int failedAttempt, DownloadFailureCategory category) =>
        TimeSpan.FromSeconds(category == DownloadFailureCategory.RateLimited
            ? Math.Min(30, 5 * failedAttempt)
            : Math.Min(10, 2 * failedAttempt));

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
