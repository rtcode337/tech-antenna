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

## トピック収集

画面の「設定」で**「トピックを収集」**を押すと、`topic-catalog.json` の語彙・外部トレンドの
話題度・すでに集めた記事/イベント/書籍のタグを突き合わせて、トピック一覧を作り直す。

- 一覧の左のチェックボックスが**収集対象の選択**。「選択を保存」で確定する。イベント・書籍は
  選んだトピックを検索語にして問い合わせるので、**何も選んでいないと 1 件も集まらない**
- **記事だけは選択で絞らない。** RSS は検索ではなく巡回なので、絞っても収集先への負荷は
  変わらず、捨てると「選んだトピックの外で何が起きているか」が見えなくなる。
  流れてきた記事はすべて保存し、選んだトピックのものを**一覧で強調する**(太字・左の帯・
  タグの色)。選択が空でも記事は集まる
- **選択したトピックは一覧の先頭に固定する。** 再収集でそのトピックが見つからなくなっても
  一覧からは消えない(話題度が 0 になるだけ)。押し出されて画面から消えると、選択の保存は
  一覧に出ている分で置き換えるため、選択そのものが外れて収集が止まってしまう
- 話題度は**収集元ごとのシェアに直してから合算する**。全期間の質問数と直近のいいね数のように
  桁の違う値をそのまま足すと、桁の大きい収集元が常に勝ってしまうため
- **在庫(記事・イベント・書籍の件数)は順位に足さない。** 集めるのは選択したトピックだけなので、
  在庫で加点すると選択済みが上位に張り付き、新しいトピックが浮上しなくなる(表示はする)
- ホームのトピック欄には、この一覧の上位 10 件(選択済み → 話題度順)が並ぶ

## 書籍収集

「書籍」画面の**「書籍の収集」**で、選択中のトピック 1 つずつで Google Books を検索し、
openBD で書誌情報を補ってから保存する。**検索語は設定ではなくトピックの選択**から来る
(→[トピック収集](#トピック収集))。

集めたいのは新刊ではなく**その分野で読んでおくべき本**なので、検索は関連度順で引き、
並びはレビューの多さ(=どのくらい読まれているか)で決める。

- **検索は Google Books、補完は openBD(書誌)と楽天ブックス(レビュー)** と役割を分けている。
  openBD は日本の書誌を無料で引けるが**キーワード検索を持たず ISBN 参照専用**なので、
  検索で得た ISBN で引き直して**欠けている項目だけ**を埋める(既に値がある項目は上書きしない)
- **読まれている度合いは楽天ブックスのレビュー件数と平均評価**。評価をベイズ平均で件数に応じて
  割り引き、件数の対数を掛けて並べる —— 件数だけだと評価の低い話題書が定番書を押しのけ、
  評価だけだとレビュー 1 件で星 5 の本が最上位に来るため。ISBN の一括指定ができない API なので
  1 冊ずつ 1 秒空けて引く。**レビューを画面に出すときは楽天ウェブサービスのクレジット表記が必要**
  (アプリ ID は `RAKUTEN_APPLICATION_ID`。未設定ならレビュー無しで動く)
- Google Books の `ratingsCount` は日本語書籍にほぼ入っていない(実測 20 件中 0 件)ため使わない
- **Google Books の API キーは実質必須。** キー無しのリクエストは Google 共有の匿名プロジェクトの
  枠に入り、その枠は 1 日あたり 0 件なので最初の 1 回から 429 になる(`Books__GoogleBooksApiKey`)
- 検索条件は `langRestrict=ja`(日本語の技術書を拾うため)だけで、**並びは既定の関連度順**。
  1 キーワードにつき 20 件、キーワードの間は 2 秒空ける。`orderBy=newest` を使わないのは、
  新刊が欲しいわけではないうえに取りこぼしが大きいため(実測で `機械学習` は新着順だと 0 件、
  関連度順なら 300 件)
- 取り込むのは**書誌事実だけ**(タイトル・著者・出版社・刊行日・ISBN・リンク・書影 URL)。
  `description` などの出版社の著作物は取り込まず、書影も画像は持たず URL のリンクにとどめる
- 同じ本かどうかは **ISBN-13 →(無ければ)詳細ページの URL →(無ければ)タイトル**で判定する。
  既にある本は書誌情報を上書きしないが、**タグだけは足す** —— 1 冊が `AI` でも `LLM` でも
  見つかることがあり、捨てると最初のトピックにしか出てこないため
- 「書籍」画面は**トピックごとの折りたたみ**。トピック名を押すと開き、複数のトピックで
  見つかった本はそのすべてに出る。中は読まれている順で、レビューが取れていない本は後ろ
- `Books:MinReviewCount` を上げると、レビューがそれ未満の本は保存しない(既定 0 = 足切り無し)。
  **レビューが取れた本だけが対象**で、取れていない本は落とさない —— 楽天のアプリ ID が
  無い環境で 1 冊も保存されなくなるのを避けるため

現状の制限:

- 一度保存した本の書誌情報は**更新しない**(レビュー件数だけは次に見つけたときに更新する)
- Google Books の `q=` はタイトル以外(著者・説明文)にも当たるため、**キーワードと関係の薄い本が
  混じる**。イベント側の `KeywordMatcher` のような絞り込みは書籍には入れていない

## 本番運用

docker compose で動かす。ホストに .NET も Postgres も要らない。

```bash
cp .env.example .env   # POSTGRES_PASSWORD と、使う外部 API のキーを入れる
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

`http://<ホスト>:10000` で開く(`.env` の `PORT` で変更可)。データは Docker の名前付き
ボリューム(`pgdata`)に入り、未適用のマイグレーションは起動時に自動で当たるため、
更新は `git pull` して同じコマンドを打つだけでよい。

- イメージは main への push で GitHub Actions がビルドし、GHCR へ公開する
  ([.github/workflows/build-and-push-image.yml](.github/workflows/build-and-push-image.yml))。
  タグは `latest` とコミット識別用の `sha-xxxxxxx`。デプロイ先はビルドせず
  `docker compose pull && docker compose up -d` でよい
- 非公開リポジトリなので GHCR のパッケージも非公開。pull 側では事前に
  `read:packages` 権限の PAT で `docker login ghcr.io` が必要
- 障害時は `.env` の `TECH_ANTENNA_IMAGE` に `ghcr.io/rtcode337/tech-antenna:sha-xxxxxxx` を
  指定すれば任意の時点のイメージに戻せる
- リポジトリを置けない環境(NAS のコンテナマネージャー等、管理画面に YAML を貼り付ける
  タイプ)向けには [docker-compose.standalone.yml](docker-compose.standalone.yml) を用意している
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

収集は画面のボタンから実行する(→[トピック収集](#トピック収集))。記事はトピックを
選んでいなくても集まるが、イベントと書籍は選択が空だと 1 件も集まらない。

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
