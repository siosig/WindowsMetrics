# WindowsMetrics 設計

## 構成

ソリューション（`WindowsMetrics.slnx`）は 2 つのプロジェクトからなる。

- `WindowsMetrics`: WPF アプリ本体（WinForms の `NotifyIcon` をトレイアイコンに併用する）。
- `WindowsMetrics.Tests`: xUnit のテスト。

`WindowsMetrics` の主なフォルダの役割は次のとおり。

| フォルダ | 役割 |
| --- | --- |
| `Services/` | 設定（`AppSettings`）、パース（`MetricsParser`・`Parsing/`）、取得（`Scraping/`）、`MetricsChannel`、トレイアイコン、ウィンドウ位置、アイコン資源（`AppIconResource`） |
| `ViewModels/` | 画面の状態（`MainViewModel`） |
| `Views/` | セクションごとの UserControl（`Sections/`）、グラフ描画（`Controls/`）、値の変換（`Converters/`） |
| `Models/` | スナップショット（`Display/MetricsSnapshot`）、推移データ（`Trends/`）、プロバイダーの状態など |

起動は `App.Application_Startup` が行う。単一インスタンスのミューテックスを取り、Serilog を設定し、Generic Host（`Host.CreateApplicationBuilder`）に `AppSettings`・`MetricsChannel`・`MetricsParser`・`MainViewModel`・`MainWindow`・`PrometheusScraper`・`ScrapeHostedService` を登録して開始する。

データの流れは次のとおり。

1. `ScrapeHostedService`（`BackgroundService`）が取得先（`ScrapeTarget`）ごとに独立した `PeriodicTimer` のループを回し、`PrometheusScraper` で取得する。
2. 共有の `MetricsParser` で `MetricsSnapshot` にし、成功・失敗のどちらでも `MetricsChannel` の `Writer` に書く。失敗時も最後の値を引き継いだスナップショットを出すので、UI は空にならない。
3. `MainViewModel` が `MetricsChannel.Reader.ReadAllAsync` で読み、UI スレッドで画面の状態を更新する。`MetricsChannel` は読み手 1 つの無制限チャネルである。

## ランタイムと SDK

- ランタイムは .NET 11、言語は C# 15。両プロジェクトとも `net11.0-windows` を対象にし、`LangVersion` は指定せず TFM の既定に任せる（TFM と言語版の対応を二重に管理しないため）。
- `global.json` は `version` を `11.0.100`、`rollForward` を `latestFeature`、`allowPrerelease` を `false` にしている。`latestFeature` は 11.0.1xx から 11.0.9xx までを許し 12 以降へは進まない。主版 11 を示しつつ、パッチや feature band の更新で `global.json` を書き換えずに済む。`latestMajor` は SDK 12 でも通ってしまい主版を示せず、`disable` は SDK の更新のたびに書き換えが要る。
- 対応する SDK が無い環境では、`dotnet build` が「A compatible .NET SDK was not found」と要求した版・`global.json` の場所を出して止まる。

### 例外の記録

ユーザーの .NET ルールは .NET 10（LTS）を必須とする。本プロジェクトは 2026-11-13 のユーザーの明示的な依頼により、このルールの例外として .NET 11（STS）を採った。STS のため、次の LTS（.NET 12）への移行が必要になる。

## 公開設定とその理由

`WindowsMetrics/WindowsMetrics.csproj` の公開設定は次のとおり。

| プロパティ | 値 | 理由 |
| --- | --- | --- |
| `PublishRuntimeIdentifier` | `win-x64` | 対象 RID。素の `RuntimeIdentifier` は `dotnet build` にも効くので使わない。 |
| `PublishSelfContained` | `true` | .NET ランタイムを同梱し、利用者に追加インストールを求めない。素の `SelfContained` は使わない（上と同じ理由）。 |
| `PublishSingleFile` | `true` | 配布物を 1 つの exe にする。 |
| `EnableCompressionInSingleFile` | `true` | 単一ファイル内のアセンブリを圧縮してサイズを抑える。 |
| `IncludeNativeLibrariesForSelfExtract` | `true` | ネイティブライブラリも exe に含める。 |
| `IncludeAllContentForSelfExtract` | `true` | .NET 11 では、これが無いと起動時に WPF の混在モード DLL（`DirectWriteForwarder.dll`）を単一ファイルから読めず `BadImageFormatException` で落ちる（.NET 11 SDK 11.0.100 で確認）。初回起動時に一時ディレクトリへ展開する。 |
| `PublishTrimmed` | `false` | WinForms は COM マーシャリングに依存し、トリミングに対して安全でない。 |
| `PublishReadyToRun` | `true` | トリミングなしで起動を速くする。 |
| `CopyOutputSymbolsToPublishDirectory` | `false` | `.pdb` を公開出力に入れない。`bin/` のビルド出力には残るので、クラッシュ解析用のシンボルは手元に残せる。`DebugType` は既定のまま。 |

これで `dotnet publish` の出力は `WindowsMetrics.exe` の 1 ファイルだけになる。

## アイコンの埋め込み

`Assets\app.ico` は `EmbeddedResource`（`LogicalName="WindowsMetrics.Assets.app.ico"`）として exe に埋め込み、`AppIconResource.OpenStream()` で読む。トレイ（`new Icon(Stream)`）とウィンドウ（`BitmapFrame.Create(Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)`）の両方がこの経路を使う。`OnLoad` で読み込み時にデコードを終えるので、ストリームは `using` で閉じてよい。

この方式を選んだ理由は次のとおり。

- マニフェストリソースはアセンブリ自体に含まれるので、単一ファイルの展開先やファイルの有無に依存しない。
- XAML の相対 pack URI は、`Assembly.Location` が空になる単一ファイルでは `InitializeComponent()` が落ちる。この経路を避けられる。
- WinForms の `Icon` と WPF の `BitmapFrame` がどちらもストリームから作れるので、読み込みの経路を 1 つにできる。

`ExcludeFromSingleFile` で隣接ファイルとして置く方式は、公開出力が exe 1 つにならないのでやめた。`IncludeAllContentForSelfExtract` による自己展開は、展開先への書き込みが要り、書き込めない環境で起動に失敗する恐れが増えるので採らなかった。`ApplicationIcon`（exe のファイルアイコン）は従来どおり残す。

## .NET 11 の挙動変更への追従

.NET 11 では、`BackgroundService` の `ExecuteAsync` が失敗していると `IHost.StopAsync` がその例外を再スローする。`ScrapeHostedService` は `BackgroundService` で、`App.Application_Exit`（`async void`）が `StopAsync` を await するので、何もしないと終了時に未処理例外になり、ミューテックスの解放とログのフラッシュが飛ばされる。

そのため `App.Application_Exit` は `StopAsync` の await を `try`/`catch (Exception)` で囲み、`Log.Warning` に記録して後始末（ミューテックスの解放・`Log.CloseAndFlushAsync`）を続ける。失敗そのものはホストが発生時に記録済みである。

出典: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/ihost-runasync-stopasync-throw-backgroundservice-failure

## テスト方針

- `PublishConfigurationTests` が、公開設定の各プロパティの値、素の `RuntimeIdentifier`・`SelfContained` が未設定であること、アイコンが `Content` でなく `EmbeddedResource`（`LogicalName` が `AppIconResource.ResourceName` と一致）であること、両プロジェクトの TFM が `net11.0-windows` であること、.NET に連動するパッケージ（`System.Management`・`Microsoft.Extensions.Hosting`・`Microsoft.Extensions.Http`）が 11.x であること、`global.json` が主版 11・`latestFeature`・`allowPrerelease=false` であることを固定する。
- `AppIconResourceTests` が、埋め込みアイコンがアセンブリから読めることと、そのバイト列がリポジトリの `Assets\app.ico` と一致することを固定する。
