using System.Buffers.Binary;

namespace ModernTubeDownloader.Updates;

internal enum InstallerPhase
{
    Wait = 1, Check, Extract, Backup, VerifyBackup, Replace, VerifyInstallation, Commit, Rollback, Restart,
    Complete, RestartFailed, Failed, Restored, RecoveryRequired, Disconnected
}

internal readonly record struct InstallerProgressFrame(Guid OperationId, long Sequence, InstallerPhase Phase, int Done = 0, int Total = 0)
{
    private const int Protocol = 1;
    internal const int Size = 40;
    internal bool Terminal => Phase >= InstallerPhase.Complete;

    internal void Write(Span<byte> data)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data, Protocol);
        OperationId.TryWriteBytes(data[4..20]);
        BinaryPrimitives.WriteInt64LittleEndian(data[20..], Sequence);
        BinaryPrimitives.WriteInt32LittleEndian(data[28..], (int)Phase);
        BinaryPrimitives.WriteInt32LittleEndian(data[32..], Done);
        BinaryPrimitives.WriteInt32LittleEndian(data[36..], Total);
    }

    internal static InstallerProgressFrame Read(ReadOnlySpan<byte> data, Guid operationId, long previous)
    {
        if (data.Length != Size || BinaryPrimitives.ReadInt32LittleEndian(data) != Protocol ||
            operationId == Guid.Empty || new Guid(data[4..20]) != operationId)
            throw new InvalidDataException("Invalid installer presentation session or revision.");

        var sequence = BinaryPrimitives.ReadInt64LittleEndian(data[20..]);
        var phase = (InstallerPhase)BinaryPrimitives.ReadInt32LittleEndian(data[28..]);
        var done = BinaryPrimitives.ReadInt32LittleEndian(data[32..]);
        var total = BinaryPrimitives.ReadInt32LittleEndian(data[36..]);
        if (sequence <= previous || phase < InstallerPhase.Wait || phase > InstallerPhase.Disconnected ||
            done < 0 || total < done || total > UpdatePackageSecurity.MaximumEntries ||
            phase >= InstallerPhase.Complete && total != 0)
            throw new InvalidDataException("Invalid installer presentation progress.");

        return new(operationId, sequence, phase, done, total);
    }
}
