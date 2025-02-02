using System;
using System.Windows.Forms;

namespace ReleaseAC
{
    public partial class Form2 : Form
    {
        private bool _isRecordingKey = false;

        public Form2()
        {
            InitializeComponent();
            KeyPreview = true;
        }

        private void Form2_Load(object sender, EventArgs e)
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            var mainForm = GetMainForm();
            if (mainForm == null) return;

            label1.Text = $"Hotkey set to: {mainForm.CurrentHotkey}";
            checkBox1.Checked = mainForm.PauseOnMouseMovement;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            _isRecordingKey = true;
            label1.Text = "Press any key...";
            Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_isRecordingKey)
            {
                _isRecordingKey = false;
                e.SuppressKeyPress = true;

                uint modifiers = 0;
                if (e.Control) modifiers |= 0x0002;
                if (e.Shift) modifiers |= 0x0004;
                if (e.Alt) modifiers |= 0x0001;

                UpdateHotkeyDisplay(e.KeyCode);
                UpdateMainFormHotkey(e.KeyCode, modifiers);
            }
            base.OnKeyDown(e);
        }

        private void UpdateHotkeyDisplay(Keys key)
        {
            label1.Text = $"Hotkey set to: {key}";
        }

        private void UpdateMainFormHotkey(Keys key, uint modifiers)
        {
            var mainForm = GetMainForm();
            mainForm?.ChangeHotkey(key, modifiers);
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            var mainForm = GetMainForm();
            if (mainForm != null)
            {
                mainForm.PauseOnMouseMovement = checkBox1.Checked;
            }
        }

        private Form1 GetMainForm()
        {
            return Application.OpenForms["Form1"] as Form1;
        }
    }
}