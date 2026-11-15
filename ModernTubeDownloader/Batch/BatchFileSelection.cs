namespace ModernTubeDownloader.Batch;

internal static class BatchFileSelection
{
    public const int MaximumFiles = 32;

    public static string[] Validate(IReadOnlyList<string> files)
    {
        if (files.Count is < 1 or > MaximumFiles) throw new InvalidDataException("Batch.Error.FileSelection");
        var validated = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file) || file.Length > 32767 || !File.Exists(file) ||
                Path.GetExtension(file).ToLowerInvariant() is not (".txt" or ".json" or ".csv"))
                throw new InvalidDataException("Batch.Error.FileSelection");
            var fullPath = Path.GetFullPath(file);
            if (new FileInfo(fullPath).Length > BatchImportParser.MaximumFileBytes) throw new InvalidDataException("Batch.Error.Size");
            if (seen.Add(fullPath)) validated.Add(fullPath);
        }
        return validated.ToArray();
    }
}
