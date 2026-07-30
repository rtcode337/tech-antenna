# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 回答言語

ユーザーとの会話・説明・コミットメッセージ等は常に日本語で行うこと(コード自体・コード中の識別子・英語サイトからの引用・エラーメッセージの原文などはこの限りではない)。

## このリポジトリの目的

技術情報を自動収集し、**記事・イベント・書籍の3つを1つの導線にまとめて提示する**Webアプリ。
「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」をつなぐ。

**C# / .NET の学習を兼ねた個人開発プロジェクト**であることに留意する。既存OSSで代替できる場面でも、
学習価値がある部分は自前で実装する方針を取ることがある。ライブラリ導入を提案する際は、
「何を学べなくなるか」もあわせて示すこと。

## 技術スタック

| 領域 | 採用 |
|---|---|
| ランタイム | .NET 10 (LTS, 2028-11 まで) |
| Web | ASP.NET Core + Blazor |
| 収集ジョブ | `BackgroundService` |
| DB | PostgreSQL + EF Core |
| LLM 要約 | Anthropic API |

.NET 9 (STS) と .NET 8 (LTS) はどちらも 2026-11 に EOL のため採用しない。

## 開発環境

.NET SDK はワークスペース共有の devcontainer(`/workspaces/vscode/.devcontainer/`)で
`ghcr.io/devcontainers/features/dotnet:2` により提供される。**このリポジトリ配下に devcontainer 定義は持たない**。
SDK のバージョンを変えるときは共有側の `devcontainer.json` を編集し、コンテナをリビルドする。

## 外部データソース

| 用途 | ソース | 備考 |
|---|---|---|
| イベント | connpass API / Doorkeeper API | どちらも公式・無料 |
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
設定し、トークンの実値はコミットしない(`Doorkeeper__AccessToken`)。
`q` は検索語を1つしか取らないためキーワードごとに問い合わせ、**見つかったイベントに
その検索キーワードをタグとして付ける**。同じイベントが複数のキーワードで見つかったら
URL でまとめてタグを足す。

## タグによる横断

記事・イベント・書籍は `TagNormalizer` を通した正規化済みタグを持ち、
`TopicService` がそれを突き合わせて `/topics` に出す。**3種がそろったタグを上位**に出す
(件数が多いだけのタグより、記事・イベント・書籍が全部あるタグを優先する)。

EF 版のタグ関連クエリだけ生 SQL にしている。`Tags` 列には値変換をかけていて LINQ から
翻訳できず、タグごとの件数集計には PostgreSQL の `unnest` が要るため。

## 書籍収集

**openBD はキーワード検索を持たず ISBN 参照専用**なので、役割を分けている。

- キーワード検索は Google Books API(`IBookCatalog`)。API キーは任意で、
  未設定でも検索できるが1日あたりの上限が低くなる
- openBD は検索結果の書誌情報を ISBN で補う後段(`IBookEnricher`)。
  **既に値がある項目は上書きせず、欠けている項目だけを埋める**

設定は `Books` セクション(`Keywords` / `IntervalHours` / `GoogleBooksApiKey` /
`UseOpenBd`)。`Keywords` が空なら書籍収集は動かない。

取り込むのは**書誌事実(タイトル・著者・出版社・刊行日・ISBN・リンク・書影 URL)だけ**で、
`description` や `textSnippet` といった出版社の著作物は取り込まない。書影は画像自体を保持せず
URL のリンクにとどめる。

## LLM 要約

記事の要約は Anthropic 公式 .NET SDK(NuGet `Anthropic`)経由で Messages API を呼ぶ。
設定は `Anthropic` セクション(`ApiKey` / `Model` / `IntervalMinutes` / `BatchSize`)で、
**キーの実値はコミットせず**環境変数(`Anthropic__ApiKey`)や user-secrets で渡す。
キー未設定なら要約ジョブは登録されない。

モデルは既定で `claude-opus-5`。コストを抑えたいときは `Model` を `claude-haiku-4-5`
(入力 $1 / 出力 $5 per MTok)に変えられる。要約はフィードの本文抜粋が入力なので
コンテキストは短く、モデルを下げても実用になりやすい。

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

## コマンド

- ビルド: `dotnet build`
- テスト: `dotnet test`
- Web 起動: `dotnet run --project src/TechAntenna.Web`
