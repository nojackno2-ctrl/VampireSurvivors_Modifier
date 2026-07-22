using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace VSModifier.App.Hotkeys;

public enum TrainerHotkeyCommand
{
    ToggleInvulnerability = 1,
    ToggleQuickTreasure = 2,
    ToggleMaxTreasure = 3,
    ToggleDamageMultiplier = 4,
    ToggleGameSpeed = 5,
    EmergencyDetach = 6
}

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly HashSet<int> _registeredIds = [];
    private bool _disposed;

    public GlobalHotkeyService(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == 0)
        {
            throw new InvalidOperationException("WPF 視窗控制代碼尚未建立，無法註冊全域熱鍵。");
        }

        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("無法取得 WPF 訊息來源。");
        _source.AddHook(WindowProc);
    }

    public event EventHandler<TrainerHotkeyCommand>? HotkeyPressed;

    public bool IsRegistered => _registeredIds.Count > 0;

    public void RegisterDefaults()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRegistered)
        {
            return;
        }

        (TrainerHotkeyCommand Command, Key Key)[] bindings =
        [
            (TrainerHotkeyCommand.ToggleInvulnerability, Key.F1),
            (TrainerHotkeyCommand.ToggleQuickTreasure, Key.F2),
            (TrainerHotkeyCommand.ToggleMaxTreasure, Key.F3),
            (TrainerHotkeyCommand.ToggleDamageMultiplier, Key.F4),
            (TrainerHotkeyCommand.ToggleGameSpeed, Key.F5),
            (TrainerHotkeyCommand.EmergencyDetach, Key.F12)
        ];

        const HotkeyModifiers modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat;
        try
        {
            foreach ((TrainerHotkeyCommand command, Key key) in bindings)
            {
                int id = (int)command;
                uint virtualKey = checked((uint)KeyInterop.VirtualKeyFromKey(key));
                if (!RegisterHotKey(_windowHandle, id, (uint)modifiers, virtualKey))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"無法註冊 Ctrl+Shift+{key}；可能已被其他程式使用。");
                }

                _registeredIds.Add(id);
            }
        }
        catch
        {
            UnregisterAll();
            throw;
        }
    }

    public void UnregisterAll()
    {
        foreach (int id in _registeredIds)
        {
            _ = UnregisterHotKey(_windowHandle, id);
        }

        _registeredIds.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _source.RemoveHook(WindowProc);
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && _registeredIds.Contains((int)wParam))
        {
            handled = true;
            HotkeyPressed?.Invoke(this, (TrainerHotkeyCommand)(int)wParam);
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    [Flags]
    private enum HotkeyModifiers : uint
    {
        Control = 0x0002,
        Shift = 0x0004,
        NoRepeat = 0x4000
    }
}
