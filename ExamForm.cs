using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CBTExamWindows
{
    public class ExamForm : Form
    {
        private readonly string examUrl;
        private WebView2 webView;
        private Label lblClock, lblBattery, lblNetwork;
        private PictureBox picCamera;
        private Panel pnlCamera;
        private System.Windows.Forms.Timer statusTimer;

        private bool isAuthorizedExit = false;
        private bool cameraRunning = false;
        private double currentZoom = 1.0;
        private IntPtr keyboardHookId = IntPtr.Zero;
        private LowLevelKeyboardProc keyboardProc;

        // --- WINDOWS NATIVE API (ANTI-SCREENSHOT, VOLUME MAX & KEYBOARD HOOK) ---
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private const byte VK_VOLUME_UP = 0xAF;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public ExamForm(string url)
        {
            examUrl = url;

            Text = "CBT EXAM BROWSER - UJIAN BERLANGSUNG";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            BackColor = Color.White;

            InisialisasiLayout();
            PasangBlokirKeyboard();
            MaksimalkanVolumeWindows();
            _ = InisialisasiWebViewAsync();
            MulaiKameraPengawas();
            MulaiMonitorStatus();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Aktifkan Anti-Screenshot & Anti-Screen Recorder di Windows 10/11
            SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
        }

        private void InisialisasiLayout()
        {
            // Bar Atas
            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(13, 71, 161),
                Padding = new Padding(12, 0, 12, 0)
            };
            Controls.Add(topBar);

            // Indikator Kiri (Ikon Jam, Ikon Baterai, Ikon Jaringan)
            FlowLayoutPanel leftPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 340,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 11, 0, 0)
            };

            lblClock = BuatLabelIndikator("🕒 00:00", Color.White);
            lblBattery = BuatLabelIndikator("🔋 100%", Color.FromArgb(165, 214, 167));
            lblNetwork = BuatLabelIndikator("📶 ONLINE", Color.FromArgb(105, 240, 174));

            leftPanel.Controls.Add(lblClock);
            leftPanel.Controls.Add(BuatLabelIndikator(" | ", Color.FromArgb(140, 255, 255, 255)));
            leftPanel.Controls.Add(lblBattery);
            leftPanel.Controls.Add(BuatLabelIndikator(" | ", Color.FromArgb(140, 255, 255, 255)));
            leftPanel.Controls.Add(lblNetwork);
            topBar.Controls.Add(leftPanel);

            // Kontrol Kanan (Refresh & Keluar)
            FlowLayoutPanel rightPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 170,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 0)
            };

            Button btnExit = new Button
            {
                Text = "KELUAR",
                Size = new System.Drawing.Size(85, 32),
                BackColor = Color.FromArgb(211, 47, 47),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnExit.FlatAppearance.BorderSize = 0;
            btnExit.Click += (s, e) =>
            {
                BunyikanAlarmPeringatan();
                TampilkanDialogKeluar();
            };

            Button btnRefresh = new Button
            {
                Text = "🔄",
                Size = new System.Drawing.Size(42, 32),
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += (s, e) => webView?.Reload();

            rightPanel.Controls.Add(btnExit);
            rightPanel.Controls.Add(btnRefresh);
            topBar.Controls.Add(rightPanel);

            // WebView2 Browser Ujian
            webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            Controls.Add(webView);
            webView.BringToFront();

            // Pojok Kiri Bawah: Live Kamera Depan Pengawas
            pnlCamera = new Panel
            {
                Size = new System.Drawing.Size(140, 115),
                BackColor = Color.FromArgb(211, 47, 47),
                Padding = new Padding(2)
            };
            picCamera = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            Label lblCamBadge = new Label
            {
                Text = "● PENGAWAS",
                Dock = DockStyle.Bottom,
                Height = 18,
                BackColor = Color.FromArgb(211, 47, 47),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlCamera.Controls.Add(picCamera);
            pnlCamera.Controls.Add(lblCamBadge);
            Controls.Add(pnlCamera);
            pnlCamera.BringToFront();

            // Pojok Kanan Bawah: Zoom In & Zoom Out
            Button btnZoomIn = BuatTombolZoom("+");
            btnZoomIn.Click += (s, e) =>
            {
                if (currentZoom < 2.0 && webView != null)
                {
                    currentZoom = Math.Round(currentZoom + 0.1, 1);
                    webView.ZoomFactor = currentZoom;
                }
            };

            Button btnZoomOut = BuatTombolZoom("−");
            btnZoomOut.Click += (s, e) =>
            {
                if (currentZoom > 0.6 && webView != null)
                {
                    currentZoom = Math.Round(currentZoom - 0.1, 1);
                    webView.ZoomFactor = currentZoom;
                }
            };

            Controls.Add(btnZoomIn);
            Controls.Add(btnZoomOut);
            btnZoomIn.BringToFront();
            btnZoomOut.BringToFront();

            Resize += (s, e) =>
            {
                pnlCamera.Location = new System.Drawing.Point(16, ClientSize.Height - pnlCamera.Height - 16);
                btnZoomOut.Location = new System.Drawing.Point(ClientSize.Width - 58, ClientSize.Height - 58);
                btnZoomIn.Location = new System.Drawing.Point(ClientSize.Width - 58, ClientSize.Height - 110);
            };
        }

        private Label BuatLabelIndikator(string text, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
        }

        private Button BuatTombolZoom(string text)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new System.Drawing.Size(42, 42),
                BackColor = Color.FromArgb(13, 71, 161),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private async System.Threading.Tasks.Task InisialisasiWebViewAsync()
        {
            try
            {
                // Simpan cache WebView2 di folder Temp agar tidak gagal karena izin folder aplikasi
                string userDataFolder = Path.Combine(Path.GetTempPath(), "CBTExamWebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                // Aktifkan User-Agent khusus: 10403445@
                webView.CoreWebView2.Settings.UserAgent = "10403445@";
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Blokir pembukaan jendela/tab baru
                webView.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;

                // Injeksi Anti-Copy & Anti-Select pada setiap halaman
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "document.addEventListener('DOMContentLoaded', () => {" +
                    "  document.documentElement.style.userSelect = 'none';" +
                    "  document.addEventListener('copy', e => e.preventDefault());" +
                    "  document.addEventListener('cut', e => e.preventDefault());" +
                    "});"
                );

                // Pastikan webView berada di belakang topBar agar docking tidak bertabrakan
                webView.SendToBack();
                webView.Source = new Uri(examUrl);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(
                    "Microsoft Edge WebView2 Runtime belum terinstal di komputer ini!\nSilakan instal WebView2 Runtime terlebih dahulu.",
                    "Komponen Kurang", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal memuat browser ujian: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MulaiKameraPengawas()
        {
            cameraRunning = true;
            Thread camThread = new Thread(() =>
            {
                using VideoCapture capture = new VideoCapture(0);
                if (!capture.IsOpened())
                {
                    try { pnlCamera.Invoke(() => pnlCamera.Visible = false); } catch { }
                    return;
                }

                using Mat frame = new Mat();
                while (cameraRunning)
                {
                    capture.Read(frame);
                    if (frame.Empty()) continue;

                    Cv2.Flip(frame, frame, FlipMode.Y); // Efek cermin kamera depan
                    using Bitmap bmp = BitmapConverter.ToBitmap(frame);
                    try
                    {
                        Bitmap clone = (Bitmap)bmp.Clone();
                        picCamera.Invoke(() =>
                        {
                            picCamera.Image?.Dispose();
                            picCamera.Image = clone;
                        });
                    }
                    catch { }
                    Thread.Sleep(70);
                }
            })
            { IsBackground = true };
            camThread.Start();
        }

        private void MulaiMonitorStatus()
        {
            statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            statusTimer.Tick += (s, e) =>
            {
                lblClock.Text = "🕒 " + DateTime.Now.ToString("HH:mm");

                PowerStatus ps = SystemInformation.PowerStatus;
                int batPct = (int)(ps.BatteryLifePercent * 100);
                if (batPct > 100) batPct = 100;
                lblBattery.Text = $"🔋 {batPct}%";
                lblBattery.ForeColor = batPct <= 20 ? Color.FromArgb(255, 82, 82) : Color.FromArgb(165, 214, 167);

                bool isOnline = NetworkInterface.GetIsNetworkAvailable();
                lblNetwork.Text = isOnline ? "📶 ONLINE" : "❌ OFFLINE";
                lblNetwork.ForeColor = isOnline ? Color.FromArgb(105, 240, 174) : Color.FromArgb(255, 82, 82);

                // Paksa jendela ujian selalu di depan (Anti Floating / Anti Pindah Aplikasi)
                if (!isAuthorizedExit && Form.ActiveForm != this)
                {
                    TopMost = true;
                    Activate();
                }
            };
            statusTimer.Start();
        }

        private void MaksimalkanVolumeWindows()
        {
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    keybd_event(VK_VOLUME_UP, 0, 0, UIntPtr.Zero);
                }
            }
            catch { }
        }

        private void BunyikanAlarmPeringatan()
        {
            MaksimalkanVolumeWindows();
            SystemSounds.Hand.Play();
            Console.Beep(1200, 500);
        }

        // --- BLOKIR TOMBOL KEYBOARD WINDOWS (ALT+TAB, WIN KEY, ESC, PRINTSCREEN, DLL) ---
        private void PasangBlokirKeyboard()
        {
            keyboardProc = HookCallback;
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule;
            keyboardHookId = SetWindowsHookEx(13, keyboardProc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !isAuthorizedExit)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                bool isWinKey = (key == Keys.LWin || key == Keys.RWin);
                bool isAltTab = (key == Keys.Tab && (ModifierKeys & Keys.Alt) != 0);
                bool isAltF4 = (key == Keys.F4 && (ModifierKeys & Keys.Alt) != 0);
                bool isEsc = (key == Keys.Escape);
                bool isPrintScreen = (key == Keys.PrintScreen || key == Keys.Snapshot);
                bool isF12 = (key == Keys.F12);

                if (isWinKey || isAltTab || isAltF4 || isEsc || isPrintScreen || isF12)
                {
                    BunyikanAlarmPeringatan();
                    return (IntPtr)1; // Blokir tombol sepenuhnya
                }
            }
            return CallNextHookEx(keyboardHookId, nCode, wParam, lParam);
        }

        private void TampilkanDialogKeluar()
        {
            using Form exitForm = new Form
            {
                Text = "IZIN KELUAR UJIAN",
                Size = new System.Drawing.Size(380, 185),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };

            Label lbl = new Label
            {
                Text = "Masukkan password pengawas untuk keluar:",
                Location = new System.Drawing.Point(20, 18),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            TextBox txtPass = new TextBox
            {
                Location = new System.Drawing.Point(20, 48),
                Width = 320,
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 11)
            };

            Button btnOk = new Button
            {
                Text = "KELUAR",
                Location = new System.Drawing.Point(230, 95),
                Size = new System.Drawing.Size(110, 36),
                BackColor = Color.FromArgb(211, 47, 47),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            Button btnCancel = new Button
            {
                Text = "LANJUT UJIAN",
                Location = new System.Drawing.Point(100, 95),
                Size = new System.Drawing.Size(120, 36),
                FlatStyle = FlatStyle.Flat
            };

            btnOk.Click += (s, e) =>
            {
                if (txtPass.Text.Trim() == "xyz")
                {
                    isAuthorizedExit = true;
                    cameraRunning = false;
                    statusTimer?.Stop();
                    if (keyboardHookId != IntPtr.Zero) UnhookWindowsHookEx(keyboardHookId);
                    exitForm.Close();
                    Close();
                }
                else
                {
                    BunyikanAlarmPeringatan();
                    MessageBox.Show("PASSWORD SALAH! Akses keluar ditolak.", "Peringatan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            btnCancel.Click += (s, e) => exitForm.Close();

            exitForm.Controls.Add(lbl);
            exitForm.Controls.Add(txtPass);
            exitForm.Controls.Add(btnOk);
            exitForm.Controls.Add(btnCancel);
            exitForm.ShowDialog(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!isAuthorizedExit)
            {
                e.Cancel = true; // Cegah penutupan jendela tanpa password
                BunyikanAlarmPeringatan();
                return;
            }
            cameraRunning = false;
            if (keyboardHookId != IntPtr.Zero) UnhookWindowsHookEx(keyboardHookId);
            base.OnFormClosing(e);
        }
    }
}
