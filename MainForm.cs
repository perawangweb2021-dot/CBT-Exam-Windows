using System;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ZXing;
using ZXing.Windows.Compatibility;

namespace CBTExamWindows
{
    public class MainForm : Form
    {
        private PictureBox picLogo;

        public MainForm()
        {
            Text = "CBT EXAM BROWSER - SMP NEGERI 1 TUALANG";
            Size = new System.Drawing.Size(480, 640);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(244, 246, 249);

            InisialisasiUI();
            _ = MuatLogoDariUrlAsync();
        }

        private void InisialisasiUI()
        {
            picLogo = new PictureBox
            {
                Size = new System.Drawing.Size(110, 110),
                Location = new System.Drawing.Point((ClientSize.Width - 110) / 2, 28),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            Controls.Add(picLogo);

            Label lblTitle = new Label
            {
                Text = "CBT EXAM BROWSER",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(13, 71, 161),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new System.Drawing.Size(ClientSize.Width, 34),
                Location = new System.Drawing.Point(0, 148)
            };
            Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "SMP NEGERI 1 TUALANG",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new System.Drawing.Size(ClientSize.Width, 24),
                Location = new System.Drawing.Point(0, 182)
            };
            Controls.Add(lblSub);

            int btnWidth = 380;
            int btnX = (ClientSize.Width - btnWidth) / 2;

            // Menu 1: SCAN QR CODE UJIAN
            Button btnScanQr = BuatTombolMenu("📷   SCAN QR CODE UJIAN", btnX, 230, btnWidth,
                Color.FromArgb(13, 71, 161), Color.White);
            btnScanQr.Click += (s, e) => BukaScannerQr();
            Controls.Add(btnScanQr);

            // Menu 2: MASUKKAN LINK UJIAN
            Button btnInputLink = BuatTombolMenu("🔗   MASUKKAN LINK UJIAN", btnX, 305, btnWidth,
                Color.White, Color.FromArgb(13, 71, 161));
            btnInputLink.FlatAppearance.BorderColor = Color.FromArgb(13, 71, 161);
            btnInputLink.FlatAppearance.BorderSize = 2;
            btnInputLink.Click += (s, e) => TampilkanDialogInputLink();
            Controls.Add(btnInputLink);

            // Menu 3: BANTUAN
            Button btnBantuan = BuatTombolMenu("ℹ️   BANTUAN", btnX, 380, btnWidth,
                Color.White, Color.FromArgb(30, 41, 59));
            btnBantuan.FlatAppearance.BorderColor = Color.FromArgb(255, 143, 0);
            btnBantuan.FlatAppearance.BorderSize = 2;
            btnBantuan.Click += (s, e) => TampilkanDialogBantuan();
            Controls.Add(btnBantuan);

            // Footer
            Label lblFooter = new Label
            {
                Text = "Copy 2026 Dibuat oleh TRI SUTRISNO, S.Kom",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new System.Drawing.Size(ClientSize.Width, 30),
                Location = new System.Drawing.Point(0, ClientSize.Height - 42)
            };
            Controls.Add(lblFooter);
        }

        private Button BuatTombolMenu(string text, int x, int y, int width, Color bg, Color fg)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(width, 60),
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(20, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private async Task MuatLogoDariUrlAsync()
        {
            try
            {
                using HttpClient client = new HttpClient();
                var bytes = await client.GetByteArrayAsync("https://smpn1tualang.sch.id/images/aplikasi/logo.png");
                using var ms = new System.IO.MemoryStream(bytes);
                picLogo.Image = Image.FromStream(ms);
            }
            catch { }
        }

        private void BukaScannerQr()
        {
            using Form qrForm = new Form
            {
                Text = "Scan QR Code Ujian",
                Size = new System.Drawing.Size(420, 380),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            PictureBox camBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            Label lblInfo = new Label
            {
                Text = "Arahkan QR Code Ujian dari Guru Pengawas ke Kamera...",
                Dock = DockStyle.Bottom,
                Height = 36,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            qrForm.Controls.Add(camBox);
            qrForm.Controls.Add(lblInfo);

            bool scanning = true;
            string detectedUrl = null;

            Thread camThread = new Thread(() =>
            {
                using VideoCapture capture = new VideoCapture(0);
                if (!capture.IsOpened())
                {
                    qrForm.Invoke(() => MessageBox.Show("Kamera tidak terdeteksi di komputer ini!"));
                    return;
                }

                var reader = new BarcodeReader();
                using Mat frame = new Mat();

                while (scanning)
                {
                    capture.Read(frame);
                    if (frame.Empty()) continue;

                    using Bitmap bmp = BitmapConverter.ToBitmap(frame);
                    var result = reader.Decode(bmp);
                    if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    {
                        detectedUrl = result.Text.Trim();
                        scanning = false;
                        qrForm.Invoke(() => qrForm.Close());
                        break;
                    }

                    try
                    {
                        Bitmap preview = (Bitmap)bmp.Clone();
                        camBox.Invoke(() =>
                        {
                            camBox.Image?.Dispose();
                            camBox.Image = preview;
                        });
                    }
                    catch { }
                    Thread.Sleep(60);
                }
            })
            { IsBackground = true };

            qrForm.FormClosing += (s, e) => scanning = false;
            qrForm.Shown += (s, e) => camThread.Start();
            qrForm.ShowDialog(this);

            if (!string.IsNullOrEmpty(detectedUrl))
            {
                BukaHalamanUjian(detectedUrl);
            }
        }

        private void TampilkanDialogInputLink()
        {
            using Form inputForm = new Form
            {
                Text = "Masukkan Link Ujian",
                Size = new System.Drawing.Size(420, 185),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            TextBox txtUrl = new TextBox
            {
                Location = new System.Drawing.Point(20, 24),
                Width = 360,
                Font = new Font("Segoe UI", 11),
                PlaceholderText = "https://...",
                AutoCompleteMode = AutoCompleteMode.None, // Tanpa history ketikan
                Text = ""
            };

            Button btnMasuk = new Button
            {
                Text = "MASUK UJIAN",
                Location = new System.Drawing.Point(250, 75),
                Size = new System.Drawing.Size(130, 38),
                BackColor = Color.FromArgb(13, 71, 161),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            Button btnBatal = new Button
            {
                Text = "BATAL",
                Location = new System.Drawing.Point(145, 75),
                Size = new System.Drawing.Size(95, 38),
                FlatStyle = FlatStyle.Flat
            };

            btnMasuk.Click += (s, e) =>
            {
                string url = txtUrl.Text.Trim();
                txtUrl.Text = ""; // Hapus ketikan langsung
                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show("Link ujian tidak boleh kosong!", "Peringatan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;

                inputForm.Close();
                BukaHalamanUjian(url);
            };

            btnBatal.Click += (s, e) =>
            {
                txtUrl.Text = "";
                inputForm.Close();
            };

            inputForm.Controls.Add(txtUrl);
            inputForm.Controls.Add(btnMasuk);
            inputForm.Controls.Add(btnBatal);
            inputForm.ShowDialog(this);
        }

        private void TampilkanDialogBantuan()
        {
            string info = "CBT EXAM BROWSER dibuat untuk tujuan mempermudah dalam melaksanakan sistem ujian yang transparan dan jujur.\n\n" +
                          "Aplikasi ini dibuat oleh :\n" +
                          "TRI SUTRISNO, S.Kom\n" +
                          "Guru SMP Negeri 1 Tualang\n" +
                          "trisutrisno79@guru.smp.belajar.id\n" +
                          "+6281276534734";

            MessageBox.Show(info, "Tentang Aplikasi & Bantuan", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BukaHalamanUjian(string url)
        {
            Hide();
            ExamForm examForm = new ExamForm(url);
            examForm.FormClosed += (s, e) => Application.Exit();
            examForm.Show();
        }
    }
}
