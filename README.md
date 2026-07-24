# tech-antenna

技術情報を自動で収集し、**記事・イベント・書籍**を1つの導線にまとめて届けるWebアプリ。

> 「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」

記事の収集・要約に特化したサービスは数多くあるが、そこにイベントと書籍まで束ねたものは
見当たらない。そこを埋めるのが狙い。あわせて C# / .NET の学習を兼ねる。

## 状態

**開発着手前。** リポジトリの初期設定のみが入っている。

## 想定する構成

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
dotnet run --project src/TechAntenna.Web   # 予定
```
