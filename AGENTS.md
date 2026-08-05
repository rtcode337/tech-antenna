# Tech Antenna: エージェント向け指示

## 目的と構成

Tech Antenna は、技術記事・イベント・書籍を収集し、共通トピックでつなぐアプリケーションである。
.NET 10、ASP.NET Core + 静的 SSR の Blazor、`BackgroundService`、PostgreSQL + EF Core、Claude Code または Anthropic API による日本語要約を使用する。.NET 8/9 を対象に変更してはならない。

- `src/TechAntenna.Core`: ドメインモデルと抽象。外部パッケージに依存しない。
- `src/TechAntenna.Infrastructure`: EF Core と外部 API の実装。Core を参照する。
- `src/TechAntenna.Web`: Blazor ホストとジョブの `BackgroundService`。
- `tests/TechAntenna.Tests`: xUnit テスト。

## 開発・検証

ホストの .NET 10 SDK で開発する。devcontainer は使わない。Ubuntu 26.04 では `sudo apt install -y dotnet-sdk-10.0` を使う。SDK がない場合は、Dockerfile と同じ SDK イメージでテストできる。

```bash
docker run --rm -v "$PWD":/src -w /src -e HOME=/tmp mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
dotnet build
dotnet test
```

ビルドとコンテナを同時に走らせる前には `free -h` を確認する。swap のない環境で OOM を起こさないためである。

```bash
# 通常の開発サーバー: http://localhost:7020
dotnet run --project src/TechAntenna.Web --launch-profile http

# ホットリロード開発サーバー: http://localhost:7022
dotnet watch --project src/TechAntenna.Web --launch-profile watch
```

- `--no-launch-profile` は使わない。リポジトリで決めたポート設定を無効にしてしまう。
- 6000 番台はブラウザが X11 用の危険なポートと扱うため使わない。
- `dotnet watch` は Razor、scoped CSS、メソッド本体の C# を即時反映する。DI 登録、`Program.cs`、型追加などは再起動になる。
- ブラウザ自動更新を curl で確認する際は `Accept: text/html` を付ける。
- 接続文字列がなければ意図的に In-Memory ストアで起動する。画面作業に DB は不要だが、データは再起動で失われる。Compose の DB はホストの 7021 を公開しない。
- 実データを読ませたいときだけ、上書き定義 `docker-compose.dev.yml` を重ねて `127.0.0.1:7021` に開け、`ConnectionStrings__Default` を渡す(手順は README「開発環境」)。開発サーバーも起動時にマイグレーションを自動適用するため、未コミットのマイグレーションを持ったまま本番の DB へ繋がない。

## ジョブと静的 SSR

記事・イベント・書籍の収集と要約は、既定で無効であり、画面ボタンからのみ実行する。外部 API・LLM の利用枠を意図せず消費しないよう、この既定を変えない。

- 記事・イベント収集: `Collection:AutoRun`（既定 `false`）、ボタンは `/` と `/events`。
- 書籍収集: `Books:AutoRun`（既定 `false`）、ボタンは `/books`。
- 要約: `Anthropic:AutoRun`（既定 `false`）、ボタンは `/`。
- 環境変数は `Collection__AutoRun`、`BOOKS_AUTORUN`、`SUMMARY_AUTORUN` などを用いる。

ジョブの本体は `Services/` の `JobRunner` 派生クラスに置く。タイマーと画面ボタンは必ず同じ Runner を通すこと。`SemaphoreSlim` による直列化を迂回してはならない。二重実行は外部 API と LLM の利用を重複させる。ボタンは `Components/JobButton.razor` の静的 SSR フォーム POST を使い、複数置くときは `FormName` を別にする。ボタンのためだけに対話レンダーモードを追加しない。

## 外部データ・秘密情報

API キーやトークンの実値をコミットしない。環境変数または user-secrets を使う。主な設定は `Connpass__ApiKey`、`Doorkeeper__AccessToken`、`Books__GoogleBooksApiKey`、`Anthropic__ApiKey`、`CLAUDE_CODE_OAUTH_TOKEN`。外部 API の User-Agent に個人メールアドレスを入れず、連絡先が必要ならリポジトリ URL のみを使う。フィードと巡回間隔は `src/TechAntenna.Web/appsettings.json` の `Collection` にある。

- connpass API v2 には `X-API-Key` と `User-Agent` が必須。キーワードごとに取得してタグを保持し、重複イベントは URL で統合してタグを足す。
- Doorkeeper は Bearer トークンを使い、仕様変更の可能性がある。説明文を保存せず、タイトル・URL・日時・会場だけに限る。`public_url` は `nofollow` を付けず表示する。イベント管理サービスとの競合機能を作らない。`KeywordMatcher` により、説明文ヒットや記号無視による誤検出をタイトルで除外する。
- TECH PLAY は RSS のみで検索できない。`tp:` 名前空間を `TechPlayFeedParser` で別途解析し、時差なしの日時を日本時間として UTC に変換する。`category` からタグを作るが、共通の `IT`、`テクノロジー`、`イベント` は除外する。公開利用前には許諾を確認する。
- 収集キーワードは AI 系である。アプリの実装言語を理由に C#/.NET を検索語へ加えない。記号を落とす収集元がある。
- Google Books がキーワード検索を担当し、openBD は ISBN の書誌情報を不足分だけ補完する。説明文・抜粋・書影そのものは保存せず、書誌事実と書影 URL のみを扱う。

タグは `TagNormalizer` を通す。`TopicService` は `/topics` に表示し、記事・イベント・書籍の全種類が揃うタグを、単に件数が多いタグより優先する。タグ集計は値変換と PostgreSQL `unnest` の都合で意図的に生 SQL を使用している。

## 要約

`Program.cs` は環境変数で要約方式を選ぶ。認証情報がどちらもなければ要約ジョブを登録しない。`CLAUDE_CODE_OAUTH_TOKEN` を `Anthropic__ApiKey` より優先する。日本語の指示文は `SummaryPrompt` に集約する。

- Claude Code 方式は `claude -p` を使う。プロンプトはコマンド引数でなく標準入力で渡し、固定費を下げるため記事をバッチ処理する。`--bare` は OAuth トークンを読めなくなるため使わない。失敗詳細は stdout の JSON に出るので `ClaudeCodeResponseParser.DescribeError` で扱う。
- Anthropic SDK 方式は記事ごとに呼ぶ。既定モデルは `claude-opus-5`。低コスト用途では `Anthropic__Model=claude-haiku-4-5` を使える。

## アイコン

ファビコンは `Components/Layout/NavMenu.razor` のアンテナロゴと一致させる。`favicon.svg` と `tools/generate-icons.py` の両方に形状定義があるため、片方だけ変更してはならない。変更後は次で PNG/ICO を再生成する。

```bash
python3 tools/generate-icons.py src/TechAntenna.Web/wwwroot
```

`apple-touch-icon.png` は全面塗りの角丸なしに保つ。角丸は iOS が適用する。

## DB と Docker 運用

PostgreSQL は `ConnectionStrings:Default`（環境変数では `ConnectionStrings__Default`）で有効になる。マイグレーションは起動時に適用される。追加時は次を使う。

```bash
dotnet ef migrations add <name> -p src/TechAntenna.Infrastructure -s src/TechAntenna.Web
```

アプリ構成、必須環境変数、待受ポートを変えるときは、同じ変更で `Dockerfile`、`docker-compose.yml`、`.env.example` を追従させる。

```bash
# GHCR のイメージを使う
docker compose pull && docker compose up -d

# 手元でビルドする本番相当の起動
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

- ポートは 7020 番台に固める。アプリは `7020`(ホスト・コンテナ内・`dotnet run` すべて同じ)、DB は `7021`(`PGPORT` でコンテナ内も 7021)。Compose と並行するホットリロードだけ `7022` を使う。
- ランタイムは Debian ベース・非 root・Node なしを維持する。Alpine は日本語の globalization を壊す。
- サブスクリプション方式の要約用に Claude Code CLI をイメージへ残し、設定用の `HOME=/home/app` を書き込み可能にする。
- PostgreSQL 18 の `PGDATA` は `/var/lib/postgresql/18/docker`。名前付きボリュームは親の `/var/lib/postgresql` にマウントする。
- `pgdata` と `dpkeys` は bind mount でなく名前付きボリュームを使う。`DataProtection__KeysDirectory=/app/keys` で Data Protection キーを永続化する。
- イメージは `.github/workflows/build-and-push-image.yml` が main への push でビルドし GHCR へ公開する。タグは `latest` とコミット識別用の `sha-xxxxxxx`。非公開リポジトリなので Actions の実行時間と GHCR の容量はプラン付属の枠を消費する —— ワークフローは amd64 のみ・`paths-ignore` で文書だけの変更を除外している。
