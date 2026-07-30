# tech-antenna

技術情報を自動で収集し、**記事・イベント・書籍**を1つの導線にまとめて届けるWebアプリ。

> 「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」

## 状態

**開発初期。** ソリューションとプロジェクトの雛形まで作成済み。機能は未実装。

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
