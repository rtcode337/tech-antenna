# tech-antenna

技術情報を自動で収集し、**記事・イベント・書籍**を1つの導線にまとめて届けるWebアプリ。

> 「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」

## 状態

**開発初期。** 記事・イベント・書籍の収集と一覧表示、タグによる3種の横断、
PostgreSQL への保存、Anthropic API による記事の日本語要約まで実装済み。

## 構成

| 領域 | 採用 |
|---|---|
| ランタイム | .NET 10 (LTS) |
| Web | ASP.NET Core + Blazor |
| 収集ジョブ | `BackgroundService` |
| DB | PostgreSQL + EF Core |
| 要約 | Anthropic API |

## データソース

- **イベント** — connpass API(API キーが必要)/ Doorkeeper API(アクセストークンが必要)
- **書籍** — openBD / Google Books API
- **記事** — Qiita・Zenn・はてなブックマーク テクノロジー等の RSS / Atom

## 本番運用

GHCR のイメージを pull して docker compose で動かす。ホストに .NET も Postgres も要らない。

```bash
cp .env.example .env   # POSTGRES_PASSWORD と、使う外部 API のキーを入れる
docker compose pull && docker compose up -d
```

`http://<ホスト>:8080` で開く(`.env` の `PORT` で変更可)。データは Docker の名前付き
ボリューム(`pgdata`)に入り、未適用のマイグレーションは起動時に自動で当たるため、
更新は `docker compose pull && docker compose up -d` だけでよい。

- イメージは main への push で GitHub Actions が amd64 / arm64 向けにビルドし
  `ghcr.io/rtcode337/tech-antenna` へ公開する。**非公開リポジトリのため、デプロイ先では
  `read:packages` 権限の PAT で `docker login ghcr.io` が必要**
- 特定のコミットへ戻すときは `.env` の `TECH_ANTENNA_IMAGE` に `:sha-xxxxxxx` を指定する
- TLS は前段のリバースプロキシで終端する前提(コンテナは HTTP のみ待ち受ける)。
  プロキシ配下に置くときは `.env` で `FORWARDED_HEADERS_ENABLED=true`
- 手元でイメージをビルドして本番同等に動かす:
  `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`

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
