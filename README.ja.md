# DefenderRestore（Defender 復元ツール）

**dControl（Defender Control）などで無効化・破壊された Microsoft Defender を診断し、正常な状態に戻すポータブルツールです。**

[![Build](https://github.com/4uthen7/defender-restore/actions/workflows/build.yml/badge.svg)](https://github.com/4uthen7/defender-restore/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/4uthen7/defender-restore?display_name=tag)](https://github.com/4uthen7/defender-restore/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)](#必要環境)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)](#必要環境)

> **このツールは Defender を「有効化・修復」するだけです。** リアルタイム保護、クラウド保護、サンプル自動送信、改ざん防止を無効化する機能はありません。それらは意図的に実装していません。

[English README](README.md)

---

## 目次

- [なぜ作ったか](#なぜ作ったか)
- [クイックスタート](#クイックスタート)
- [検査する項目](#検査する項目)
- [修復する内容](#修復する内容)
- [状態確認コマンド](#状態確認コマンド)
- [結果の読み方](#結果の読み方)
- [確認後にすること](#確認後にすること)
- [エラー 1058 と Windows セキュリティ UI](#エラー-1058-と-windows-セキュリティ-ui)
- [dControl から戻せない理由](#dcontrol-から戻せない理由)
- [コマンドライン](#コマンドライン)
- [ビルド](#ビルド)
- [プロジェクト構成](#プロジェクト構成)
- [必要環境](#必要環境)
- [制限と安全上の注意](#制限と安全上の注意)
- [FAQ](#faq)
- [コントリビュート](#コントリビュート)
- [ライセンス](#ライセンス)

---

## なぜ作ったか

**dControl v2.1** は Windows 10 / 初期 Windows 11 向けに作られたツールです。現在の Windows では次のような「途中で止まる」問題が起きます。

- `WinDefend` / `wscsvc` / `SecurityHealthService` を無効化したままにする
- `DisableAntiSpyware` / `DisableAntiVirus` ポリシーを残す
- サービスの ACL、`ImagePath`、Windows セキュリティ アプリの登録を壊す
- その結果、dControl の「有効化」ボタンでも戻せなくなる

その結果、**Defender のエンジンは動いているのに Windows セキュリティの画面にオプションが出ない**、**サービスが無効のままで再起動しても戻らない**、といった状態になります。

DefenderRestore は、それらの層を順番に監査し、正しい順序で修復する小さな WinForms ツールです。Windows 11 26H2 で動作確認し、Windows 10 22H2 でも実行レポートを確認しています。

---

## クイックスタート

1. [最新リリース](https://github.com/4uthen7/defender-restore/releases/latest) から **`DefenderRestore.exe`** をダウンロードします。
2. **ローカル フォルダー**（例: `C:\Users\<名前>\Downloads\DefenderRestore`）に置きます。**OneDrive 上では実行しないでください**（.NET Framework がクラウド同期フォルダーからの実行を拒否することがあります）。
3. `DefenderRestore.exe` をダブルクリックします。
4. 赤または黄色の帯が出たら **「管理者として実行して修復」** を押し、UAC で「はい」を選びます。
5. 修復 → 再診断が自動で進みます。再起動が必要と出たら Windows を再起動します。
6. 下記の「状態確認コマンド」で確認します。

![DefenderRestore の画面](docs/screenshot.png)

### ワンクリック修復

リリース ZIP には `one-click-repair.bat`（`ワンクリック修復.bat`）が入っています。ダブルクリックして UAC を許可すると、

```text
DefenderRestore.exe --repair --yes
```

が実行され、診断 → 管理者昇格 → 修復まで自動で進みます。

---

## 検査する項目

| 分野 | 内容 |
|---|---|
| OS / ビルド / 権限 | Windows のバージョン、Build、UBR、管理者権限 |
| サービス | `WinDefend` / `WdNisSvc` / `SecurityHealthService` / `wscsvc` / `mpssvc` / `Sense` |
| サービス構成 | `Start` 値、依存関係、実行状態（レポートにも出力） |
| ドライバー | `WdFilter` / `WdBoot` / `WdNisDrv` / `mpsdrv` |
| ポリシー | `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender` と、`Policy Manager` などのネストされたサブキー |
| 実設定 | `DisableAntiSpyware` / `DisableAntiVirus` / `DisableRealtimeMonitoring` |
| セキュリティ センター | `Provider\Av` / `Fw` の `Disabled` フラグ |
| IFEO | Defender 実行ファイルへの `Debugger` 乗っ取り |
| レジストリ ACL | Defender 関連キーの明示的な `Deny` エントリ（管理者時） |
| 改ざん防止 | 有効/無効の状態（読み取りのみ。変更はしません） |
| エンジン | WMI `MSFT_MpComputerStatus` のウイルス対策・リアルタイム保護・モード・定義日齢 |
| Windows セキュリティ アプリ | `Microsoft.SecHealthUI` パッケージと `windowsdefender:` プロトコル登録 |
| プラットフォーム | `C:\ProgramData\Microsoft\Windows Defender\Platform` の `MsMpEng.exe` |
| dControl の残骸 | 除外設定、常駐プロセス、残ったフォルダー |
| ファイアウォール | ドメイン / 標準 / パブリック プロファイルの状態 |

---

## 修復する内容

1. Defender を無効化するポリシーを削除（ネストされたサブキーも走査）
2. `DisableAntiSpyware` / `DisableAntiVirus` / `DisableRealtimeMonitoring` を `0` に戻す
3. セキュリティ センターの AV / FW プロバイダーの `Disabled` を解除
4. サービスの前に、レジストリ ACL の `Deny` エントリを除去
5. サービスの開始種類を復元
   - `WinDefend` → 自動
   - `WdNisSvc` / `SecurityHealthService` / `Sense` → 手動
   - `wscsvc` / `mpssvc` → 自動
6. `WinDefend` の `ImagePath` が壊れていれば `MsMpEng.exe` に修復
7. ドライバーの開始種類を復元（`WdFilter` / `WdBoot` = ブート、`WdNisDrv` / `mpsdrv` = 手動）
8. IFEO の `Debugger` を削除
9. dControl の除外設定を削除
10. Windows セキュリティ アプリの登録が壊れていれば再登録
11. ファイアウォールが無効なら有効化
12. サービスを起動
13. 再診断して結果を表示
14. 任意で復元ポイント作成、`MpCmdRun.exe -RestoreDefaults`、定義更新

> **改ざん防止（Tamper Protection）は絶対に無効化しません。** 有効なまま変更がブロックされた場合は、その旨をログに出して終了します。回避しようとはしません。

---

## 状態確認コマンド

**管理者として PowerShell** を開いて実行してください（スタート → ターミナル (管理者) / PowerShell (管理者)）。

### まとめて確認するスクリプト

リポジトリには**読み取り専用**の確認スクリプトがあります。

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\health-check.ps1
```

リリース ZIP では `.\health-check.ps1` にあります。エンジン、設定、サービス、`sc.exe qc`、ポリシー、Windows セキュリティ アプリ、ファイアウォールをまとめて表示し、最後に次の手順を案内します。設定は一切変更しません。

> **cmd では `Get-Service` などの PowerShell コマンドレットは使えません。** cmd の場合は `sc.exe query` / `sc.exe qc` を使ってください。

### 1. エンジンの状態

```powershell
Get-MpComputerStatus |
  Select-Object AntivirusEnabled, RealTimeProtectionEnabled, IsTamperProtected,
                AMRunningMode, AntivirusSignatureAge, AntivirusSignatureVersion
```

正常例:

```text
AntivirusEnabled          : True
RealTimeProtectionEnabled : True
IsTamperProtected         : True
AMRunningMode             : Normal
AntivirusSignatureAge     : 0
```

### 2. Defender の設定

```powershell
Get-MpPreference |
  Select-Object DisableRealtimeMonitoring, DisableIOAVProtection,
                DisableBehaviorMonitoring, DisableScriptScanning
```

正常ならすべて `False` です。

### 3. サービス

```powershell
Get-Service WinDefend, WdNisSvc, SecurityHealthService, wscsvc, mpssvc, Sense -ErrorAction SilentlyContinue |
  Select-Object Name, Status, StartType
```

Windows 10/11 の期待値:

| サービス | 状態 | 開始種類 |
|---|---|---|
| `WinDefend` | Running | Automatic |
| `WdNisSvc` | Running | Manual |
| `SecurityHealthService` | Running | Manual |
| `wscsvc` | Running | Automatic |
| `mpssvc` | Running | Automatic |
| `Sense` | Stopped（一般 PC） | Manual |

### 4. サービスの生の構成

```powershell
sc.exe qc WinDefend
sc.exe qc SecurityHealthService
sc.exe qc wscsvc
```

`START_TYPE` を見ます。

- `2 AUTO_START` — 正常
- `3 DEMAND_START` — 正常
- `4 DISABLED` — 異常。再起動しても起動しません

### 5. ポリシーの残骸

```powershell
reg query "HKLM\SOFTWARE\Policies\Microsoft\Windows Defender" /s
```

正常なら `DisableAntiSpyware=1` や `DisableAntiVirus=1` はありません。空、または `Policy Manager` だけなら正常です。

### 6. Windows セキュリティ アプリ

```powershell
Get-AppxPackage *SecHealthUI* | Select-Object Name, Version, Status
Get-AppxPackage *SecHealthUI* | Select-Object -ExpandProperty InstallLocation
```

`Status : Ok` なら正常です。パッケージが無い/壊れていると、設定画面に「Windows セキュリティを開く」ボタンだけが出ることがあります。

### 7. ファイアウォール

```powershell
Get-NetFirewallProfile | Select-Object Name, Enabled
```

すべて `True` が正常です。

---

## 結果の読み方

| 見え方 | 意味 | 対処 |
|---|---|---|
| `AntivirusEnabled : True` / `RealTimeProtectionEnabled : True` | エンジンは有効 | 何もしなくてOK |
| `RealTimeProtectionEnabled : False` | リアルタイム保護が無効 | ツールを管理者で実行 → 再起動 → 再確認 |
| `IsTamperProtected : True` | 改ざん防止が有効 | そのまま |
| `IsTamperProtected : False` | 改ざん防止が無効 | Windows セキュリティから有効化（ツールでは変更不可） |
| `WinDefend` の `StartType` が `Disabled` | サービスが無効 | ツールで修復。だめなら `sc.exe config` + 再起動 |
| `wscsvc` / `SecurityHealthService` が停止 | Windows セキュリティ UI が空になる | 起動する。1058 なら下記へ |
| `DisableRealtimeMonitoring : True` | 設定で保護が無効 | ツールで修復 |
| ポリシーに `DisableAntiSpyware=1` | dControl 系のポリシー | ツールで修復 |
| `Get-AppxPackage` で見つからない | Windows セキュリティ アプリが無い | 再登録 |
| ファイアウォールが `Enabled : False` | ファイアウォール無効 | ツールで有効化 |

---

## 確認後にすること

### ケース 1 — すべて正常

完了です。Windows Update 後や、Defender 無効化ツールを使った後に再確認してください。

### ケース 2 — エンジン / リアルタイム保護が無効

1. `DefenderRestore.exe` を管理者として実行
2. 「修復を実行」
3. 再起動が必要なら再起動
4. 状態確認コマンドを再実行

### ケース 3 — サービスが `DISABLED`

**管理者の cmd** で:

```cmd
sc.exe config WinDefend start= auto
sc.exe config wscsvc start= auto
sc.exe config SecurityHealthService start= demand
sc.exe config WdNisSvc start= demand
sc.exe config mpssvc start= auto
```

その後、再起動してください。**無効のサービスは再起動だけでは戻りません。**

### ケース 4 — 自動/手動だが停止している

```cmd
sc.exe start wscsvc
sc.exe start SecurityHealthService
sc.exe start WinDefend
```

起動したら Windows セキュリティを開き直します。すぐ止まる場合は、イベント ビューアー → Windows ログ → システム で `Service Control Manager` のエラー（`7000` / `7001` / `7031` / `7034`）を確認します。

### ケース 5 — エラー 1058

[エラー 1058](#エラー-1058-と-windows-セキュリティ-ui) を参照してください。

### ケース 6 — Windows セキュリティにオプションが出ない

1. `wscsvc` と `SecurityHealthService` が **Running** か確認
2. Explorer を再起動、またはサインアウト/サインイン
3. アプリを再登録:

```powershell
Get-AppxPackage -AllUsers *SecHealthUI* |
  ForEach-Object {
    Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $_.InstallLocation 'AppxManifest.xml')
  }
```

4. 再起動

### ケース 7 — 改ざん防止が無効

ツールでは有効化できません。**Windows セキュリティ → ウイルスと脅威の防止 → 設定の管理 → 改ざん防止** から有効にしてください。

### ケース 8 — 会社・学校の PC

グループ ポリシーや Intune が優先されます。`gpresult /h report.html` で適用ポリシーを確認し、管理者に相談してください。

---

## エラー 1058 と Windows セキュリティ UI

`ERROR_SERVICE_DISABLED (1058)` は次の意味です。

> サービスが無効であるか、有効なデバイスが関連付けられていないため、開始できません。

主な原因と対処:

1. **`START_TYPE : 4 DISABLED`**  
   `demand` / `auto` に戻して再起動します。

2. **`START_TYPE` は `2` / `3` なのに 1058 が続く**  
   Service Control Manager（SCM）が古い「無効」状態を保持しています。レジストリ経由で開始種類を変えた場合に起こります。**再起動**してから、もう一度ツールを実行してください。ログに「レジストリ経由 / 再起動で反映」と出ていたら、まさにこのケースです。

3. **依存関係の問題**  
   `sc.exe qc` の `DEPENDENCIES` を確認します。`wscsvc` と `SecurityHealthService` は `RpcSs`、`WdNisSvc` は `WdNisDrv`、`mpssvc` は `mpsdrv` / `bfe` / `nsi` に依存します。依存サービスを先に起動してください。

4. **バイナリの破損**  
   `C:\Windows\System32\SecurityHealthService.exe` と `svchost.exe` の存在を確認し、次を実行します。

   ```cmd
   sfc /scannow
   DISM /Online /Cleanup-Image /RestoreHealth
   ```

5. **改ざん防止 / ポリシー**  
   変更がブロックされた場合はツールがログに出します。改ざん防止を無理に突破しようとしないでください。UI または管理者に相談してください。

> `wscsvc` は**遅延自動開始**です。再起動直後は停止していても、2〜3分で起動することがあります。すぐに判断しないでください。

---

## dControl から戻せない理由

1. **改ざん防止のデッドロック**  
   dControl は変更前に改ざん防止を手動でオフにする必要があります。しかし Defender を無効化すると Windows セキュリティ アプリ/サービスが開けなくなり、戻せなくなります。改ざん防止が ON の間は、dControl のレジストリ書き込みやサービス変更は Windows に拒否されます。

2. **Windows 11 22H2 以降の仕様変更**  
   クライアント版では `DisableAntiSpyware` ポリシーが無視されます。dControl 2.1（2022 年 3 月）はこの変更に未対応で、表示と実際の状態がズレます。

3. **「有効化」が途中で止まる**  
   「ポリシー削除 → サービスを自動に戻す → 起動」の途中で、ACL / 改ざん防止 / 依存関係のどれかが失敗すると、UI はオフのまま残ります。

4. **新しいプラットフォーム構成**  
   Defender 本体は `C:\ProgramData\Microsoft\Windows Defender\Platform\...\MsMpEng.exe` に移りました。dControl が前提にする `C:\Program Files\Windows Defender\MSASCui.exe` は Windows 11 にはありません。

5. **dControl 自体が隔離される**  
   Defender は dControl を HackTool として検知・削除することがあります。実行ファイルが消えると「有効化」ボタン自体が動きません。

---

## コマンドライン

```text
DefenderRestore.exe                     # GUI。診断のみ
DefenderRestore.exe --repair --yes      # 診断 → 管理者昇格 → 修復
DefenderRestore.exe --report <path>     # 診断レポートを書き出して終了
```

`--report` は管理者権限がなくても動きます（ACL 検査はスキップされます）。

---

## ビルド

必要なもの: Windows + .NET Framework 4.x（4.8 推奨）。Visual Studio や NuGet パッケージは不要です。

```powershell
git clone https://github.com/4uthen7/defender-restore.git
cd defender-restore
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

出力: `dist\DefenderRestore.exe`

手動ビルド:

```powershell
$csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
  /win32manifest:src\app.manifest /win32icon:src\app.ico /out:dist\DefenderRestore.exe `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:System.Management.dll /r:System.Security.dll `
  src\Core.cs src\MainForm.cs src\AssemblyInfo.cs
```

---

## プロジェクト構成

```text
.
├─ src/
│  ├─ Core.cs            # 診断・修復ロジック
│  ├─ MainForm.cs        # WinForms UI + CLI エントリ
│  ├─ AssemblyInfo.cs    # バージョン情報
│  ├─ app.manifest       # DPI / OS 互換
│  └─ app.ico            # アイコン
├─ docs/
│  ├─ screenshot.png
│  ├─ security-overview.png
│  └─ threat-page.png
├─ samples/
│  └─ sample_report.txt
├─ tools/
│  ├─ one-click-repair.bat
│  └─ health-check.ps1
├─ build.ps1
├─ README.md
├─ README.ja.md
├─ CHANGELOG.md
├─ SECURITY.md
└─ LICENSE
```

---

## 必要環境

- Windows 7 SP1 / 8 / 8.1 / 10 / 11（Windows 10 22H2 と Windows 11 26H2 で動作確認）
- .NET Framework 4.8（Windows 10 1903 以降 / Windows 11 は標準。古い OS ではインストールが必要）
- 修復時に管理者権限
- ローカル ディスク上での実行（OneDrive / ネットワーク共有は不可）

---

## 制限と安全上の注意

- **保護を無効化する機能はありません。** リアルタイム保護・クラウド保護・サンプル送信・改ざん防止の一括 OFF は対象外です。
- **改ざん防止はプログラムから無効化できません。** UI からのみ変更でき、Windows が自動で戻すことがあります。
- **一部の修復は再起動が必要です。** ドライバーの開始種類や SCM の開始種類変更は再起動で反映されます。
- **署名なしの EXE です。** SmartScreen の警告が出る場合があります。`SHA256.txt` のハッシュを確認してください。
- **管理 PC ではグループ ポリシーが優先されます。** 管理者に相談してください。
- **バックアップを取ってください。** 必要なら「復元ポイントを作成」をオンにしてください。

---

## FAQ

**Defender を無効化するツールですか？**  
いいえ。有効化・修復だけを行います。

**改ざん防止をオフにできますか？**  
できません。意図的に実装していません。

**再起動すれば Windows セキュリティのオプションは戻りますか？**  
サービスの開始種類が `2` / `3` で、アプリ登録が正常なら戻る可能性が高いです。`4 DISABLED` のままなら再起動しても戻りません。[確認後にすること](#確認後にすること) を参照してください。

**OneDrive 上で動かないのはなぜですか？**  
.NET Framework がクラウド同期フォルダーからのアセンブリ実行を拒否するためです。`Downloads` などのローカル フォルダーで実行してください。

**Windows 7 でも使えますか？**  
存在しないコンポーネントはスキップしますが、Windows 7/8.1 は .NET Framework 4.8 が必要で、Microsoft のサポートも終了しています。Windows 10/11 を主対象としています。

---

## コントリビュート

Issue / Pull Request を歓迎します。特に:

- 他の Defender 無効化ツールの残骸パターン
- エラーメッセージのローカライズ
- 未確認の Windows ビルドでのレポート

**Defender の保護や改ざん防止を無効化する機能は受け付けません。** その手の PR は拒否します。

---

## ライセンス

[MIT](LICENSE) © 2026 4uthen7

本プロジェクトは Microsoft および Sordum とは無関係です。Microsoft Defender は Microsoft Corporation の商標です。
