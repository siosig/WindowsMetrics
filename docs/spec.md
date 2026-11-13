# WindowsMetrics 仕様

## 概要

WindowsMetrics は、Windows のシステムメトリクス（CPU・メモリ・GPU・ディスク・ネットワーク・温度）を常時表示する WPF のデスクトップウィジェットである。メトリクスは windows_exporter と LibreHardwareMonitor が公開する Prometheus 形式のエンドポイントを定期的に取得して表示する。

- 対象 OS: Windows 11 x64
- 対象 CPU: x86-64-v2 以上（Windows 11 の要件を満たす PC は該当する）
- ランタイム: .NET 11（自己完結。別途のインストールは不要）

## 機能一覧

### 表示セクション

メインウィンドウは次のセクションを上から順に並べる（`MainWindow.xaml`）。

| セクション | 内容 |
| --- | --- |
| 日時 | 現在の日時（`DateTimeSection`） |
| カレンダー | 月送りができるカレンダー。見出しのクリックで開閉する（`CalendarSection`） |
| メモリ | 物理メモリとページファイルの使用量・合計・使用率（`MemorySection`） |
| プロセスメモリ | メモリ温度の最小・最大・平均と、メモリ使用量の上位プロセス（`ProcessMemorySection`） |
| CPU | 温度・使用率、最小・最大・平均温度、C1・C2・C3・DPC・割り込みの比率、CPU 使用率の上位プロセス（`CpuSection`） |
| GPU | 温度・使用率、最小・最大・平均温度、GPU 使用率の上位プロセス（`GpuSection`） |
| 温度 | マザーボード各部（SYS・CPU・MOS・PCH・Sock・PCIe・M2）の温度（`TemperatureSection`） |
| ディスク | ドライブごとの温度・読み書き速度・IOPS・使用率（`DiskSection`） |
| ネットワーク（合計） | インターフェース名と受信・送信速度、トレンドのグラフ（`NetworkTotalSection`） |
| ネットワーク（プロセス別） | 送受信量の上位プロセス（`NetworkProcessSection`） |

### トレイアイコン

`TrayIconService` が通知領域にアイコン（ツールチップは `WindowsMetrics`）を表示する。

- 右クリックメニュー: `Settings`（ウィンドウを表示して設定パネルを開閉する）、`Exit`（アプリを終了する）。
- ダブルクリック: ウィンドウを表示して前面に出す。
- ウィンドウの閉じるボタンは終了せず、ウィンドウを隠す（`MainWindow.CloseBtn_Click`）。

### 設定

設定パネルでフォントとフォントサイズを変更できる。サイズは 8〜24 に丸める。カレンダーの開閉状態も保存される。ウィンドウの位置と幅は、ウィンドウを閉じるときに保存される。起動時にこれらを復元する。

### 単一インスタンス

名前付き Mutex（`WindowsMetrics_SingleInstance`）で多重起動を防ぐ。2 つ目以降の起動はすぐに終了する（`App.xaml.cs`）。

### ログ

Serilog でファイルに記録する。最小レベルは Information（`Microsoft`・`System` の名前空間は Warning）。日ごとにファイルを分け、1 ファイル 1 MiB で分割し、5 ファイルまで保持する。設定の読み込みで既定値に戻した値があれば、起動時に Warning として記録する。

## メトリクスの取得先

| 取得先 | 既定の URL | 既定の取得間隔 |
| --- | --- | --- |
| windows_exporter | `http://127.0.0.1:9182/metrics` | 1000 ms |
| LibreHardwareMonitor | `http://127.0.0.1:8085/metrics` | 5000 ms |

- 既定値は `ScrapeTarget.DefaultWindowsExporterUrl`・`DefaultLhmUrl` などで定める。
- URL と取得間隔は、レジストリの値で変更できる（`AppSettings.WindowsExporterUrl`・`LhmUrl`・`WindowsExporterIntervalMs`・`LhmIntervalMs`）。設定パネルからは変更できない。
- URL は絶対の http／https でなければならず、不正なら既定値を使って警告を記録する。取得間隔は 250〜60000 ms に丸める。
- 取得は `ScrapeHostedService` が行い、プロキシは使わない（取得先は常にループバック）。

## 配布物

- 配布物は `WindowsMetrics.exe` の 1 ファイルである。.NET 11 のランタイムを含む自己完結型で、アイコンは exe に埋め込む。`.pdb` は公開しない。
- 配布用の zip 名は `WindowsMetrics-v<版>-win-x64.zip` とする。
- 公開コマンド: `dotnet publish WindowsMetrics/WindowsMetrics.csproj -c Release -o publish/win-x64`

## 保存先

### 設定

設定はレジストリの `HKEY_CURRENT_USER\Software\WindowsMetrics`（`AppSettings.DefaultRegPath`）に保存する。`AppSettings.Save()` が書く値は次のとおり。

| 値の名前 | 型 | 内容 |
| --- | --- | --- |
| `FontFamilyName` | REG_SZ | フォント名 |
| `FontSize` | REG_SZ | フォントサイズ（インバリアントカルチャの文字列） |
| `IsCalendarExpanded` | REG_DWORD | カレンダーの開閉（1 が開） |

同じキーに `WindowPositionService.Save()` が `Left`・`Top`・`Width`（いずれも REG_DWORD）を書く。`AppSettings.Load()` は、取得先の URL と取得間隔（`WindowsExporterUrl`・`LhmUrl`・`WindowsExporterIntervalMs`・`LhmIntervalMs`）も同じキーから読む。アプリはこれらを書かない。

### ログ

ログは `%LOCALAPPDATA%\WindowsMetrics\logs\`（ファイル名は `windowsmetrics-<日付>.log`）に出力する。
