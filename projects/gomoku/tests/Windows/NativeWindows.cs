using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class GomokuSmokeWindows
{
    private const uint AbortIfHung = 2;
    private const uint Command = 0x0111;
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(IntPtr desktop, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, int access, IntPtr attributes);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rectangle rectangle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rectangle rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int left, int top, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr word, IntPtr value, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ReadMessage(IntPtr window, uint message, IntPtr word, StringBuilder value, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr WriteMessage(IntPtr window, uint message, IntPtr word, string value, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr word, IntPtr value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string file, StringBuilder arguments, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int Horizontal, Vertical;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public int PositionX, PositionY, Width, Height, XChars, YChars, Fill, Flags;
        public short ShowWindow, ReservedCount;
        public IntPtr ReservedBytes, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    public sealed class Child
    {
        public IntPtr Handle { get; set; }
        public string Text { get; set; }
        public string ClassName { get; set; }
        public bool Enabled { get; set; }
        public bool Visible { get; set; }
    }

    public static IntPtr OpenDesktop(string name)
    {
        var desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception();
        return desktop;
    }

    public static int StartOnDesktop(string executable, string directory, string desktopName)
    {
        var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)), Desktop = "WinSta0\\" + desktopName };
        ProcessInfo process;
        if (!CreateProcess(executable, new StringBuilder("\"" + executable + "\""), IntPtr.Zero, IntPtr.Zero,
            false, 0, IntPtr.Zero, directory, ref startup, out process)) throw new Win32Exception();

        CloseHandle(process.Thread);
        CloseHandle(process.Process);
        return process.ProcessId;
    }

    public static IntPtr FindWindow(int processId, string titlePrefix, IntPtr desktop)
    {
        var found = IntPtr.Zero;
        EnumDesktopWindows(desktop, (window, unused) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var title = new StringBuilder(256);
            if (owner != processId || GetWindowText(window, title, title.Capacity) == 0 ||
                !title.ToString().StartsWith(titlePrefix, StringComparison.Ordinal)) return true;

            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static Child[] Children(IntPtr window)
    {
        var children = new List<Child>();
        EnumChildWindows(window, (child, unused) =>
        {
            var text = new StringBuilder(512);
            var className = new StringBuilder(256);
            IntPtr ignored;
            ReadMessage(child, 0x000d, new IntPtr(text.Capacity), text, AbortIfHung, 1000, out ignored);
            GetClassName(child, className, className.Capacity);
            children.Add(new Child
            {
                Handle = child,
                Text = text.ToString(),
                ClassName = className.ToString(),
                Enabled = IsWindowEnabled(child),
                Visible = IsWindowVisible(child)
            });
            return true;
        }, IntPtr.Zero);
        return children.ToArray();
    }

    private static IntPtr Send(IntPtr window, uint message, IntPtr word, IntPtr value)
    {
        IntPtr result;
        if (SendMessageTimeout(window, message, word, value, AbortIfHung, 3000, out result) == IntPtr.Zero)
            throw new TimeoutException("The test window did not respond to message " + message + ".");

        return result;
    }

    public static void Press(IntPtr window, bool asynchronous)
    {
        var parent = GetParent(window);
        var id = new IntPtr(GetDlgCtrlID(window) & 0xffff);
        if (asynchronous)
        {
            if (!PostMessage(parent, Command, id, window)) throw new Win32Exception();
        }
        else
        {
            Send(parent, Command, id, window);
        }
    }

    public static void RequestClose(IntPtr window)
    {
        if (!PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
    }

    public static void SetText(IntPtr window, string text)
    {
        IntPtr ignored;
        if (WriteMessage(window, 0x000c, IntPtr.Zero, text, AbortIfHung, 2000, out ignored) == IntPtr.Zero)
            throw new TimeoutException("Setting test control text timed out.");
    }

    public static void SelectIndex(IntPtr window, int index, bool comboBox)
    {
        var selection = comboBox ? 0x014eU : 0x0186U;
        if (Send(window, selection, new IntPtr(index), IntPtr.Zero).ToInt64() != index)
            throw new InvalidOperationException("Cannot select item " + index + ".");

        var change = (1 << 16) | (GetDlgCtrlID(window) & 0xffff);
        Send(GetParent(window), Command, new IntPtr(change), window);
    }

    public static string[] ListText(IntPtr window)
    {
        var values = new List<string>();
        var count = Send(window, 0x018b, IntPtr.Zero, IntPtr.Zero).ToInt32();
        for (var index = 0; index < count; index++)
        {
            var text = new StringBuilder(512);
            IntPtr ignored;
            if (ReadMessage(window, 0x0189, new IntPtr(index), text, AbortIfHung, 1000, out ignored) != IntPtr.Zero)
                values.Add(text.ToString());
        }
        return values.ToArray();
    }

    public static int[] ClientSize(IntPtr window)
    {
        Rectangle bounds;
        if (!GetClientRect(window, out bounds)) throw new InvalidOperationException("Cannot read client dimensions.");
        return new[] { bounds.Right, bounds.Bottom };
    }

    public static int[] Bounds(IntPtr window)
    {
        Rectangle bounds;
        if (!GetWindowRect(window, out bounds)) throw new InvalidOperationException("Cannot read window bounds.");
        return new[] { bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top };
    }

    public static void Click(IntPtr window, int horizontal, int vertical)
    {
        var coordinates = new IntPtr((vertical << 16) | (horizontal & 0xffff));
        Send(window, 0x0201, new IntPtr(1), coordinates);
        Send(window, 0x0202, IntPtr.Zero, coordinates);
    }

    public static void Resize(IntPtr window, int width, int height)
    {
        if (!SetWindowPos(window, IntPtr.Zero, 0, 0, width, height, 0x16)) throw new Win32Exception();
    }

    public static bool Fits(IntPtr control)
    {
        Rectangle child;
        if (!GetWindowRect(control, out child) || child.Right <= child.Left || child.Bottom <= child.Top) return false;

        for (var parent = GetParent(control); parent != IntPtr.Zero; parent = GetParent(parent))
        {
            Rectangle client;
            var origin = new Point();
            if (!GetClientRect(parent, out client) || !ClientToScreen(parent, ref origin) ||
                child.Left < origin.Horizontal || child.Top < origin.Vertical ||
                child.Right > origin.Horizontal + client.Right || child.Bottom > origin.Vertical + client.Bottom) return false;
        }
        return true;
    }
}
