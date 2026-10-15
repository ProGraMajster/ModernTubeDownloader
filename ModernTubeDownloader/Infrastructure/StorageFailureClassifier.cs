using System.ComponentModel;

namespace ModernTubeDownloader.Infrastructure;

public enum StorageFailureKind { None, DiskFull, InsufficientSpace }

/// <summary>Recognizes storage exhaustion without treating every IOException as a full disk.</summary>
public static class StorageFailureClassifier
{
    public static StorageFailureKind Classify(Exception error)
    {
        if (error is StorageSpaceException) return StorageFailureKind.InsufficientSpace;
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Any(e => Classify(e) == StorageFailureKind.DiskFull))
            return StorageFailureKind.DiskFull;
        // HRESULT_FROM_WIN32(ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL). These codes
        // do not depend on the Windows display language or a translated message.
        if (error.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027) ||
            error is Win32Exception { NativeErrorCode: 112 or 39 }) return StorageFailureKind.DiskFull;
        var diagnostic = error is ExternalProcessException process ? process.Result?.StandardErrorText ?? error.Message : error.Message;
        if (diagnostic.Contains("no space left on device", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("errno 28", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("disk full", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("za mało miejsca na dysku", StringComparison.OrdinalIgnoreCase)) return StorageFailureKind.DiskFull;
        return error.InnerException is { } inner ? Classify(inner) : StorageFailureKind.None;
    }
}

/// <summary>A conservative known-size preflight failed; this is not proof that the disk is already full.</summary>
public sealed class StorageSpaceException : IOException
{
    public StorageSpaceException() : base("Insufficient free space for the estimated operation and reserve.") { }
}

/// <summary>No new media phase may start until an explicit successful recovery checkpoint.</summary>
public sealed class StoragePausedException : IOException
{
    public StoragePausedException() : base("Queue storage is unavailable; resume requires a durable checkpoint.") { }
}
