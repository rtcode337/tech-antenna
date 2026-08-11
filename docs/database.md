# データベース定義

PostgreSQL のテーブル定義と、テーブル間の関係をまとめた文書。**マイグレーションを
たどらなくても最終形が分かるようにするため**に置いている。

定義の**権威は3か所**にあり、この文書はそれを読める形に写したもの:

| 何 | どこ |
|---|---|
| 列そのもの(C# のプロパティ) | [`src/TechAntenna.Core/`](../src/TechAntenna.Core/) のモデル |
| 主キー・必須・型変換・索引 | [`TechAntennaDbContext.OnModelCreating`](../src/TechAntenna.Infrastructure/Persistence/TechAntennaDbContext.cs) |
| 実際に当たる DDL | [`src/TechAntenna.Infrastructure/Migrations/`](../src/TechAntenna.Infrastructure/Migrations/) |

**DB に変更を入れたら、同じコミットでこの文書も更新する**(手順は末尾)。

## 全体像

```mermaid
erDiagram
    Articles {
        uuid Id PK
        text Title
        text Url UK "収集元の URL。重複判定のキー"
        text SourceName
        text Kind "Article / News / Paper / TrendingPaper"
        text Summary "LLM 要約。null = 未要約"
        text TitleJa "論文タイトルの和訳"
        text ContentSnippet "フィードの本文抜粋（要約の材料）"
        integer BookmarkCount "null=未取得 / 0=ブックマークなし"
        integer UpvoteCount "HF Daily Papers の upvote。はてブとは別指標"
        text_array Tags "正規化済み"
        text_array RawTags "収集元のまま"
        timestamptz PublishedAt
        timestamptz CollectedAt
    }
    Events {
        uuid Id PK
        text Title
        text Url UK
        text SourceName
        timestamptz StartsAt
        timestamptz EndsAt
        text Venue
        boolean IsOnline
        text Organizer "主催グループ名。公式判定の材料（TECH PLAY は取れず null）"
        integer ParticipantCount "null=未取得 / 0=参加者なし"
        text_array Tags
        text_array RawTags
        timestamptz CollectedAt
    }
    Books {
        uuid Id PK
        text Title
        text DedupKey UK "ISBN13 → URL → タイトルの順で決める"
        text Isbn13
        text_array Authors
        text Publisher
        date PublishedOn
        text Url
        text CoverUrl
        double ReviewAverage "楽天。レビュー無しは null"
        integer ReviewCount "null=未取得 / 0=レビューなし"
        text_array RecommendedBy "推薦元の記事 URL"
        text_array Tags
        text_array RawTags
        text SourceName
        timestamptz CollectedAt
    }
    Tags {
        text Key PK "正規化済みタグ"
        text Status "Pending / Promoted / Alias / NotTopic / Unresolved"
        text TopicKey "Promoted なら自分、Alias なら寄せ先"
        text DecidedBy "None / Rule / Seed / Llm / Human"
        timestamptz DecidedAt
        timestamptz RetryAfter "Unresolved の再挑戦時刻"
        integer ArticleCount
        integer EventCount
        integer BookCount
        double TrendScore "外部トレンド由来"
        integer SourceCount
        timestamptz FirstSeenAt
        timestamptz LastSeenAt
    }
    Topics {
        text Key PK "正規化済みキー"
        text Display "画面に出す正式表記"
        text Parent "1つ上の粒度のキー"
        text English "英語圏の収集元へ投げる検索語"
        text Description "一言説明（120 文字以内）"
        text DecidedBy "Seed / Llm / Human"
        boolean IsSelected "収集対象に選んだか"
        double TrendScore "単体の話題度"
        double SubtreeTrendScore "配下込みの話題度"
        integer ArticleCount "自分 + 別名のタグから合算"
        integer EventCount
        integer BookCount
        timestamptz UpdatedAt
    }

    Digests {
        uuid Id PK
        timestamptz GeneratedAt "最新の1件を選ぶキー"
        text Lead "全体の導入（1〜2文）"
        jsonb Items "項目の配列（title / body / url）"
        text GeneratorName "Claude Code / Anthropic API"
    }

    Secrets {
        text Name PK "設定パス（例 Connpass-ApiKey）"
        text Value "Data Protection で暗号化した値"
        timestamptz UpdatedAt
    }

    Tags }o..o{ Articles : "Articles.Tags に Key が含まれる"
    Tags }o..o{ Events : "Events.Tags に Key が含まれる"
    Tags }o..o{ Books : "Books.Tags に Key が含まれる"
    Topics |o--o{ Tags : "TopicKey"
    Topics |o--o{ Topics : "Parent"
```

`Secrets` は画面から設定した API キー・トークンと、**キー以外でも画面から設定する数少ないもの**
(定期実行の時刻・ジョブのチェック、公式イベントの主催者名簿 `Events:OfficialOrganizers`)。
**どのテーブルとも関係を持たない**。`Name` は設定パスの形(例 `Connpass:ApiKey`)。
**これらの設定の入口はこのテーブル(= 画面)だけ**で、環境変数では渡せない。`Value` は Web 層が Data Protection(鍵は `DataProtection__KeysDirectory`)
で暗号化した文字列 —— **平文は DB に入らない**ので、DB のバックアップだけを持ち出しても
キーは読めない。裏返しに、**鍵ディレクトリを失うと値は戻せない**(アプリは復号できない行を
「未設定」として扱い、画面から入れ直してもらう。行は消さない —— 鍵を戻せば読める可能性を残す)。

`Digests` はホームの「今日のサマリー」の生成履歴。**どのテーブルとも関係を持たない**
(生成時点の記事・イベントから LLM が書いた文章のスナップショットで、元データが
消えても読み返せることに意味がある)。`Items` を行に分けず `jsonb` 1 列で持つのは、
項目単体を検索・集計する予定が無く、常にダイジェスト丸ごとで読み書きするため。
画面に出すのは `GeneratedAt` が最新の1件だけ。

**外部キーは1つも張っていない**(点線はそのため)。タグは `text[]` の中の文字列と突き合わせる
緩い対応で、正規化の規則を変えると対応先が変わる。参照整合性を DB に持たせると、
再正規化のたびに整合を取り直す仕組みが要るので、**アプリ側の突き合わせにとどめている**。

## 横断的な決めごと

- **日時はすべて `timestamp with time zone`(UTC で保存)**。人に見せるときだけ
  日本時間へ直す(`JapanTime`。実行環境の TZ には依存させない。CLAUDE.md「日時の表示」)
- **`Tags` / `RawTags` / `Authors` / `RecommendedBy` は `text[]`**。C# の
  `IReadOnlyList<string>` と値変換でつないでいる。**この変換のせいで LINQ から翻訳できず、
  タグごとの件数集計だけ生 SQL**(PostgreSQL の `unnest`)で書いてある
- **`Tags` は正規化済み、`RawTags` は収集元のまま。** 規則を変えたときに過去データを作り直せる
  ように両方持つ(`RawTags` から `Tags` を再生成する)
- **列挙は数値ではなく名前で保存**(`Articles.Kind`・`Tags.Status`・`Tags.DecidedBy`)。
  `psql` で覗いたときに読めるほうを優先した
- **`null` と `0` は別物**。`BookmarkCount` / `UpvoteCount` / `ReviewCount` /
  `ParticipantCount` の `null` は「取得していない」、`0` は「ブックマーク・upvote・
  レビュー・参加者が無い」。混ぜると未取得の行が最下位に沈む
- **収集で上書きするのは「後から増える数」だけ。** 既存行の書誌にあたる情報は上書きせず、
  `Events` は主催者と参加者数、`Books` はレビューだけを取り込む(取れなかった回に
  `null` で上書きもしない)
- **人気の指標は収集元ごとに列を分ける**(`BookmarkCount` = はてブ、`UpvoteCount` = HF の
  upvote)。母集団が違うものを 1 列に混ぜると、2 つの意味が 1 つの数字に潰れる
- **重複判定のキーは列にしてユニーク索引を張る**(`Articles.Url` / `Events.Url` /
  `Books.DedupKey`)。ISBN が無い本もあるので、書籍は ISBN13 → URL → タイトルの順で決めた値を持つ

### 索引

| テーブル | 索引 | 何のため |
|---|---|---|
| Articles | `Url`(ユニーク) | 重複取り込みの防止 |
| Articles | `Kind` | 種別ごとの一覧(ニュース / 記事 / 論文) |
| Events | `Url`(ユニーク) | 重複取り込みの防止 |
| Events | `StartsAt` | 開催日順の一覧 |
| Books | `DedupKey`(ユニーク) | 重複取り込みの防止 |
| Topics | `IsSelected` | 収集キーワードの取得 |
| Topics | `Parent` | ツリーの組み立て |
| Tags | `Status` + `RetryAfter` | 「次に聞く語」の抽出 |
| Tags | `TopicKey` | 語彙への合算 |
| Digests | `GeneratedAt` | 最新の1件の取得 |

`Secrets` は主キー(`Name`)だけで足りる(数件しか入らない)。

## タグ層(Tags)と語彙(Topics)

**「見かけたタグ」と「語彙」は別物なので、テーブルを分けている。** 以前は `Topics` 一枚に
同居していて(語彙 355 行 + タグ 1400 行)、状態を列にできず、画面の区分を
「行の有無 × カタログに載っているか × 分類記録の種別」から導出していた。
分けたことで **3 テーブル(`Topics` / `TopicClassifications` / `TopicDescriptions`)が
2 テーブル**になった。

```mermaid
erDiagram
    Tags {
        text Key PK "正規化済みタグ"
        text Status "Pending / Promoted / Alias / NotTopic / Unresolved"
        text TopicKey FK "Promoted なら自分、Alias なら寄せ先"
        text DecidedBy "Rule / Seed / Llm / Human"
        timestamptz DecidedAt
        timestamptz RetryAfter "Unresolved の再挑戦時刻"
        integer ArticleCount
        integer EventCount
        integer BookCount
        double TrendScore "外部トレンド由来"
        integer SourceCount
        timestamptz FirstSeenAt
        timestamptz LastSeenAt
    }
    Topics {
        text Key PK "正規化済みキー"
        text Display "画面に出す正式表記"
        text Parent FK "1つ上の粒度"
        text English "英語圏の収集元へ投げる検索語"
        text Description "一言説明"
        text DecidedBy "Seed / Llm / Human"
        boolean IsSelected
        double TrendScore
        double SubtreeTrendScore
        integer ArticleCount
        integer EventCount
        integer BookCount
        timestamptz UpdatedAt
    }
    Topics |o--o{ Tags : "TopicKey"
    Topics |o--o{ Topics : "Parent"
```

**役割:** `Tags` は「見かけた語とその処理状況」、`Topics` は「語彙」。
収集データ(`Articles` / `Events` / `Books`)のタグは<b>まず `Tags` に入る</b>。

### 状態(`Tags.Status`)

| Status | 意味 | 次にどうなるか |
|---|---|---|
| `Pending` | まだ仕分けていない | **全部が次の仕分けの対象**(件数での足切りはしない) |
| `Promoted` | トピックとして精査済み | `Topics` に行があり、`TopicKey` は自分 |
| `Alias` | 別名として既存トピックに吸収 | 件数は `TopicKey` のトピックへ合算 |
| `NotTopic` | トピックとして扱わない(画面の見出しは「除外」) | 語彙に入れず、LLM にも聞き直さない |
| `Unresolved` | LLM が判断できなかった | `RetryAfter` を過ぎたら `Pending` に戻す |

- **`RetryAfter` を列に持つ**のが要点。「7 日」の計算が読む側から消え、
  「次に聞く語」が `Status = Pending or RetryAfter <= now` の一発の条件になる
- **`DecidedBy`** は出どころ(規則で寄せた / シード / LLM / 人が直した)。
  画面で「この別名は LLM が付けた」を出しているので、これを列で持つ
- **状態を書き換えるのはタグの仕分けと手直しの操作だけ。** 観測(件数・話題度の書き込み)は
  状態に触らない —— 収集のたびに仕分けが巻き戻ると、同じ語を何度も LLM に聞くことになる
- **仕分けは「すでに観測したタグ」を対象にする。** 仕分けのジョブは外部トレンドを
  引かないので、**押しても対象は増えない**(押すぶんだけ減る)—— 新しい語が入るのは
  収集と「話題度を取り直す」のとき。画面の「次の仕分けで LLM に聞く語」と一致する

### 語彙の権威は DB に置く(`topic-seed.json` はシード)

`topic-seed.json` は**人が確定させた語彙ではなく、AI に作らせた初期値**なので、
**DB が空のときに一度流し込むシード**として扱う(`DecidedBy = Seed`)。以後の権威は DB 側で、
JSON との衝突ルールは持たない。手直しは画面から状態を書き換える(`DecidedBy = Human`)。

シードを完全に無くすには、次の 2 つが要る(入れば JSON は削除できる):

- ~~英語表記を LLM に出させる~~ —— **済み**。`Topics.English` に持ち、分類の応答に
  相乗りさせている(呼び出しは増えない)。arXiv には英語の検索語が要る
- ~~同義の親を寄せる統合パス~~ —— **済み**(`ITopicMergeAdvisor`)。仕分けの中で語彙の重複を
  LLM に見つけさせ、寄せ元のタグ・配下の親・行の削除まで面倒を見る(`TopicMerger`)
- ~~手直しの経路~~ —— **済み**。`/settings/tags` から状態を書き換えられる(`DecidedBy = Human`)

**残っているのは JSON を実際に外すかの判断だけ。** 空から始めると語彙が育つまで数回の
仕分けが要るので、初期値として置いておくか、捨てて統合パスに任せるかは運用の好みで決められる。

### 何が単純になるか

- 画面の 4 バケツが `GROUP BY Status`(いまは 3 テーブルの突き合わせ)
- 「次に聞く語」が 1 つの条件式(いまは候補の導出 + 期限の計算)
- **別名の件数が寄せ先に合算されるのが構造で保証される**(いまは再正規化の副作用)
- `IsSelected` がトピック側だけに付く(いまは生タグも選択できてしまい、掃除から守る必要がある)
- 呼び方が決まる: `Tags.Key` / `Topics.Key`、跨ぐ参照は `TopicKey`
  (いまは `Topics.Tag` と `TopicDescriptions.Key` が同じ意味で名前が違う)

### マイグレーションの履歴について

**本格稼働の前に履歴を 1 本(`InitialCreate`)にまとめてある。** タグ層を入れるまでに
16 個積み上がっていて、最終形を読むのに全部たどる必要があったため。既存の DB は捨てて
作り直した(まだ本番運用に入っていない時点だったので、移行は書き捨てた)。

以後は積み上げる。**列を足したら、この文書も同じコミットで更新する。**

## 変更手順

1. `src/TechAntenna.Core/` のモデルを直す(必要なら `TechAntennaDbContext` の設定も)
2. マイグレーションを作る
   ```
   dotnet ef migrations add <名前> -p src/TechAntenna.Infrastructure -s src/TechAntenna.Web
   ```
3. **この文書を同じコミットで更新する**(列の追加・削除・意味の変更・索引・重複キーの変更)
4. 起動時に自動適用される(`Program.cs`)。現物を確かめるなら
   `docker compose exec db psql -U techantenna -d techantenna -c '\d+ "Topics"'`

`TechAntennaDbContextModelSnapshot.cs` は差分計算のために EF が管理するファイルなので手で触らない。
**接続文字列を渡さない起動では EF を通らず**、`src/TechAntenna.Infrastructure/Storage/` の
メモリ実装が使われる(テストが触るのもこちらなので、`dotnet test` に DB は要らない)。
