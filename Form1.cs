using System;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using WindowsInput;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Reflection;

namespace ReleaseAC
{
    public partial class Form1 : Form
    {
        #region WinAPI Declarations
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_MOUSEMOVE = 0x0200;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const int MOUSE_MOVEMENT_THRESHOLD = 500;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        #endregion

        #region Global Hotkey Implementation
        private class GlobalHotkey : IDisposable
        {
            private const int WM_HOTKEY = 0x0312;
            private readonly IntPtr _handle;
            private readonly int _id;

            [DllImport("user32.dll")]
            private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [DllImport("user32.dll")]
            private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
        #endregion
        #region Fields and Properties
        private readonly InputSimulator _inputSimulator = new InputSimulator();
        private bool _isClicking;
        private Thread _clickingThread;
        private GlobalHotkey _globalHotkey;
        private Keys _currentHotkey = Keys.F6;
        public Keys CurrentHotkey => _currentHotkey;
        private uint _currentModifiers;
        private readonly string _settingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SalkAutoClicker");
        private const string SettingsFile = "settings.dat";

        private int _clicksRemaining;
        private bool _infiniteRepeat = true;
        private bool _pauseOnMovement;
        private Point _lastMousePosition;
        private DateTime _lastMovementTime = DateTime.Now;

        private IntPtr _hookID = IntPtr.Zero;
        private bool _isPickingLocation;
        private LowLevelMouseProc _hookProc;
        private int _lastUpdate;

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
        #endregion

        public Form1()
        {
            InitializeComponent();
            InitializeApplication();
        }

        private void InitializeApplication()
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            InitializeSettings();
            InitializeGlobalHotkey();
            _lastMousePosition = Cursor.Position;

            radioLeftClick.Checked = true;
            radioButton4.Checked = true;
            radioButton2.Checked = true;
            radioButton7.Checked = true;

            button2.Click += PickLocationClick;
            button3.Click += ToggleClicking;
            button4.Click += ToggleClicking;
        }

        #region Core Functionality
        private void AutoClick()
        {
            try
            {
                int desiredTotalClicks = _infiniteRepeat ? int.MaxValue : (int)repeatTimes.Value;
                int clicksPerAction = GetClicksPerAction();
                int interval = _currentInterval;

                int fullActions = desiredTotalClicks / clicksPerAction;
                int remainder = desiredTotalClicks % clicksPerAction;

                int totalActions = remainder == 0 ? fullActions : fullActions + 1;
                _clicksRemaining = totalActions;

                int totalClicksPerformed = 0;

                while (_isClicking && _clicksRemaining > 0)
                {
                    if (ShouldPauseForMovement())
                    {
                        Invoke((MethodInvoker)ToggleClicking);
                        return;
                    }

                    var (x, y) = GetClickCoordinates();
                    MoveCursorIfNeeded(x, y);

                    // Determine clicks for this action
                    int clicksThisAction = (_clicksRemaining == 1 && remainder > 0) ? remainder : clicksPerAction;

                    Stopwatch sw = Stopwatch.StartNew();
                    PerformClick(clicksThisAction); // Pass the number of clicks
                    sw.Stop();

                    totalClicksPerformed += clicksThisAction;
                    _clicksRemaining--;

                    // Stop if we've reached/exceeded the desired clicks
                    if (!_infiniteRepeat && totalClicksPerformed >= desiredTotalClicks)
                    {
                        _clicksRemaining = 0;
                        break;
                    }

                    // Sleep remaining interval
                    int remainingInterval = Math.Max(0, interval - (int)sw.ElapsedMilliseconds);
                    int timeSlept = 0;
                    while (timeSlept < remainingInterval && _isClicking)
                    {
                        int sleepTime = Math.Min(10, remainingInterval - timeSlept);
                        Thread.Sleep(sleepTime);
                        timeSlept += sleepTime;
                    }
                }
            }
            finally
            {
                Invoke((MethodInvoker)(() =>
                {
                    _isClicking = false;
                    UpdateWindowTitle();
                    UpdateButtonStates();
                }));
            }
        }

        private (int x, int y) GetClickCoordinates()
        {
            int x = 0, y = 0;
            if (radioButton5.Checked)
            {
                int.TryParse(textBox2.Text, out x);
                int.TryParse(textBox1.Text, out y);
            }
            return (x, y);
        }
        private int GetClicksPerAction()
        {
            if (radioButton1.Checked) return 3; // Triple click
            else if (radioButton3.Checked) return 2; // Double click
            else return 1; // Single click
        }
        private bool ShouldPauseForMovement()
        {
            return _pauseOnMovement &&
                   (DateTime.Now - _lastMovementTime).TotalMilliseconds < MOUSE_MOVEMENT_THRESHOLD;
        }

        private (int interval, int x, int y) GetClickParameters()
        {
            int x = 0, y = 0;
            if (radioButton5.Checked)
            {
                int.TryParse(textBox2.Text, out x);
                int.TryParse(textBox1.Text, out y);
            }
            return (_currentInterval, x, y); // Use cached interval
        }

        private void MoveCursorIfNeeded(int x, int y)
        {
            if (radioButton5.Checked && (x != 0 || y != 0))
            {
                Cursor.Position = new Point(x, y);
            }
        }

        private void PerformClick(int numberOfClicks)
        {
            if (numberOfClicks < 1 || numberOfClicks > 3)
                throw new ArgumentOutOfRangeException(nameof(numberOfClicks));

            bool isLeftClick = radioLeftClick.Checked;

            for (int i = 0; i < numberOfClicks; i++)
            {
                if (isLeftClick)
                    _inputSimulator.Mouse.LeftButtonClick();
                else
                    _inputSimulator.Mouse.RightButtonClick();

                // Add small delay between clicks in multi-click actions
                if (i < numberOfClicks - 1)
                    Thread.Sleep(5);
            }
        }

        private void PerformLeftClick(bool isDouble, bool isTriple)
        {
            if (isTriple)
            {
                // Triple click with 5ms between clicks
                _inputSimulator.Mouse.LeftButtonClick();
                Thread.Sleep(5);
                _inputSimulator.Mouse.LeftButtonClick();
                Thread.Sleep(5);
                _inputSimulator.Mouse.LeftButtonClick();
            }
            else if (isDouble)
            {
                // Double click with 5ms between clicks
                _inputSimulator.Mouse.LeftButtonClick();
                Thread.Sleep(5);
                _inputSimulator.Mouse.LeftButtonClick();
            }
            else
            {
                // Single click
                _inputSimulator.Mouse.LeftButtonClick();
            }
        }

        private void PerformRightClick(bool isDouble, bool isTriple)
        {
            if (isTriple)
            {
                // Triple click with 5ms between clicks
                _inputSimulator.Mouse.RightButtonClick();
                Thread.Sleep(5);
                _inputSimulator.Mouse.RightButtonClick();
                Thread.Sleep(5);
                _inputSimulator.Mouse.RightButtonClick();
            }
            else if (isDouble)
            {
                // Double click with 5ms between clicks
                _inputSimulator.Mouse.RightButtonClick();
                Thread.Sleep(5);
                _inputSimulator.Mouse.RightButtonClick();
            }
            else
            {
                // Single click
                _inputSimulator.Mouse.RightButtonClick();
            }
        }
        #endregion

        #region UI Management
        private void UpdateWindowTitle()
        {
            Invoke((MethodInvoker)(() =>
                Text = $"AutoClicker {(_isClicking ? "(Running)" : "(Stopped)")}"));
        }

        private void UpdateButtonStates()
        {
            Invoke((MethodInvoker)(() =>
            {
                button3.Enabled = !_isClicking;
                button4.Enabled = _isClicking;
            }));
        }

        private void ToggleClicking(object sender, EventArgs e) => ToggleClicking();

        private void ToggleClicking()
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)ToggleClicking);
                return;
            }

            _isClicking = !_isClicking;
            _infiniteRepeat = radioButton7.Checked; // Assuming radioButton7 is "Infinite" mode

            if (_isClicking)
            {
                if (_pauseOnMovement) StartMouseHook();
                _clickingThread = new Thread(AutoClick) { IsBackground = true };
                _clickingThread.Start();
            }
            else
            {
                CleanupHook();
            }

            UpdateWindowTitle();
            UpdateButtonStates();
        }

        private void StartMouseHook()
        {
            if (_hookID != IntPtr.Zero) return;

            _hookProc = HookCallback;
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _hookProc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void CleanupHook()
        {
            if (_hookID == IntPtr.Zero) return;

            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;

            // Add small delay to prevent hook collision
            Thread.Sleep(50);
        }
        #endregion

        #region Mouse Hook Handling
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return CallNextHookEx(_hookID, nCode, wParam, lParam);

            if (wParam == (IntPtr)WM_MOUSEMOVE)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var currentPos = new Point(hookStruct.pt.X, hookStruct.pt.Y);
                if (currentPos != _lastMousePosition)
                {
                    _lastMovementTime = DateTime.Now;
                    _lastMousePosition = currentPos;
                }
            }

            if (_pauseOnMovement && ShouldPauseForMovement() && _isClicking)
            {
                Invoke((MethodInvoker)ToggleClicking);
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        private int _currentInterval = 100; // Default value
        private void numericInterval_ValueChanged(object sender, EventArgs e)
        {
            _currentInterval = (int)numericInterval.Value;
        }
        // Replace the problematic using declarations in the PickLocationClick method:
        private void PickLocationClick(object sender, EventArgs e)
        {
            CleanupHook();
            _isPickingLocation = true;
            _lastUpdate = Environment.TickCount;

            BeginInvoke((MethodInvoker)(() =>
            {
                TopMost = true;
                Activate();
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }));

            _hookProc = HookCallback;
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }
        #endregion

        #region Settings Management
        private void InitializeGlobalHotkey()
        {
            try
            {
                _globalHotkey = new GlobalHotkey(Handle, 1, _currentModifiers, (uint)_currentHotkey);
                button3.Text = "Start (" + _currentHotkey.ToString() + ")";
                button4.Text = "Stop (" + _currentHotkey.ToString() + ")";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to register hotkey: {ex.Message}");
            }
        }

        private void ChangePauseOnMovement(object sender, EventArgs e)
        {

        }

        private void InitializeSettings()
        {
            try
            {
                Directory.CreateDirectory(_settingsFolder);
                if (!File.Exists(SettingsPath)) SaveSettings();
                else LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing settings: {ex.Message}");
            }
        }

        private string SettingsPath => Path.Combine(_settingsFolder, SettingsFile);

        // Modify SaveSettings and LoadSettings
        public void SaveSettings()
        {
            try
            {
                var settings = new StringBuilder();
                settings.AppendLine(_currentHotkey.ToString());
                settings.AppendLine(_pauseOnMovement.ToString());
                File.WriteAllText(SettingsPath, settings.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;

                var lines = File.ReadAllLines(SettingsPath, Encoding.UTF8);
                if (lines.Length > 0)
                {
                    if (Enum.TryParse(lines[0], out Keys loadedKey))
                    {
                        _currentHotkey = loadedKey;
                    }
                    if (lines.Length > 1 && bool.TryParse(lines[1], out bool pauseOnMove))
                    {
                        _pauseOnMovement = pauseOnMove;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}");
            }
        }

        public void ChangeHotkey(Keys newKey, uint modifiers = 0)
        {
            _globalHotkey?.Dispose();
            _currentHotkey = newKey;
            _currentModifiers = modifiers;
            InitializeGlobalHotkey();
            SaveSettings(); // Ensure this stays here
        }

        public bool PauseOnMouseMovement
        {
            get => _pauseOnMovement;
            set
            {
                _pauseOnMovement = value;
                // Update hook when setting changes
                if (_isClicking)
                {
                    CleanupHook();
                    StartMouseHook();
                }
                SaveSettings();
            }
        }
        #endregion

        #region Event Handlers
        protected override void WndProc(ref Message m)
        {
            if (GlobalHotkey.IsHotkeyMessage(m, 1))
                ToggleClicking();
            else
                base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _globalHotkey?.Dispose();
            _isClicking = false;
            _clickingThread?.Join();
            base.OnFormClosing(e);
        }

        private void OpenSettingsClick(object sender, EventArgs e)
        {
            var settings = new Form2
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(
                    Location.X + (Width - 220) / 2,
                    Location.Y + (Height - 164) / 2)
            };
            settings.Show();
            SetWindowPos(settings.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        private void TextBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar);
        }
        #endregion

        private void Form1_Load(object sender, EventArgs e) { }
    }
}