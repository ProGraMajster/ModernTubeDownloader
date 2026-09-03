namespace ModernTubeDownloader.Localization;

internal sealed record LocalizedOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
