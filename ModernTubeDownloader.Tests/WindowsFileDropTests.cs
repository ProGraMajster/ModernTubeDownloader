using System.Runtime.InteropServices;
using System.Text;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Tests;

public sealed class WindowsFileDropTests
{
    [Fact]
    public void NativeShellDropDecodesUnicodeRejectsMultipleAndDetaches()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = CreateWindowEx(0, "STATIC", "MTD owned drop test", 0, 0, 0, 1, 1, new nint(-3), 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            var received = new List<string[]>();
            using (var adapter = new WindowsFileDrop(hwnd, files => received.Add(files), new NullAppLogger()))
            {
                adapter.Enabled = true;
                Drop(hwnd, [@"C:\QA\Łódź 日本語.csv"]);
                Assert.Equal(new[] { @"C:\QA\Łódź 日本語.csv" }, Assert.Single(received));
                Drop(hwnd, [@"C:\QA\one.csv", @"C:\QA\two.csv"]);
                Assert.Empty(received[1]);
                adapter.Enabled = false; Drop(hwnd, [@"C:\QA\ignored.csv"]);
                Assert.Equal(2, received.Count);
            }
            Assert.Equal(0, GetWindowLongPtr(hwnd, -20).ToInt64() & 0x10); // WS_EX_ACCEPTFILES restored.
        }
        finally { DestroyWindow(hwnd); }
    }

    private static void Drop(nint hwnd, string[] paths)
    {
        var data = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var handle = GlobalAlloc(0x42, (nuint)(20 + data.Length)); Assert.NotEqual(0, handle);
        var buffer = GlobalLock(handle); Assert.NotEqual(0, buffer);
        Marshal.WriteInt32(buffer, 0, 20); Marshal.WriteInt32(buffer, 16, 1);
        Marshal.Copy(data, 0, buffer + 20, data.Length); GlobalUnlock(handle);
        SendMessage(hwnd, 0x233, (nuint)handle, 0); // Receiver owns DragFinish, even when disabled or rejected.
    }
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] private static extern nint SendMessage(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint handle);
}
