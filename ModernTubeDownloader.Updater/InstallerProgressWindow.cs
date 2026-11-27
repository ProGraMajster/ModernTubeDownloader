using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ModernTubeDownloader.Updates;

internal static class InstallerProgressWindow
{
    private const uint Created = 0, Timer = 4;
    private const uint SetMarqueeMode = 0x400 + 103, SetPosition = 0x400 + 106, AnimateMarquee = 0x400 + 107,
        SetElement = 0x400 + 108;
    private const uint ClickButton = 0x400 + 102;
    private const int MainInstruction = 3, Content = 0;
    private static readonly Lazy<Dictionary<string, string[]>> Messages = new(() =>
    {
        using var stream = typeof(InstallerProgressWindow).Assembly.GetManifestResourceStream("InstallerMessages.json")
            ?? throw new InvalidDataException("The installer presentation catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidDataException("The installer presentation catalog is invalid.");
    });

    internal static string NormalizeLanguage(string language) => Messages.Value.ContainsKey(language) ? language : "en";

    internal static int Run(string[] args)
    {
        if (args.Length != 5 || !Guid.TryParseExact(args[1], "N", out var nonce) || nonce == Guid.Empty ||
            !Guid.TryParseExact(args[2], "N", out var operation) || operation == Guid.Empty ||
            !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var workerId) || workerId <= 0) return 2;
        var language = NormalizeLanguage(args[4]);
        var culture = CultureInfo.GetCultureInfo(language);
        var text = Messages.Value[language];
        using var close = new CancellationTokenSource();
        var available = new System.Collections.Concurrent.ConcurrentQueue<InstallerProgressFrame>();
        long displayed = 0;
        nuint successAt = 0;
        var receive = InstallerProgressChannel.ReceiveAsync(nonce, operation, workerId, frame =>
        {
            while (available.TryDequeue(out _)) { }
            available.Enqueue(frame);
        }, close.Token);

        DialogCallback callback = (window, notification, elapsed, _, _) =>
        {
            if (notification == Created) SendMessage(window, AnimateMarquee, 1, 45);
            if (notification != Timer) return 0;
            if (successAt != 0 && elapsed >= successAt) SendMessage(window, ClickButton, 8, 0);
            if (!available.TryDequeue(out var frame) || frame.Sequence <= displayed) return 0;
            displayed = frame.Sequence;
            if (frame.Phase == InstallerPhase.Complete) successAt = elapsed + 5000;

            SendText(window, SetElement, MainInstruction, text[(int)frame.Phase]);
            var detail = frame.Total > 0
                ? string.Create(culture, $"[{frame.Done:N0} / {frame.Total:N0}]")
                : frame.Terminal ? "" : text[0];
            SendText(window, SetElement, Content, detail);
            var marquee = frame.Total == 0 && !frame.Terminal;
            SendMessage(window, SetMarqueeMode, marquee ? 1 : 0, 0);
            SendMessage(window, AnimateMarquee, marquee ? 1 : 0, 45);
            if (!marquee)
            {
                var percent = frame.Total > 0 ? (int)(100L * frame.Done / frame.Total) :
                    frame.Phase is InstallerPhase.Complete or InstallerPhase.RestartFailed ? 100 : 0;
                SendMessage(window, SetPosition, percent, 0);
            }
            return 0;
        };
        var config = new TaskDialogConfig
        {
            Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
            Flags = 0x8 | 0x400 | 0x800,
            Buttons = 0x20, // Windows-localized Close only dismisses the presentation.
            Title = "ModernTubeDownloader",
            MainInstruction = text[(int)InstallerPhase.Wait],
            Content = text[0],
            Callback = Marshal.GetFunctionPointerForDelegate(callback),
            Width = 225
        };
        var result = ShowDialog(ref config, out _, out _, out _);
        GC.KeepAlive(callback);
        close.Cancel();
        GC.KeepAlive(receive);
        return result < 0 ? 2 : 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct TaskDialogConfig
    {
        internal uint Size;
        internal nint Parent, Instance;
        internal uint Flags, Buttons;
        internal string Title;
        internal nint MainIcon;
        internal string MainInstruction, Content;
        internal uint ButtonCount;
        internal nint CustomButtons;
        internal int DefaultButton;
        internal uint RadioCount;
        internal nint RadioButtons;
        internal int DefaultRadio;
        internal nint Verification, Expanded, ExpandedControl, CollapsedControl, FooterIcon, Footer;
        internal nint Callback, CallbackData;
        internal uint Width;
    }

    private delegate int DialogCallback(nint window, uint notification, nuint wParam, nint lParam, nint data);

    [DllImport("comctl32.dll", EntryPoint = "TaskDialogIndirect", ExactSpelling = true)]
    private static extern int ShowDialog(ref TaskDialogConfig config, out int button, out int radio, out int verification);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendText(nint window, uint message, nint wParam, string lParam);
}
