using System;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using WindowsInput;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ReleaseAC
{
    public partial class Form1 : Form
    {
        private InputSimulator _inputSimulator = new InputSimulator();
        private bool _isClicking = false;
        private Thread _clickingThread;
        private GlobalHotkey _globalHotkey;
        private Keys _currentHotkey = Keys.F6;
        private uint _currentModifiers = 0;
        private readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "SalkAutoClicker");
        private readonly string SettingsFile = "settings.dat";
        // Low-level mouse hook for position picking
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_MOUSEMOVE = 0x0200; // Add this for mouse movement
        private IntPtr _hookID = IntPtr.Zero;
        private bool _isPickingLocation = false;

        // Windows API declarations
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        public Form1()
        {
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            InitializeComponent();

            // Initialize settings before creating hotkey
            InitializeSettings();
            InitializeGlobalHotkey();

            radioLeftClick.Checked = true;
            radioButton4.Checked = true;
            radioButton2.Checked = true;

            button2.Click += button2_Click;
        }

        // Add this class inside Form1
        private class GlobalHotkey : IDisposable
        {
            [DllImport("user32.dll")]
            private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
            [DllImport("user32.dll")]
            private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            private const int WM_HOTKEY = 0x0312;
            private readonly IntPtr _handle;
            private readonly int _id;

            public GlobalHotkey(IntPtr handle, int id, uint modifier, uint key)
            {
                _handle = handle;
                _id = id;
                if (!RegisterHotKey(_handle, _id, modifier, key))
                    throw new InvalidOperationException("Hotkey registration failed");
            }

            public void Dispose() => UnregisterHotKey(_handle, _id);
            public static bool IsHotkeyMessage(Message m, int id) =>
                m.Msg == WM_HOTKEY && m.WParam.ToInt32() == id;
        }

        private void InitializeGlobalHotkey()
        {
            try
            {
                _globalHotkey = new GlobalHotkey(this.Handle, 1, _currentModifiers, (uint)_currentHotkey);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to register hotkey: {ex.Message}");
            }
        }

        // Mouse hook implementation
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && _isPickingLocation)
                {
                    MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                    // Throttle mouse move updates to prevent UI overload
                    if (wParam == (IntPtr)WM_MOUSEMOVE)
                    {
                        if (Environment.TickCount - _lastUpdate > 50) // Update every 50ms
                        {
                            _lastUpdate = Environment.TickCount;
                            this.BeginInvoke((MethodInvoker)(() =>
                            {
                                if (!this.IsDisposed && _isPickingLocation)
                                {
                                    textBox2.Text = hookStruct.pt.X.ToString();
                                    textBox1.Text = hookStruct.pt.Y.ToString();
                                }
                            }));
                        }
                    }
                    else if (wParam == (IntPtr)WM_LBUTTONDOWN)
                    {
                        this.BeginInvoke((MethodInvoker)(() =>
                        {
                            if (!this.IsDisposed && _isPickingLocation)
                            {
                                textBox2.Text = hookStruct.pt.X.ToString();
                                textBox1.Text = hookStruct.pt.Y.ToString();
                                CleanupHook();
                            }
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hook error: {ex.Message}");
                CleanupHook();
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        private void CleanupHook()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
            _isPickingLocation = false;

            this.BeginInvoke((MethodInvoker)(() =>
            {
                if (!this.IsDisposed)
                {
                    this.TopMost = false;
                    SetWindowPos(this.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                }
            }));
        }
        private int _lastUpdate;
        private bool _isDisposed;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private LowLevelMouseProc _hookProc;

        // Modified mouse hook setup
        private void button2_Click(object sender, EventArgs e)
        {
            CleanupHook();
            _isPickingLocation = true;
            _lastUpdate = Environment.TickCount;

            this.BeginInvoke((MethodInvoker)(() =>
            {
                this.TopMost = true;
                this.Activate();
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }));

            // Keep the delegate reference alive
            _hookProc = HookCallback;

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _hookProc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void AutoClick()
        {
            while (_isClicking)
            {
                bool isLeftClick = radioLeftClick.Checked;
                bool isMiddleClick = radioButton1.Checked;
                bool isDouble = radioButton3.Checked;
                bool isTriple = radioButton1.Checked;
                bool isCustomPos = radioButton5.Checked;
                int interval = 0;
                int x = 0, y = 0;

                this.Invoke((MethodInvoker)delegate
                {
                    interval = (int)numericInterval.Value;
                    if (isCustomPos)
                    {
                        int.TryParse(textBox2.Text, out x);
                        int.TryParse(textBox1.Text, out y);
                    }
                });

                if (isCustomPos)
                {
                    if (x == 0 && y == 0)
                    {
                        MessageBox.Show("Invalid custom coordinates!");
                        _isClicking = false;
                        this.Invoke((MethodInvoker)delegate { lblStatus.Text = "Status: Stopped"; });
                        return;
                    }
                    Cursor.Position = new Point(x, y);
                }

                if (isLeftClick)
                {
                    _inputSimulator.Mouse.LeftButtonClick();
                    if (isDouble)
                    {
                        Thread.Sleep(20);
                        _inputSimulator.Mouse.LeftButtonClick();
                    }
                    else if (isTriple)
                    {
                        Thread.Sleep(20);
                        _inputSimulator.Mouse.LeftButtonClick();
                        Thread.Sleep(20);
                        _inputSimulator.Mouse.LeftButtonClick();
                    }
                }
                else
                {
                    _inputSimulator.Mouse.RightButtonClick();
                    if (isDouble)
                    {
                        Thread.Sleep(20);
                        _inputSimulator.Mouse.RightButtonClick();
                    }
                    else if (isTriple)
                    {
                        Thread.Sleep(20);
                        _inputSimulator.Mouse.RightButtonClick();
                        Thread.Sleep(20);
                        _inputSimulator.Mouse.RightButtonClick();
                    }
                }

                Thread.Sleep(interval);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (GlobalHotkey.IsHotkeyMessage(m, 1))
            {
                ToggleClicking();
                return;
            }
            base.WndProc(ref m);
        }

        private void ToggleClicking()
        {
            _isClicking = !_isClicking;
            lblStatus.Text = _isClicking ? "Status: Running" : "Status: Stopped";

            if (_isClicking)
            {
                _clickingThread = new Thread(AutoClick);
                _clickingThread.IsBackground = true;
                _clickingThread.Start();
            }
        }
        private void InitializeSettings()
        {
            try
            {
                // Create directory if needed
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                // Create default settings if file doesn't exist
                var settingsPath = Path.Combine(SettingsFolder, SettingsFile);
                if (!File.Exists(settingsPath))
                {
                    SaveSettings();
                }
                else
                {
                    LoadSettings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing settings: {ex.Message}");
            }
        }
        private void LoadSettings()
        {
            try
            {
                var settingsPath = Path.Combine(SettingsFolder, SettingsFile);
                if (File.Exists(settingsPath))
                {
                    var lines = File.ReadAllLines(settingsPath, Encoding.UTF8);
                    if (lines.Length > 0 && Enum.TryParse(lines[0], out Keys loadedKey))
                    {
                        _currentHotkey = loadedKey;
                        label3.Text = $"{_currentHotkey} to toggle autoclicker";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settingsPath = Path.Combine(SettingsFolder, SettingsFile);
                File.WriteAllText(settingsPath, _currentHotkey.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}");
            }
        }
        public void ChangeHotkey(Keys newKey, uint modifiers = 0)
        {
            _globalHotkey?.Dispose();
            _currentHotkey = newKey;
            _currentModifiers = modifiers;
            InitializeGlobalHotkey();
            label3.Text = $"{newKey} to toggle autoclicker";
            SaveSettings(); // Save the new hotkey
        }

        private void openSettings_Click(object sender, EventArgs e)
        {
            var settings = new Form2();
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(
                this.Location.X + (this.Width - settings.Width) / 2,
                this.Location.Y + (this.Height - settings.Height) / 2
            );
            settings.Show();
            SetWindowPos(settings.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _globalHotkey?.Dispose();
            _isClicking = false;
            _clickingThread?.Join();
            base.OnFormClosing(e);
        }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true; // Ignore the input
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}