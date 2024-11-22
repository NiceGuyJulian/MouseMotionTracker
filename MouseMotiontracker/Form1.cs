using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseMotiontracker
{
    public partial class Form1 : Form
    {
        private List<Point> recordedPositions = new List<Point>();
        private bool isRecording = false;
        private bool isPlaying = false;
        private ListBox coordinatesListBox;
        private Cursor originalCursor;
        private Cursor redCursor;
        private Label statusLabel;
        private Thread? playbackThread;
        private NumericUpDown delayInput;
        private Label delayLabel;
        private CancellationTokenSource? cancellationTokenSource;

        // Keyboard hook
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private LowLevelKeyboardProc _keyboardProc;
        private static IntPtr _keyboardHookID = IntPtr.Zero;

        // Mouse hook
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private LowLevelMouseProc _mouseProc;
        private static IntPtr _mouseHookID = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }


        

        public Form1()
        {
            InitializeComponent();

            this.TopMost = true;

            // Create and setup ListBox
            coordinatesListBox = new ListBox();
            coordinatesListBox.Location = new Point(10, 10);
            coordinatesListBox.Size = new Size(200, 300);
            this.Controls.Add(coordinatesListBox);

            // Create status label
            statusLabel = new Label();
            statusLabel.Location = new Point(220, 10);
            statusLabel.AutoSize = true;
            statusLabel.Text = "Press SPACE to start recording, C to play recording, ESC to cancel";
            this.Controls.Add(statusLabel);

            // Add delay controls here
            delayLabel = new Label
            {
                Location = new Point(220, 40),
                AutoSize = true,
                Text = "Delay between clicks (ms):"
            };
            this.Controls.Add(delayLabel);

            delayInput = new NumericUpDown
            {
                Location = new Point(220, 60),
                Size = new Size(80, 20),
                Minimum = 100,
                Maximum = 5000,
                Value = 500,
                Increment = 100
            };
            this.Controls.Add(delayInput);


            // Create red cursor
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.Red, 0, 0, 8, 8);
            }
            redCursor = new Cursor(bmp.GetHicon());
            originalCursor = this.Cursor;

            // Set up both hooks
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;
            _keyboardHookID = SetKeyboardHook(_keyboardProc);
            _mouseHookID = SetMouseHook(_mouseProc);
        }

        private IntPtr SetKeyboardHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                if (curModule != null)
                    return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                        GetModuleHandle(curModule.ModuleName), 0);
            }
            return IntPtr.Zero;
        }

        private IntPtr SetMouseHook(LowLevelMouseProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                if (curModule != null)
                    return SetWindowsHookEx(WH_MOUSE_LL, proc,
                        GetModuleHandle(curModule.ModuleName), 0);
            }
            return IntPtr.Zero;
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                HandleKeyPress((Keys)vkCode);
            }
            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN && isRecording)
            {
                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                Point clickPoint = new Point(hookStruct.pt.x, hookStruct.pt.y);

                this.Invoke((MethodInvoker)delegate
                {
                    recordedPositions.Add(clickPoint);
                    coordinatesListBox.Items.Add($"Click {recordedPositions.Count}: X={clickPoint.X}, Y={clickPoint.Y}");
                    statusLabel.Text = $"Recording... Clicks recorded: {recordedPositions.Count}";
                });
            }
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        private void HandleKeyPress(Keys key)
        {
            if (key == Keys.Space)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (!isRecording)
                    {
                        // Start new recording
                        recordedPositions.Clear();
                        coordinatesListBox.Items.Clear();
                        isRecording = true;
                        this.Cursor = redCursor;
                        statusLabel.Text = "Recording... Press SPACE to stop";
                        statusLabel.ForeColor = Color.Red;
                    }
                    else
                    {
                        // End recording
                        isRecording = false;
                        this.Cursor = originalCursor;
                        statusLabel.Text = "Recording stopped. Press SPACE to start new recording, C to play";
                        statusLabel.ForeColor = Color.Black;
                    }
                });
            }
            else if (key == Keys.C && !isRecording && !isPlaying)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    PlaybackRecording();
                });
            }
            else if (key == Keys.Escape && isPlaying)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    StopPlayback();
                });
            }
        }

        private void StopPlayback()
        {
            if (isPlaying && cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                // Handle global hotkeys if needed
            }
            base.WndProc(ref m);
        }

        private async void PlaybackRecording()
        {
            if (recordedPositions.Count == 0)
            {
                MessageBox.Show("No positions recorded!");
                return;
            }

            isPlaying = true;
            this.Enabled = false;
            statusLabel.Text = "Playing back recording... (ESC to cancel)";

            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        foreach (Point position in recordedPositions)
                        {
                            // Check for cancellation
                            if (cancellationTokenSource.Token.IsCancellationRequested)
                                break;

                            Cursor.Position = position;
                            await Task.Delay((int)delayInput.Value / 2, cancellationTokenSource.Token);

                            if (cancellationTokenSource.Token.IsCancellationRequested)
                                break;

                            mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)position.X, (uint)position.Y, 0, 0);
                            await Task.Delay((int)delayInput.Value / 2, cancellationTokenSource.Token);
                        }
                    }
                    finally
                    {
                        this.Invoke(() =>
                        {
                            isPlaying = false;
                            this.Enabled = true;
                            statusLabel.Text = "Playback complete. Press SPACE to start new recording, C to play";
                        });
                    }
                }, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                this.Invoke(() =>
                {
                    isPlaying = false;
                    this.Enabled = true;
                    statusLabel.Text = "Playback cancelled. Press SPACE to start new recording, C to play";
                });
            }
            catch (Exception ex)
            {
                this.Invoke(() =>
                {
                    isPlaying = false;
                    this.Enabled = true;
                    MessageBox.Show($"Error during playback: {ex.Message}");
                });
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(_keyboardHookID);
            UnhookWindowsHookEx(_mouseHookID);
            if (redCursor != null) redCursor.Dispose();
            base.OnFormClosing(e);
        }

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    }
}
