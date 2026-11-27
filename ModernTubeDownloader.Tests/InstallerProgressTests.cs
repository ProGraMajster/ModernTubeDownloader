using System.Buffers.Binary;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Tests;

public sealed class InstallerProgressTests
{
    [Fact]
    public void FramesRequireExactRevisionOperationAndIncreasingSequence()
    {
        var operation = Guid.NewGuid();
        var frame = new InstallerProgressFrame(operation, 9, InstallerPhase.Replace, 2, 7);
        var bytes = new byte[InstallerProgressFrame.Size];
        frame.Write(bytes);

        Assert.Equal(frame, InstallerProgressFrame.Read(bytes, operation, 2));
        Assert.Throws<InvalidDataException>(() => InstallerProgressFrame.Read(bytes, operation, 9));
        Assert.Throws<InvalidDataException>(() => InstallerProgressFrame.Read(bytes, Guid.NewGuid(), 0));
        Assert.Throws<InvalidDataException>(() => InstallerProgressFrame.Read(bytes.AsSpan(0, 39), operation, 0));
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2);
        Assert.Throws<InvalidDataException>(() => InstallerProgressFrame.Read(bytes, operation, 0));
    }

    [Theory]
    [InlineData((int)InstallerPhase.Replace, -1, 12)]
    [InlineData((int)InstallerPhase.Extract, 3, 2)]
    [InlineData((int)InstallerPhase.Extract, 0, 5001)]
    [InlineData((int)InstallerPhase.Complete, 1, 1)]
    [InlineData(99, 0, 0)]
    public void RejectsInvalidProgress(int phase, int done, int total)
    {
        var operation = Guid.NewGuid();
        var bytes = new byte[InstallerProgressFrame.Size];
        new InstallerProgressFrame(operation, 1, (InstallerPhase)phase, done, total).Write(bytes);
        Assert.Throws<InvalidDataException>(() => InstallerProgressFrame.Read(bytes, operation, 0));
    }

    [Fact]
    public void InstalledVerificationFailureRestoresOriginalBeforeCommit()
    {
        var root = Directory.CreateTempSubdirectory("mtd-installed-verify-");
        try
        {
            var original = root.CreateSubdirectory("installation").FullName;
            var extracted = root.CreateSubdirectory("extracted").FullName;
            const string executable = "ModernTubeDownloader.exe";
            var installedFile = Path.Combine(original, executable);
            File.WriteAllText(installedFile, "1.3.2");
            File.WriteAllText(Path.Combine(extracted, executable), "1.4.0");
            var failure = Assert.Throws<UpdateTransactionException>(() => UpdateTransaction.Install(original, extracted, [executable],
                beforeCopy: null, observe: (stage, outcome, _, _) =>
                {
                    if (stage == DiagnosticStage.Replace && outcome == DiagnosticOutcome.Completed)
                        File.WriteAllText(installedFile, "corrupt bytes");
                }));
            Assert.Equal(UpdateOperation.VerifyInstalledFile, failure.Failure.Operation);
            Assert.Equal(UpdateRecoveryStatus.Restored, failure.Recovery);
            Assert.Equal("1.3.2", File.ReadAllText(installedFile));
            UpdatePackageSecurity.VerifyStartupInstallation(original);
        }
        finally { root.Delete(recursive: true); }
    }
}
