namespace ModernTubeDownloader.Updates;

/// <summary>A startup child may install only after its parent commits this exact handoff.</summary>
public static class StartupUpdateAdmission
{
    public static string MarkerPath(string requestPath) => Path.GetFullPath(requestPath) + ".startup-commit";

    public static void Validate(string requestPath, UpdaterRequest request)
    {
        if (request.StartupCommitToken is null) return; // Existing manual/exit requests remain compatible.
        var token = request.StartupCommitToken;
        if (token.Length != 32 || token.Any(character => !char.IsAsciiHexDigit(character)))
            throw new InvalidDataException("Invalid startup installation commitment token.");
        var marker = MarkerPath(requestPath);
        UpdatePackageSecurity.EnsureNoReparsePoints(requestPath);
        UpdatePackageSecurity.EnsureNoReparsePoints(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length != 32 || File.ReadAllText(marker) != token)
            throw new InvalidDataException("The parent did not commit this startup installation; existing application files remain unchanged.");
    }
}
