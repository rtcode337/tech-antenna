# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## このリポジトリの目的

技術情報を自動収集し、**記事・イベント・書籍の3つを1つの導線にまとめて提示する**Webアプリ。
「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」をつなぐ。

## 技術スタック

| 領域 | 採用 |
|---|---|
| ランタイム | .NET 10 (LTS, 2028-11 まで) |
| Web | ASP.NET Core + Blazor |
| 収集ジョブ | `BackgroundService` |
| DB | PostgreSQL + EF Core |
| LLM 要約 | Claude Code ヘッドレス(`claude -p`)/ Anthropic API |

.NET 9 (STS) と .NET 8 (LTS) はどちらも 2026-11 に EOL のため採用しない。

## 開発環境

**ホストに .NET SDK を直接入れて開発する**(devcontainer は使わない)。Ubuntu 26.04 では
公式アーカイブに .NET 10 があるので、Microsoft のフィードを足さずに入る:

```
sudo apt install -y dotnet-sdk-10.0
```

SDK が無いホストでも、`Dockerfile` と同じ `mcr.microsoft.com/dotnet/sdk:10.0` で
ビルドやテストは通せる:

```
docker run --rm -v "$PWD":/src -w /src -e HOME=/tmp mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
```

## 定期実行と手動実行

収集(記事・イベント・書籍)と要約はどれも `BackgroundService` で定期実行するが、
**開発環境では既定で止めてある**(`appsettings.Development.json`)。開発サーバーを
消し忘れると、気づかないうちに収集先を叩き続けたり LLM の枠を使い続けたりするため。

| ジョブ | 設定 | 既定 | 開発 | 手動ボタン |
|---|---|---|---|---|
| 記事の収集 | `Collection:AutoRun` | true | false | `/` |
| イベントの収集 | `Collection:AutoRun` | true | false | `/events` |
| 書籍の収集 | `Books:AutoRun` | true | false | `/books` |
| 記事の要約 | `Anthropic:AutoRun` | true | false | `/` |

**環境で分岐せず設定値にしている**ので、本番で一時的に止めるときも
`Collection__AutoRun=false` のように環境変数で効く。

実行の中身は `BackgroundService` ではなく **`JobRunner` の派生クラス**(`Services/`)に
あり、**定期実行と画面のボタンが同じ経路を通る**。`JobRunner` が `SemaphoreSlim` で
直列化するので、**自動実行中にボタンを押しても二重には走らない** —— 二重に走ると同じ
収集先へ続けて叩きに行ったり、同じ記事を二度要約して LLM の枠を無駄にする。
`BackgroundService` 側はタイマーを回して Runner を呼ぶだけ。

ボタンは共通コンポーネント `Components/JobButton.razor`。**静的 SSR のフォーム POST**で、
対話回線(WebSocket)は張らない —— このアプリは `<Routes />` にレンダーモードを指定して
おらず**全ページ静的 SSR** なので、ボタンのためだけに回線を張るのは釣り合わない。
同じページに複数置くときは `FormName` を別々にする。

## 外部データソース

| 用途 | ソース | 備考 |
|---|---|---|
| イベント | connpass API / Doorkeeper API / TECH PLAY の RSS | connpass は要申請、他は不要 |
| 書籍 | openBD / Google Books API | openBD は日本の書誌情報が無料 |
| 記事 | 各種 RSS / Atom | Qiita・Zenn・はてブ テクノロジー等 |

外部 API を叩くコードを書くときは、**User-Agent に個人のメールアドレスを入れないこと**。
連絡先が必要な場合はリポジトリ URL のみを記載する。

収集対象のフィードと巡回間隔は `src/TechAntenna.Web/appsettings.json` の
`Collection` セクションで設定する。

connpass は API v2(`X-API-Key` と `User-Agent` が必須)。API キーと検索キーワードは
`Connpass` セクションで設定し、**キーの実値はコミットせず**環境変数
(`Connpass__ApiKey`)や user-secrets で渡す。キー未設定ならイベント収集は動かない。

Doorkeeper は `Authorization: Bearer` にアクセストークンが必要。`Doorkeeper` セクションで
設定し、トークンの実値はコミットしない(`Doorkeeper__AccessToken`)。レート制限は
認証済みで 300 リクエスト / 300 秒。**API は alpha 扱いで破壊的変更が予期されている**ため、
レスポンス形式が変わりうる前提で読むこと。

### Doorkeeper API Terms of Use で課される義務

規約(<https://www.doorkeeper.jp/developer/api> の API Terms of Use)のうち、**コードを見ても
分からないもの**を挙げる。`/terms`・`/privacy`・`/personal_information` は Doorkeeper に
投稿する側と Doorkeeper 社自身の個人情報取扱いの規定で、API 利用者への条件はこちらにある。

- **イベントを表示するときは必ず Doorkeeper のイベントページ(`public_url`)へのリンクを
  含め、そのリンクに `nofollow` 等を付けてはならない**(Must Have Information)。
  外部リンクの扱いを変えるときは規約違反にならないか確認する
- **説明文は取り込まない**。API 経由のコンテンツは Doorkeeper とその顧客に帰属する
  (Ownership)ため、取り込むのはイベントの事実情報(タイトル・URL・日時・会場)だけに
  とどめる。書籍で書誌事実だけを取り込むのと同じ方針
- **外部公開するなら、このアプリ自身の利用規約とプライバシーポリシーが必要**
  (Responsibility)。個人が手元で動かす分には不要だが、公開の前提条件として残しておく
- **Doorkeeper と競合するサービスに使ってはならない**(Restrictions)。本アプリは
  イベント管理・集客ではなく記事/イベント/書籍の横断発見なので競合しないと解しているが、
  イベント検索側を作り込むときは読み直す

**両方ともキーワードごとに問い合わせ、見つかったイベントにその検索キーワードをタグとして
付ける**。同じイベントが複数のキーワードで見つかったら URL でまとめてタグを足す。
connpass は `keyword_or` で全キーワードを1リクエストにまとめることもできるが、それだと
どのキーワードで見つかったか分からず、`hash_tag` が無いイベントがタグ無しになって
トピック横断に乗らないため、リクエスト数と引き換えにキーワードごとに引いている。

**Doorkeeper は検索語がタイトルに実際に含まれるものだけを取り込む**(`KeywordMatcher`)。
`q` が説明文まで検索し、しかも記号を落としてから照合するため、そのまま信じると
タグが意味を失うから —— 実測では `q=C#` が `q=C` と同じ結果を返し(`#` が無視される)、
`q=.NET` は説明文中の `https://…​.net/` という URL に当たっていた。C#/.NET/Blazor で
38 件取れたイベントは、タイトルに検索語を含むものが 0 件だった。

`KeywordMatcher` は単純な部分一致ではなく、**検索語の端が英数字のときだけ、その側が
英数字と地続きでないことを求める**。「AI」が `Rails`・`email` に誤爆するのを防ぎつつ、
語が空白で区切られない日本語(「生成AI最新ニュース」)と記号を含む語(`C#`・`.NET`)を
同じ規則で扱うため。

収集キーワードは AI 系(`AI` / `生成AI` / `LLM` / `AIエージェント` / `機械学習`)。
**C#/.NET は記号がトークナイズで落ちる収集元があり検索語として使いにくい**。
アプリの実装言語と、アプリが集める情報の対象は別。

### TECH PLAY(RSS)

**キーも申請も要らない代わりに検索ができない**。最新のイベントが 50 件流れてくるだけなので、
巡回して差分を溜めることで広く拾う(過去には遡れない)。企業主催のウェビナーはこの経路が
一番厚く、connpass / Doorkeeper に載らないベンダー系のイベントが入る。

- RSS 2.0 だが**開催日時・会場・住所は独自名前空間 `tp:`**(`https://rss.techplay.jp/`)に
  あり、標準の要素からは取れない。記事用の `FeedParser` と別実装(`TechPlayFeedParser`)なのは
  このため。`tp:` の日時には時差の表記が無く**日本時間**なので、読むときに UTC へ直す
- **タグは `<category>` から作る**(他の収集元は検索キーワードをタグにする)。ただし
  `IT`・`テクノロジー`・`イベント` は全イベントに必ず付いていて横断に使えないため落とす
- 設定は `TechPlay` セクション(`FeedUrl`)。空なら収集しない

規約([利用規約](https://techplay.jp/terms_of_use))は Doorkeeper と違い、**取得・再利用を
明示的に許諾していない**。第7条3項が「当社の許諾がない限り、当社著作物等の全部または一部の
利用、複製、転載等を行うことができない」とする一方、クローリングや RSS 利用を禁じる条項は
無い。取り込むのはイベントの事実情報(タイトル・URL・日時・会場)だけにとどめ、
**公開するなら事前に問い合わせること**。

## タグによる横断

記事・イベント・書籍は `TagNormalizer` を通した正規化済みタグを持ち、
`TopicService` がそれを突き合わせて `/topics` に出す。**3種がそろったタグを上位**に出す
(件数が多いだけのタグより、記事・イベント・書籍が全部あるタグを優先する)。

EF 版のタグ関連クエリだけ生 SQL にしている。`Tags` 列には値変換をかけていて LINQ から
翻訳できず、タグごとの件数集計には PostgreSQL の `unnest` が要るため。

## 書籍収集

**openBD はキーワード検索を持たず ISBN 参照専用**なので、役割を分けている。

- キーワード検索は Google Books API(`IBookCatalog`)。**API キーは実質必須** ——
  キー無しのリクエストは Google 共有の匿名プロジェクトの枠に入り、その枠は1日あたり 0 件
  (`defaultPerDayPerProject` が 0)なので、最初の1回から 429 が返る
- openBD は検索結果の書誌情報を ISBN で補う後段(`IBookEnricher`)。
  **既に値がある項目は上書きせず、欠けている項目だけを埋める**

設定は `Books` セクション(`Keywords` / `IntervalHours` / `GoogleBooksApiKey` /
`UseOpenBd`)。`Keywords` が空なら書籍収集のジョブ自体を登録しない。
`GoogleBooksApiKey` が空の場合はジョブは動くが検索が毎回 429 で失敗する —— 原因が
スタックトレースからは読めないため、429 のときだけキー未設定かどうかを見分けた
メッセージを投げている(`GoogleBooksCatalog`)。

取り込むのは**書誌事実(タイトル・著者・出版社・刊行日・ISBN・リンク・書影 URL)だけ**で、
`description` や `textSnippet` といった出版社の著作物は取り込まない。書影は画像自体を保持せず
URL のリンクにとどめる。

## LLM 要約

要約の実装(`ISummarizer`)は2つあり、`Program.cs` が**環境変数を見て選ぶ**。両方無ければ
要約ジョブを登録しない。**キー・トークンの実値はコミットせず**環境変数か user-secrets で渡す。

| 方式 | 選ばれる条件 | 課金 |
|---|---|---|
| `ClaudeCodeSummarizer` | `CLAUDE_CODE_OAUTH_TOKEN` がある(**優先**) | サブスクリプションの枠 |
| `AnthropicSummarizer` | `Anthropic__ApiKey` がある | API の従量課金 |

共通の設定は `Anthropic` セクション(`AutoRun` / `IntervalMinutes` / `BatchSize`)。指示文は
`SummaryPrompt` に集約し、方式を切り替えても要約の口調が変わらないようにしている。
定期実行の可否と手動実行は「定期実行と手動実行」を参照。

### Claude Code 方式(`claude -p`)

Claude Code にログイン済みの端末で `claude setup-token` を実行して得た長期 OAuth トークンを
`CLAUDE_CODE_OAUTH_TOKEN` で渡す。**ホストの `~/.claude` はマウントしない。**
トークンは CLI が環境変数から直接読むので、アプリ側は有無を見て方式を選ぶだけ
(`ClaudeCodeOptions` にトークンは持たせない)。設定は `ClaudeCode` セクション
(`ExecutablePath` / `Model` / `TimeoutSeconds`)。

**呼び出し1回の固定費が大きい。** 記事1件だけ渡しても Claude Code のハーネスが毎回入力に乗る
(実測: 1件で 32,300 トークン、5件まとめて 1件あたり 6,584 トークン)。だから `ISummarizer` は
記事1件ではなく**リストを受け取る**形にしてあり、`BatchSize` の既定も 20 と大きめにしている。
ツールを禁じても固定費はほとんど減らないので、**まとめて渡すこと自体が唯一の対策**。

実装上の勘所:

- **プロンプトは引数ではなく標準入力で渡す**(`IProcessRunner`)。Linux の単一引数の長さ上限
  (MAX_ARG_STRLEN = 128KiB)を記事をまとめると容易に超え、実行前に E2BIG で落ちる
- **`--json-schema` で番号と要約の対応を返させる**。記事の Id(GUID)を LLM に写させない
- **`claude` は失敗の詳細を stderr ではなく stdout の JSON に書く**(`result` と
  `api_error_status`)。終了コードだけ見ると原因が分からないので
  `ClaudeCodeResponseParser.DescribeError` で拾う
- **`--bare` は使えない。** bare モードは keychain と OAuth の読み取りを飛ばすため、
  `CLAUDE_CODE_OAUTH_TOKEN` が効かなくなる。公式ドキュメントは `--bare` を推奨し
  「将来 `-p` の既定にする」としているので、そうなったら追従が要る(v2.1.220 時点で
  `--no-bare` のような opt-out は無い)

### Anthropic API 方式

Anthropic 公式 .NET SDK(NuGet `Anthropic`)経由で Messages API を呼ぶ。呼び出しの固定費が
小さいので記事ごとに1リクエスト。モデルは既定で `claude-opus-5`、コストを抑えたいときは
`Anthropic__Model` を `claude-haiku-4-5`(入力 $1 / 出力 $5 per MTok)に変えられる。

## 構成

- `src/TechAntenna.Core` — ドメインモデルと抽象。外部パッケージへの依存を持たない
- `src/TechAntenna.Infrastructure` — EF Core・外部 API クライアント等の実装(Core を参照)
- `src/TechAntenna.Web` — ASP.NET Core + Blazor (Server)。収集ジョブの `BackgroundService` もここでホストする
- `tests/TechAntenna.Tests` — xUnit

## DB

- PostgreSQL + EF Core。接続文字列は `ConnectionStrings:Default`(環境変数なら
  `ConnectionStrings__Default`)で渡す。**未設定ならメモリ上のストアにフォールバック**する
- マイグレーションは起動時に自動適用される(個人運用前提)
- マイグレーション追加:
  `dotnet ef migrations add <名前> -p src/TechAntenna.Infrastructure -s src/TechAntenna.Web`

## 本番の実行形態(Docker)

本番は「コンテナイメージを docker compose で動かす」形態。アプリの構成
(プロジェクト構成・必要な環境変数・待ち受けポート)を変えたら、同じコミットで
`Dockerfile`・`docker-compose.yml`・`.env.example` も追従させる。

**リポジトリが非公開の間は、イメージをビルド・公開する GitHub Actions を置かない**
(非公開リポジトリでは Actions の実行時間も GHCR のストレージ・転送量もプラン付属の枠を
消費し、イメージはベース層だけで圧縮後 90MB/アーキ 積まれるため)。本番起動は
デプロイ先でのビルド(`docker-compose.build.yml` を重ねる)で行い、GHCR に置きたいときは
手元でタグを打って push する。公開リポジトリに切り替えるときにワークフローを追加する。

- `Dockerfile` — マルチステージ。`sdk:10.0` で publish し、`aspnet:10.0` に成果物だけを載せて
  非 root(`USER $APP_UID`=1654)で `dotnet TechAntenna.Web.dll` を実行する。HTTP 8080 のみ待ち受け
  - **Claude Code の CLI を同梱する**(要約のサブスク枠方式で使う)。イメージは約 370MB →
    730MB になる。配布物は Node に依存しない単体のネイティブバイナリなので**実行イメージに
    Node は入れない**。npm パッケージはアーキごとに分かれていて名前で明示すればビルドホストから
    対象アーキ用を取れるため、取得ステージも `$BUILDPLATFORM` で動かしエミュレーションを避ける
  - `claude` は起動時にホーム配下へ設定を書くので、非 root で動かすために `HOME=/home/app` を
    用意する(ディレクトリはビルドステージで作って `COPY --chown` する)
  - **Alpine ではなく Debian ベース**を使う。Alpine 版の .NET イメージは globalization
    invariant モードが既定で、日本語の日付・文字列の書式が効かない
  - arm64 向けは QEMU ではなく **.NET のクロスコンパイル**(`--platform=$BUILDPLATFORM` +
    `dotnet publish -a`)で出す。実行ステージに `RUN` を置かないのもエミュレーションを
    避けるため(鍵置き場のディレクトリはビルドステージで作って `COPY --chown` する)
- `docker-compose.yml` — 本番用(`app` + `db`)。この定義自体はビルドせず GHCR のイメージを
  参照する。設定は `.env` から環境変数で渡す。`docker-compose.build.yml` はその場でビルドする
  上書き定義(`:local` タグ)で、非公開の間の本番起動と手元の動作確認はこちらを重ねて使う
  - Postgres は `postgres:18-alpine`。**18 のイメージは PGDATA が
    `/var/lib/postgresql/18/docker`** で、ボリュームの単位はその1段上の
    `/var/lib/postgresql`(17 以前と位置が違うので、マウント先を変えると初期化し直しになる)
  - データは名前付きボリューム(`pgdata`/`dpkeys`)に置く。bind マウントにしないのは、
    ホスト側の所有者調整が要らないようにするため
- GHCR に置く場合の公開先は `ghcr.io/rtcode337/tech-antenna`。`latest` だけでなく
  `sha-xxxxxxx` も打つ(どのコミットが動いているか後から特定できるようにするため。
  手順は README「本番運用」)

`Program.cs` にコンテナ運用のための分岐が2つある。

- **Data Protection の鍵の永続化** — `DataProtection:KeysDirectory`(Docker では
  `DataProtection__KeysDirectory=/app/keys`)が設定されていればそこへ保存する。既定の
  一時領域だとコンテナを作り直すたびに鍵が変わり、発行済みの antiforgery トークンが無効になる。
  起動時に出る「No XML encryptor configured」の警告は、鍵ファイル自体の暗号化(証明書や
  KMS が要る)を設定していないためで、ホスト内のボリュームに 0600 で置く運用のため許容している
- **HTTPS 前提の設定の切り替え** — `UseHttpsRedirection`/`UseHsts` は HTTPS の待ち受けが
  分かるときだけ有効にする(`httpsConfigured`)。TLS をリバースプロキシで終端する構成では
  コンテナは HTTP しか持たず、リダイレクト先が決まらないまま警告だけが出るため

## 待ち受けポート

**開発(`launchSettings.json` の HTTP)も本番の公開ポート(`docker-compose.yml` の `PORT` の
既定)も 10000**。コンテナ内は 8080 固定(ベースイメージの `ASPNETCORE_HTTP_PORTS` の既定)で、
外に出す番号だけを揃えている。同じホストで開発サーバーと本番コンテナを同時に上げることは
できない(片方の `PORT` を変える)。**6000 番台は使わない** —— Chrome/Firefox が X11 用ポートとして
拒否し(`ERR_UNSAFE_PORT`)ブラウザから開けなくなるため。

## コマンド

- ビルド: `dotnet build`
- テスト: `dotnet test`
- Web 起動: `dotnet run --project src/TechAntenna.Web`(http://localhost:10000)
- 本番同等の起動(GHCR から pull): `docker compose pull && docker compose up -d`
- 本番同等の起動(手元でビルド):
  `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`
