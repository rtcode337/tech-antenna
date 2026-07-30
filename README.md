# tech-antenna

技術情報を自動で収集し、**記事・イベント・書籍**を1つの導線にまとめて届けるWebアプリ。

> 「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」

## 状態

**開発初期。** RSS / Atom フィードからの記事収集、connpass からのイベント収集
(API キーが必要)、PostgreSQL への保存、一覧表示まで実装済み。
書籍・LLM 要約・Doorkeeper 連携は未実装。

## 構成

| 領域 | 採用 |
|---|---|
| ランタイム | .NET 10 (LTS) |
| Web | ASP.NET Core + Blazor |
| 収集ジョブ | `BackgroundService` |
| DB | PostgreSQL + EF Core |
| 要約 | Anthropic API |

## データソース

- **イベント** — connpass API / Doorkeeper API
- **書籍** — openBD / Google Books API
- **記事** — Qiita・Zenn・はてなブックマーク テクノロジー等の RSS / Atom

## 開発環境

.NET SDK はワークスペース共有の devcontainer から提供される。
このリポジトリ単体で開く場合は .NET 10 SDK を別途用意すること。

```bash
dotnet build
dotnet test
dotnet run --project src/TechAntenna.Web
```

DB は PostgreSQL。接続文字列 `ConnectionStrings:Default` を設定して起動すると
未適用のマイグレーションが自動で適用される。未設定の場合はメモリ上のストアで動く
(再起動すると消える)。
