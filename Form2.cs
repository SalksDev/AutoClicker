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
            this.KeyPreview = true; // Ensure the form captures key events
        }

        private void button1_Click(object sender, EventArgs e)
        {
            _isRecordingKey = true;
            label1.Text = "Press any key...";
            this.Focus(); // Ensure the form has focus to capture keys
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_isRecordingKey)
            {
                _isRecordingKey = false;
                e.SuppressKeyPress = true; // Prevent key from being processed twice

                // Capture modifiers (Ctrl, Shift, Alt)
                uint modifiers = 0;
                if (e.Control) modifiers |= 0x0002;
                if (e.Shift) modifiers |= 0x0004;
                if (e.Alt) modifiers |= 0x0001;

                label1.Text = $"Hotkey set to: {e.KeyCode}";

                // Update the hotkey in Form1
                Form1 mainForm = Application.OpenForms["Form1"] as Form1;
                if (mainForm != null)
                {
                    mainForm.ChangeHotkey(e.KeyCode, modifiers);
                }
            }

            base.OnKeyDown(e);
        }
    }
}