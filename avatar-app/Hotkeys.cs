using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AvatarStudio;

public sealed class Hotkeys : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    readonly IntPtr handle;
    readonly HwndSource source;
    readonly Dictionary<int, Action> actions = new();
    public Hotkeys(Window window)
    {
        handle = new WindowInteropHelper(window).Handle;
        source = HwndSource.FromHwnd(handle)!; source.AddHook(Hook);
    }
    public void Clear() { foreach (var id in actions.Keys) UnregisterHotKey(handle, id); actions.Clear(); }
    public bool Add(Shortcut shortcut, Action action)
    {
        int id = actions.Count + 1;
        if (!RegisterHotKey(handle, id, shortcut.Modifiers | 0x4000, (uint)shortcut.Key)) return false;
        actions[id] = action; return true;
    }
    IntPtr Hook(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x312 && actions.TryGetValue(w.ToInt32(), out var action)) { action(); handled = true; }
        return IntPtr.Zero;
    }
    static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static bool IsDown(Shortcut s) => Down(s.Key) &&
        (((s.Modifiers & 2) != 0) == Down(0x11)) && (((s.Modifiers & 1) != 0) == Down(0x12)) &&
        (((s.Modifiers & 4) != 0) == Down(0x10)) && (((s.Modifiers & 8) != 0) == (Down(0x5b) || Down(0x5c)));
    public void Dispose() { Clear(); source.RemoveHook(Hook); }
}

public sealed class ShortcutDialog : Window
{
    public Shortcut? Result { get; private set; }
    public ShortcutDialog(Window owner)
    {
        Owner = owner; Title = "Asignar atajo"; Width = 440; Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        Background = MainWindow.Navy; Foreground = System.Windows.Media.Brushes.White;
        Content = new System.Windows.Controls.TextBlock { Text = "Pulsa una combinación de teclas.\nUsa Ctrl, Alt o una tecla F1–F24.\nEsc cancela.", Margin = new Thickness(24), FontSize = 17 };
        PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { DialogResult = false; return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var mod = Keyboard.Modifiers;
            if ((mod & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 && !(key >= Key.F1 && key <= Key.F24)) return;
            uint mask = (uint)(((mod & ModifierKeys.Alt) != 0 ? 1 : 0) | ((mod & ModifierKeys.Control) != 0 ? 2 : 0) |
                ((mod & ModifierKeys.Shift) != 0 ? 4 : 0) | ((mod & ModifierKeys.Windows) != 0 ? 8 : 0));
            Result = new Shortcut(KeyInterop.VirtualKeyFromKey(key), mask); DialogResult = true;
        };
    }
}
