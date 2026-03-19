using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using QRCoder;

namespace ImageReceiver
{
    public class MainForm : Form
    {
        private const int Port = 8080;
        private const int MaxLogItems = 200;

        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic"
        };

        // UI controls
        private Panel headerPanel;
        private PictureBox qrPictureBox;
        private Label urlLabel;
        private Label hintLabel;
        private Label statusLabel;
        private Label countLabel;
        private ListBox logListBox;
        private Button startButton;
        private Button stopButton;
        private Button openFolderButton;

        // Server
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool isRunning;
        private int receivedCount;
        private string saveFolder;

        public MainForm()
        {
            InitializeComponent();
            saveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "受信画像");
        }

        private void InitializeComponent()
        {
            this.Text = "画像受信アプリ";
            this.Size = new Size(780, 560);
            this.MinimumSize = new Size(700, 500);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Header panel
            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(0, 102, 204)
            };
            var titleLabel = new Label
            {
                Text = "画像受信アプリ",
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI", 16, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };
            headerPanel.Controls.Add(titleLabel);

            // Left panel
            var leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 280,
                Padding = new Padding(16, 12, 8, 12)
            };

            qrPictureBox = new PictureBox
            {
                Size = new Size(220, 220),
                Location = new Point(30, 16),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            urlLabel = new Label
            {
                Location = new Point(16, 244),
                Size = new Size(248, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(0, 102, 204),
                Font = new Font("Yu Gothic UI", 9)
            };

            hintLabel = new Label
            {
                Location = new Point(16, 272),
                Size = new Size(248, 100),
                Text = "【操作手順】\n1. 「▶ 開始」をクリック\n2. スマホでQRコードを読み取る\n3. ブラウザで画像を選択して送信\n4. 完了後「⏹ 停止」をクリック",
                Font = new Font("Yu Gothic UI", 9),
                ForeColor = Color.Gray
            };

            leftPanel.Controls.AddRange(new Control[] { qrPictureBox, urlLabel, hintLabel });

            // Right panel
            var rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 12, 16, 12)
            };

            statusLabel = new Label
            {
                Text = "● 停止中",
                ForeColor = Color.Gray,
                Font = new Font("Yu Gothic UI", 12, FontStyle.Bold),
                Location = new Point(8, 8),
                AutoSize = true
            };

            countLabel = new Label
            {
                Text = "受信枚数: 0 枚",
                Font = new Font("Yu Gothic UI", 10),
                Location = new Point(8, 36),
                AutoSize = true
            };

            logListBox = new ListBox
            {
                Location = new Point(8, 64),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Yu Gothic UI", 9),
                HorizontalScrollbar = true
            };

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44
            };

            startButton = new Button
            {
                Text = "▶ 開始",
                Size = new Size(100, 34),
                Location = new Point(8, 4),
                Font = new Font("Yu Gothic UI", 10),
                BackColor = Color.FromArgb(0, 153, 68),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            startButton.Click += StartButton_Click;

            stopButton = new Button
            {
                Text = "⏹ 停止",
                Size = new Size(100, 34),
                Location = new Point(116, 4),
                Font = new Font("Yu Gothic UI", 10),
                BackColor = Color.FromArgb(204, 0, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            stopButton.Click += StopButton_Click;

            openFolderButton = new Button
            {
                Text = "📂 保存フォルダを開く",
                Size = new Size(180, 34),
                Location = new Point(224, 4),
                Font = new Font("Yu Gothic UI", 10),
                FlatStyle = FlatStyle.Flat
            };
            openFolderButton.Click += OpenFolderButton_Click;

            buttonPanel.Controls.AddRange(new Control[] { startButton, stopButton, openFolderButton });

            rightPanel.Controls.AddRange(new Control[] { statusLabel, countLabel, logListBox, buttonPanel });

            this.Controls.Add(rightPanel);
            this.Controls.Add(leftPanel);
            this.Controls.Add(headerPanel);

            // Adjust logListBox size after layout
            this.Load += (s, e) =>
            {
                logListBox.Size = new Size(
                    rightPanel.ClientSize.Width - 16,
                    rightPanel.ClientSize.Height - 64 - buttonPanel.Height - 12
                );
            };
            this.Resize += (s, e) =>
            {
                logListBox.Size = new Size(
                    rightPanel.ClientSize.Width - 16,
                    rightPanel.ClientSize.Height - 64 - buttonPanel.Height - 12
                );
            };
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            string ip = GetLocalIPAddress();
            if (ip == null)
            {
                MessageBox.Show("ネットワークインターフェースが見つかりません。\nLAN に接続されているか確認してください。",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Directory.CreateDirectory(saveFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存フォルダの作成に失敗しました。\n" + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string url = string.Format("http://{0}:{1}/", ip, Port);

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(string.Format("http://+:{0}/", Port));
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                MessageBox.Show(
                    "ポートの開放に失敗しました。\n\n" +
                    "管理者権限の PowerShell で以下を実行してください:\n" +
                    string.Format("netsh http add urlacl url=http://+:{0}/ user=Everyone", Port) +
                    "\n\n詳細: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            isRunning = true;
            receivedCount = 0;
            UpdateCount();

            // Generate QR code
            var gen = new QRCodeGenerator();
            var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var qr = new QRCode(data);
            Bitmap bmp = qr.GetGraphic(6);
            qrPictureBox.Image = bmp;
            urlLabel.Text = url;

            statusLabel.Text = "● 受信待機中";
            statusLabel.ForeColor = Color.FromArgb(0, 153, 68);
            startButton.Enabled = false;
            stopButton.Enabled = true;

            AddLog("サーバーを開始しました: " + url);

            listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true
            };
            listenerThread.Start();
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopServer();
        }

        private void StopServer()
        {
            isRunning = false;

            try
            {
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                    listener = null;
                }
            }
            catch { }

            statusLabel.Text = "● 停止中";
            statusLabel.ForeColor = Color.Gray;
            startButton.Enabled = true;
            stopButton.Enabled = false;
            qrPictureBox.Image = null;
            urlLabel.Text = "";

            AddLog("サーバーを停止しました。");
        }

        private void OpenFolderButton_Click(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(saveFolder);
                System.Diagnostics.Process.Start("explorer.exe", saveFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show("フォルダを開けませんでした。\n" + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ListenLoop()
        {
            while (isRunning)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch
                {
                    break;
                }

                try
                {
                    HandleRequest(context);
                }
                catch (Exception ex)
                {
                    try
                    {
                        this.Invoke(new Action(() => AddLog("⚠ エラー: " + ex.Message)));
                        SendResponse(context.Response, 500, "Internal Server Error");
                    }
                    catch { }
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/")
            {
                string html = GetUploadHtml();
                SendResponse(response, 200, html, "text/html; charset=utf-8");
            }
            else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/upload")
            {
                HandleUpload(request, response);
            }
            else
            {
                SendResponse(response, 404, "Not Found");
            }
        }

        private void HandleUpload(HttpListenerRequest request, HttpListenerResponse response)
        {
            string contentType = request.ContentType;
            if (contentType == null || !contentType.Contains("multipart/form-data"))
            {
                SendResponse(response, 400, "Bad Request");
                return;
            }

            // Extract boundary
            string boundary = null;
            string[] parts = contentType.Split(';');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("boundary="))
                {
                    boundary = trimmed.Substring("boundary=".Length).Trim('"');
                    break;
                }
            }

            if (boundary == null)
            {
                SendResponse(response, 400, "Bad Request: no boundary");
                return;
            }

            // Read entire body
            byte[] bodyBytes;
            using (var ms = new MemoryStream())
            {
                request.InputStream.CopyTo(ms);
                bodyBytes = ms.ToArray();
            }

            // Parse multipart
            byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
            byte[] endBoundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "--");

            var fileParts = ParseMultipart(bodyBytes, boundaryBytes, endBoundaryBytes);

            int savedCount = 0;
            foreach (var filePart in fileParts)
            {
                string ext = Path.GetExtension(filePart.FileName);
                if (!AllowedExtensions.Contains(ext))
                {
                    continue;
                }

                string safeFileName = SanitizeFileName(filePart.FileName);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string saveName = timestamp + "_" + safeFileName;
                string savePath = Path.Combine(saveFolder, saveName);

                File.WriteAllBytes(savePath, filePart.Data);
                savedCount++;

                double sizeKB = filePart.Data.Length / 1024.0;
                string logMsg = string.Format("✅ 保存: {0} ({1:F1} KB)", saveName, sizeKB);
                this.Invoke(new Action(() =>
                {
                    receivedCount++;
                    UpdateCount();
                    AddLog(logMsg);
                }));
            }

            if (savedCount == 0)
            {
                this.Invoke(new Action(() => AddLog("⚠ 画像ファイルが含まれていませんでした。")));
            }

            string resultHtml = GetCompletionHtml(savedCount);
            SendResponse(response, 200, resultHtml, "text/html; charset=utf-8");
        }

        private List<FilePart> ParseMultipart(byte[] body, byte[] boundaryBytes, byte[] endBoundaryBytes)
        {
            var result = new List<FilePart>();
            var positions = FindAllOccurrences(body, boundaryBytes);

            for (int i = 0; i < positions.Count - 1; i++)
            {
                int partStart = positions[i] + boundaryBytes.Length;
                int partEnd = positions[i + 1];

                // Skip \r\n after boundary
                if (partStart < body.Length - 1 && body[partStart] == 0x0D && body[partStart + 1] == 0x0A)
                {
                    partStart += 2;
                }

                // Find header/body separator (\r\n\r\n)
                int headerEnd = FindSequence(body, new byte[] { 0x0D, 0x0A, 0x0D, 0x0A }, partStart, partEnd);
                if (headerEnd < 0) continue;

                string headers = Encoding.UTF8.GetString(body, partStart, headerEnd - partStart);
                int bodyStart = headerEnd + 4;

                // Remove trailing \r\n before next boundary
                int bodyEnd = partEnd;
                if (bodyEnd >= 2 && body[bodyEnd - 2] == 0x0D && body[bodyEnd - 1] == 0x0A)
                {
                    bodyEnd -= 2;
                }

                // Extract filename from Content-Disposition
                string filename = ExtractFilename(headers);
                if (filename == null) continue;

                int dataLength = bodyEnd - bodyStart;
                if (dataLength <= 0) continue;

                byte[] data = new byte[dataLength];
                Array.Copy(body, bodyStart, data, 0, dataLength);

                result.Add(new FilePart { FileName = filename, Data = data });
            }

            return result;
        }

        private string ExtractFilename(string headers)
        {
            foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                {
                    // Look for filename="..."
                    int idx = line.IndexOf("filename=\"");
                    if (idx >= 0)
                    {
                        idx += "filename=\"".Length;
                        int endIdx = line.IndexOf('"', idx);
                        if (endIdx > idx)
                        {
                            return line.Substring(idx, endIdx - idx);
                        }
                    }
                }
            }
            return null;
        }

        private List<int> FindAllOccurrences(byte[] data, byte[] pattern)
        {
            var positions = new List<int>();
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    positions.Add(i);
                }
            }
            return positions;
        }

        private int FindSequence(byte[] data, byte[] pattern, int start, int end)
        {
            for (int i = start; i <= end - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(fileName);
            foreach (char c in invalidChars)
            {
                sb.Replace(c, '_');
            }
            return sb.ToString();
        }

        private void SendResponse(HttpListenerResponse response, int statusCode, string body, string contentType = "text/plain; charset=utf-8")
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            byte[] buffer = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private void AddLog(string message)
        {
            string entry = DateTime.Now.ToString("HH:mm:ss") + " " + message;
            logListBox.Items.Insert(0, entry);
            while (logListBox.Items.Count > MaxLogItems)
            {
                logListBox.Items.RemoveAt(logListBox.Items.Count - 1);
            }
        }

        private void UpdateCount()
        {
            countLabel.Text = string.Format("受信枚数: {0} 枚", receivedCount);
        }

        private string GetLocalIPAddress()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
            return null;
        }

        private string GetUploadHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""ja"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, user-scalable=no"">
<title>画像送信</title>
<style>
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: -apple-system, BlinkMacSystemFont, sans-serif; background: #f0f2f5; color: #333; }
.header { background: #0066cc; color: #fff; padding: 16px; text-align: center; font-size: 20px; font-weight: bold; }
.container { padding: 16px; max-width: 480px; margin: 0 auto; }
.upload-area { background: #fff; border: 2px dashed #ccc; border-radius: 12px; padding: 32px 16px; text-align: center; margin-bottom: 16px; }
.upload-area.has-files { border-color: #0066cc; border-style: solid; }
.file-input-label { display: inline-block; background: #0066cc; color: #fff; padding: 12px 32px; border-radius: 8px; font-size: 16px; cursor: pointer; }
.file-input { display: none; }
.preview-area { display: flex; flex-wrap: wrap; gap: 8px; margin: 16px 0; justify-content: center; }
.preview-item { width: 80px; height: 80px; border-radius: 8px; object-fit: cover; border: 1px solid #ddd; }
.more-count { display: flex; align-items: center; justify-content: center; width: 80px; height: 80px; border-radius: 8px; background: #e0e0e0; font-size: 14px; color: #666; }
.file-count { margin: 8px 0; color: #666; font-size: 14px; }
.send-btn { display: block; width: 100%; padding: 14px; background: #00994d; color: #fff; border: none; border-radius: 8px; font-size: 18px; font-weight: bold; cursor: pointer; }
.send-btn:disabled { background: #ccc; }
.progress-container { display: none; margin: 16px 0; }
.progress-bar { width: 100%; height: 24px; border-radius: 12px; background: #e0e0e0; overflow: hidden; }
.progress-fill { height: 100%; background: #0066cc; width: 0%; transition: width 0.2s; border-radius: 12px; }
.progress-text { text-align: center; margin-top: 8px; font-size: 14px; color: #666; }
</style>
</head>
<body>
<div class=""header"">画像送信</div>
<div class=""container"">
  <div class=""upload-area"" id=""uploadArea"">
    <label class=""file-input-label"">
      📷 画像を選択
      <input type=""file"" class=""file-input"" id=""fileInput"" accept=""image/*"" multiple>
    </label>
    <div class=""file-count"" id=""fileCount""></div>
    <div class=""preview-area"" id=""previewArea""></div>
  </div>
  <button class=""send-btn"" id=""sendBtn"" disabled>送信する</button>
  <div class=""progress-container"" id=""progressContainer"">
    <div class=""progress-bar""><div class=""progress-fill"" id=""progressFill""></div></div>
    <div class=""progress-text"" id=""progressText"">送信中...</div>
  </div>
</div>
<script>
var fileInput = document.getElementById('fileInput');
var sendBtn = document.getElementById('sendBtn');
var previewArea = document.getElementById('previewArea');
var uploadArea = document.getElementById('uploadArea');
var fileCount = document.getElementById('fileCount');
var progressContainer = document.getElementById('progressContainer');
var progressFill = document.getElementById('progressFill');
var progressText = document.getElementById('progressText');

fileInput.addEventListener('change', function() {
    var files = fileInput.files;
    previewArea.innerHTML = '';
    if (files.length === 0) {
        sendBtn.disabled = true;
        uploadArea.classList.remove('has-files');
        fileCount.textContent = '';
        return;
    }
    sendBtn.disabled = false;
    uploadArea.classList.add('has-files');
    fileCount.textContent = files.length + ' 件選択中';
    var maxPreview = 6;
    for (var i = 0; i < Math.min(files.length, maxPreview); i++) {
        (function(file) {
            var reader = new FileReader();
            reader.onload = function(e) {
                var img = document.createElement('img');
                img.className = 'preview-item';
                img.src = e.target.result;
                previewArea.appendChild(img);
            };
            reader.readAsDataURL(file);
        })(files[i]);
    }
    if (files.length > maxPreview) {
        var more = document.createElement('div');
        more.className = 'more-count';
        more.textContent = '+' + (files.length - maxPreview) + '件';
        previewArea.appendChild(more);
    }
});

sendBtn.addEventListener('click', function() {
    var files = fileInput.files;
    if (files.length === 0) return;
    var formData = new FormData();
    for (var i = 0; i < files.length; i++) {
        formData.append('images', files[i], files[i].name);
    }
    sendBtn.disabled = true;
    progressContainer.style.display = 'block';
    progressFill.style.width = '0%';
    progressText.textContent = '送信中...';

    var xhr = new XMLHttpRequest();
    xhr.open('POST', '/upload', true);
    xhr.upload.addEventListener('progress', function(e) {
        if (e.lengthComputable) {
            var pct = Math.round((e.loaded / e.total) * 100);
            progressFill.style.width = pct + '%';
            progressText.textContent = pct + '% 送信中...';
        }
    });
    xhr.onload = function() {
        if (xhr.status === 200) {
            progressFill.style.width = '100%';
            progressText.textContent = '送信完了！';
            setTimeout(function() { window.location.href = '/'; }, 1500);
        } else {
            progressText.textContent = 'エラーが発生しました。';
            sendBtn.disabled = false;
        }
    };
    xhr.onerror = function() {
        progressText.textContent = '通信エラーが発生しました。';
        sendBtn.disabled = false;
    };
    xhr.send(formData);
});
</script>
</body>
</html>";
        }

        private string GetCompletionHtml(int count)
        {
            return string.Format(@"<!DOCTYPE html>
<html lang=""ja"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, user-scalable=no"">
<title>送信完了</title>
<style>
* {{ box-sizing: border-box; margin: 0; padding: 0; }}
body {{ font-family: -apple-system, BlinkMacSystemFont, sans-serif; background: #f0f2f5; color: #333; }}
.header {{ background: #0066cc; color: #fff; padding: 16px; text-align: center; font-size: 20px; font-weight: bold; }}
.container {{ padding: 32px 16px; max-width: 480px; margin: 0 auto; text-align: center; }}
.check {{ font-size: 64px; margin-bottom: 16px; }}
.message {{ font-size: 22px; font-weight: bold; margin-bottom: 8px; }}
.detail {{ color: #666; margin-bottom: 32px; }}
.link {{ display: inline-block; background: #0066cc; color: #fff; padding: 12px 32px; border-radius: 8px; font-size: 16px; text-decoration: none; }}
</style>
</head>
<body>
<div class=""header"">画像送信</div>
<div class=""container"">
  <div class=""check"">✅</div>
  <div class=""message"">送信完了</div>
  <div class=""detail"">{0} 枚の画像を送信しました</div>
  <a class=""link"" href=""/"">続けて送信する</a>
</div>
</body>
</html>", count);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isRunning)
            {
                StopServer();
            }
            base.OnFormClosing(e);
        }

        private class FilePart
        {
            public string FileName { get; set; }
            public byte[] Data { get; set; }
        }
    }
}
