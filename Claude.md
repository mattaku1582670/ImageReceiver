# 画像受信アプリ　実装指示書

## 概要

スマホ（院内端末）から PC（院内端末）へ画像を片方向転送するツール。  
PC 側でローカル HTTP サーバーを起動し、そのアドレスを QR コードで表示。  
スマホで QR を読み取り、ブラウザから画像を POST 送信する。

---

## 技術要件

| 項目 | 内容 |
|------|------|
| プロジェクト形式 | Visual Studio ソリューション（.sln + .csproj） |
| ターゲット | .NET Framework 4.7.2、WinForms（`OutputType: WinExe`） |
| 外部ライブラリ | QRCoder 1.4.3（NuGet）のみ |
| 画像保存先 | `%USERPROFILE%\Desktop\受信画像\`（起動時に自動作成） |
| サーバーポート | 8080 |

---

## ファイル構成

```
ImageReceiver/
├── ImageReceiver.sln
└── ImageReceiver/
    ├── ImageReceiver.csproj
    ├── Program.cs
    └── MainForm.cs
```

---

## 各ファイルの実装内容

### ImageReceiver.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net472</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>disable</Nullable>
    <LangVersion>7.3</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="QRCoder" Version="1.4.3" />
  </ItemGroup>
</Project>
```

### Program.cs

`[STAThread]` で `MainForm` を起動するだけのエントリポイント。

### MainForm.cs

以下の機能をすべて 1 ファイルに実装する。

#### UI 構成

- **ヘッダーパネル**（青帯、アプリタイトル表示）
- **左ペイン**
  - `PictureBox`：QR コード表示（220×220）
  - `Label`：現在の URL 表示
  - `Label`：操作手順のヒント文
- **右ペイン**
  - ステータスラベル（停止中 / 受信待機中）
  - 受信枚数カウンター
  - ログ `ListBox`（新着が上に追加、最大 200 件）
  - ボタン：「▶ 開始」「⏹ 停止」「📂 保存フォルダを開く」

#### HTTP サーバー（`HttpListener`）

- `StartButton_Click` でリスナー起動、バックグラウンドスレッドでループ
- `GET /` → スマホ向けアップロード HTML を返す
- `POST /upload` → multipart/form-data を受信し画像を保存
- `StopButton_Click` でリスナー停止

#### multipart/form-data パーサー

外部ライブラリを使わず自前実装。  
`boundary` でパートを分割し、`Content-Disposition` の `filename` を取得。  
対象拡張子：`.jpg .jpeg .png .gif .bmp .webp .heic`  
保存ファイル名：`yyyyMMdd_HHmmss_fff_元のファイル名`（無効文字はアンダースコアに置換）

#### QR コード生成

```csharp
var gen  = new QRCodeGenerator();
var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
var qr   = new QRCode(data);
Bitmap bmp = qr.GetGraphic(6);
qrPictureBox.Image = bmp;
```

#### IP アドレス取得

`NetworkInterface.GetAllNetworkInterfaces()` を走査し、  
`OperationalStatus.Up` かつ `AddressFamily.InterNetwork` の最初のアドレスを使用。

#### スマホ向け HTML（`GET /` のレスポンス）

インラインで C# 文字列として保持。要件：

- `<meta name="viewport">` でモバイル最適化
- `<input type="file" accept="image/*" multiple>` で複数選択
- サムネイルプレビュー（最大 6 枚 + 残り件数）
- `XMLHttpRequest` で `POST /upload` に送信、進捗バー表示
- 送信完了後は `/`（アップロードページ）にリダイレクト

#### 完了ページ（`POST /upload` のレスポンス）

「✅ 送信完了」を表示し、「続けて送信する」リンクで `/` に戻るシンプルなページ。

---

## 動作フロー

```
1. exe 起動
2. 「▶ 開始」クリック
   └─ HttpListener をポート 8080 で起動
   └─ PC の LAN IP を取得して QR コード生成・表示
3. スマホで QR 読み取り → ブラウザで http://<PC_IP>:8080/ を開く
4. 画像を選択して「送信する」タップ
   └─ multipart POST → PC 側で受信・保存
   └─ ログに「✅ 保存: ファイル名 (XX.X KB)」を追記
   └─ 受信枚数カウンターをインクリメント
5. 「⏹ 停止」でサーバー終了
```

---

## エラーハンドリング

| 状況 | 対応 |
|------|------|
| ポート開放失敗（`HttpListenerException`） | MessageBox で `netsh` コマンドを案内 |
| NIC が見つからない | MessageBox でエラー表示 |
| multipart に画像なし | ログに警告表示 |
| リクエスト処理例外 | ログに記録して 500 を返す |

---

## 注意事項

- PC とスマホが**同一院内 LAN** に接続されていること
- 認証なし。外部ネットワークには公開しないこと
- 初回起動時に Windows ファイアウォールの許可ダイアログが出る場合あり  
  出ない場合は管理者 PowerShell で以下を実行：  
  `netsh http add urlacl url=http://+:8080/ user=Everyone`
