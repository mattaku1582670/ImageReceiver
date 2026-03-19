# 画像受信アプリ

スマホ（院内端末）から PC（院内端末）へ画像を片方向転送するツールです。
PC 側でローカル HTTP サーバーを起動し、そのアドレスを QR コードで表示します。
スマホで QR を読み取り、ブラウザから画像を送信するだけで転送が完了します。

---

## 動作要件

| 項目 | 内容 |
|------|------|
| OS | Windows（.NET Framework 4.7.2 が必要） |
| ランタイム | .NET Framework 4.7.2 |
| ネットワーク | PC とスマホが同一 LAN に接続されていること |
| ポート | 8080（TCP） |

---

## 使い方

### 1. アプリを起動する

`ImageReceiver.exe` をダブルクリックして起動します。

### 2. サーバーを開始する

「**▶ 開始**」ボタンをクリックします。
PC の LAN IP アドレスが自動取得され、QR コードが表示されます。

### 3. スマホで QR を読み取る

スマホのカメラアプリで QR コードを読み取り、表示された URL をブラウザで開きます。

### 4. 画像を送信する

1. 「📷 画像を選択」をタップして画像を選択（複数選択可）
2. サムネイルプレビューを確認
3. 「**送信する**」をタップ
4. 進捗バーが 100% になったら送信完了

### 5. サーバーを停止する

転送が終わったら「**⏹ 停止**」ボタンをクリックしてサーバーを終了します。

---

## 保存先

受信した画像は以下のフォルダに自動保存されます（起動時に自動作成）。

```
%USERPROFILE%\Desktop\受信画像\
```

ファイル名は `yyyyMMdd_HHmmss_fff_元のファイル名` 形式で保存されます。

「**📂 保存フォルダを開く**」ボタンからすぐにフォルダを開くことができます。

---

## 対応画像形式

`.jpg` `.jpeg` `.png` `.gif` `.bmp` `.webp` `.heic`

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

## ビルド方法

```bash
dotnet restore
dotnet build
```

出力先: `ImageReceiver/bin/Debug/net472/ImageReceiver.exe`

---

## トラブルシューティング

### ポートの開放に失敗する

管理者権限の PowerShell で以下を実行してください。

```powershell
netsh http add urlacl url=http://+:8080/ user=Everyone
```

### ファイアウォールでブロックされる

初回起動時に Windows ファイアウォールの許可ダイアログが表示される場合があります。
表示されない場合は、以下で手動でルールを追加してください。

```powershell
netsh advfirewall firewall add rule name="ImageReceiver" dir=in action=allow protocol=TCP localport=8080
```

### スマホからアクセスできない

- PC とスマホが同じ Wi-Fi（LAN）に接続されているか確認してください
- ウイルス対策ソフトがポート 8080 をブロックしていないか確認してください

---

## 注意事項

- 本ツールは**認証なし**で動作します。外部ネットワークには公開しないでください
- **同一院内 LAN 内のみ**での使用を想定しています
