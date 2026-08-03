# tech-antenna

技術情報を自動で収集し、**記事・イベント・書籍**を1つの導線にまとめて届けるWebアプリ。

> 「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」

## 状態

**開発初期。** 記事・イベント・書籍の収集と一覧表示、タグによる3種の横断、
PostgreSQL への保存、タグからのトピック一覧生成、LLM による記事の日本語要約まで実装済み。

## 構成

| 領域 | 採用 |
|---|---|
| ランタイム | .NET 10 (LTS) |
| Web | ASP.NET Core + Blazor |
| 収集ジョブ | `BackgroundService` |
| DB | PostgreSQL + EF Core |
| 要約 | Claude Code ヘッドレス(`claude -p`)/ Anthropic API |

## データソース

- **イベント** — connpass API(API キーが必要)/ Doorkeeper API(アクセストークンが必要)
- **書籍** — openBD / Google Books API
- **記事** — Qiita・Zenn・はてなブックマーク テクノロジー等の RSS / Atom

## 本番運用

docker compose で動かす。ホストに .NET も Postgres も要らない。

```bash
cp .env.example .env   # POSTGRES_PASSWORD と、使う外部 API のキーを入れる
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

`http://<ホスト>:10000` で開く(`.env` の `PORT` で変更可)。データは Docker の名前付き
ボリューム(`pgdata`)に入り、未適用のマイグレーションは起動時に自動で当たるため、
更新は `git pull` して同じコマンドを打つだけでよい。

- リポジトリが非公開の間は、イメージを自動公開する GitHub Actions を置いていない
  (非公開リポジトリでは Actions の実行時間と GHCR のストレージ・転送量がプラン付属の枠を
  消費するため)。公開に切り替えるときに追加する
- ビルド済みイメージを GHCR から pull して動かすこともできる。その場合は手元で
  タグを打って push し(コミットを特定できるよう `latest` だけにしない)、
  デプロイ先では `docker compose pull && docker compose up -d`。
  非公開パッケージなので pull 側は `read:packages` 権限の PAT で `docker login ghcr.io` が必要
  ```bash
  SHA=$(git rev-parse --short HEAD)
  docker build -t ghcr.io/rtcode337/tech-antenna:latest \
               -t ghcr.io/rtcode337/tech-antenna:sha-$SHA .
  docker push --all-tags ghcr.io/rtcode337/tech-antenna
  ```
  デプロイ先で特定のイメージを使うときは `.env` の `TECH_ANTENNA_IMAGE` に指定する
- TLS は前段のリバースプロキシで終端する前提(コンテナは HTTP のみ待ち受ける)。
  プロキシ配下に置くときは `.env` で `FORWARDED_HEADERS_ENABLED=true`
- **収集と要約は既定では自動実行しない**(画面のボタンを押したときだけ動く)。
  外部 API や LLM の無料枠を意図せず使い切らないため。定期実行にするときは `.env` で
  `COLLECTION_AUTORUN` / `BOOKS_AUTORUN` / `SUMMARY_AUTORUN` を `true` にする

## 開発環境

.NET 10 SDK が要る(Ubuntu 26.04 なら `sudo apt install -y dotnet-sdk-10.0`)。

```bash
dotnet build
dotnet test
dotnet run --project src/TechAntenna.Web   # http://localhost:10000

# 画面を触るときはホットリロード付きで(保存すると再起動なしでブラウザに反映される)
dotnet watch --project src/TechAntenna.Web --launch-profile watch   # http://localhost:10001
```

DB は PostgreSQL。接続文字列 `ConnectionStrings:Default` を設定して起動すると
未適用のマイグレーションが自動で適用される。未設定の場合はメモリ上のストアで動く
(再起動すると消える)。

記事・イベント・書籍を収集した後は、画面の「設定」から「トピックを収集」を実行する。
保存済みデータのタグを集計して DB のトピック一覧を更新し、ホームには話題度順の上位 10 件を表示する。

開発サーバーから compose の DB の実データを読むときは、`docker-compose.dev.yml` を
重ねて 5432 を開ける(`127.0.0.1` のみ。本番では重ねない)。

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d db
set -a; . ./.env; set +a
ConnectionStrings__Default="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=${POSTGRES_DB:-techantenna};Username=${POSTGRES_USER:-techantenna};Password=$POSTGRES_PASSWORD" \
  dotnet run --project src/TechAntenna.Web
```

**本番コンテナと同じ DB を見ることになる**ので、開発中のマイグレーションを流したくない
ときは重ねないこと(起動時に自動適用される)。SQL で覗くだけなら開ける必要はない
—— `docker compose exec db psql -U techantenna -d techantenna`。
