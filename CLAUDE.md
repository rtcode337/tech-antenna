# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## このリポジトリの目的

技術情報を自動収集し、**記事・ニュース・論文・イベント・書籍を1つの導線にまとめて提示する**Webアプリ。
「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本・この論文」をつなぐ。

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

収集(記事・イベント・書籍)と要約はどれも `BackgroundService` で定期実行できるが、
**既定はすべて無効で、動くのは画面のボタンを押したときだけ**。消し忘れたサーバーが
気づかないうちに収集先を叩き続けたり、LLM や外部 API の無料枠を使い切ったりするため。
定期実行への切り替えは将来の課題。

| ジョブ | 設定 | 既定 | 手動ボタン |
|---|---|---|---|
| 記事・ニュース・論文の収集 | `Collection:AutoRun` | false | `/recent` |
| ブックマーク数の補完 | (記事の収集に含まれる) | — | `/recent` |
| イベントの収集 | `Collection:AutoRun` | false | `/events` |
| 書籍の収集 | `Books:AutoRun` | false | `/books` |
| 記事の要約 | `Anthropic:AutoRun` | false | `/recent` |
| 論文タイトルの翻訳 | (定期実行しない) | — | `/papers` |
| トピックの再編成(未知語の LLM 分類を含む) | (定期実行しない) | — | `/settings` |

**環境で分岐せず設定値にしている**ので、定期実行を有効にするときは
`Collection__AutoRun=true` のように環境変数で効く(開発・本番で挙動が変わらない)。
docker compose では `.env` の `COLLECTION_AUTORUN` / `BOOKS_AUTORUN` /
`SUMMARY_AUTORUN`(既定 false)がその環境変数に渡る。

実行の中身は `BackgroundService` ではなく **`JobRunner` の派生クラス**(`Services/`)に
あり、**定期実行と画面のボタンが同じ経路を通る**。`JobRunner` が `SemaphoreSlim` で
直列化するので、**自動実行中にボタンを押しても二重には走らない** —— 二重に走ると同じ
収集先へ続けて叩きに行ったり、同じ記事を二度要約して LLM の枠を無駄にする。
`BackgroundService` 側はタイマーを回して Runner を呼ぶだけ。

サイドバーのグループは**ホームと設定の2つ**(ホームの下に直近動向/書籍/イベント、
直近動向の下にさらにニュース/記事/論文が1段下がってぶら下がる。設定の下に外部連携)。
折りたたみは `details`/`summary` で、**開いているページのグループを既定で開く**
(`NavigationManager` で現在のパスを見る)。全ページ静的 SSR なので JS は使わない。

- **開いているグループ以外も DOM に残して三角で開けるようにする。** 現在地のグループだけ
  出すと、設定の下にいるあいだ記事や書籍へ辿れなくなる
- `summary` の `display` は **`list-item` のまま**にすること。`flex` にすると Chrome が
  開閉の三角(`::marker`)を消し、閉じたグループを開く手がかりが無くなる

ボタンは共通コンポーネント `Components/JobButton.razor`。**静的 SSR のフォーム POST**で、
対話回線(WebSocket)は張らない —— このアプリは `<Routes />` にレンダーモードを指定して
おらず**全ページ静的 SSR** なので、ボタンのためだけに回線を張るのは釣り合わない。
同じページに複数置くときは `FormName` を別々にする。

- **ジョブはバックグラウンドで走らせる**(`JobRunner.StartInBackground`)。応答を返し切るまで
  画面が出ない静的 SSR で数分のジョブを await すると、押した人は白い画面を待たされる。
  ページのハンドラは開始だけして戻る
- **実行中は `meta refresh`(3秒)で自動リロード**し、進捗(`JobRunner.Progress`。
  「未知の語 120 件を LLM で分類中: バッチ 1/2…」)をボタンの隣に出す。JS は使わない。
  リロード先の URL を明示した GET にするのは、POST 応答の再読み込みでフォームが
  再送されるのを避けるため
- **結果の文言は Runner が持つ**(`LastMessage`/`LastError`)。ページのフィールドに
  置くと、自動リロード(GET)のたびに消えてしまう

## 外部データソース

| 用途 | ソース | 備考 |
|---|---|---|
| イベント | connpass API / Doorkeeper API / TECH PLAY の RSS | connpass は要申請、他は不要 |
| 書籍 | openBD / Google Books API / 楽天ブックス / Qiita API | 楽天はレビュー、Qiita は推薦本 |
| 記事 | 各種 RSS / Atom | Qiita・Zenn・はてブ テクノロジー等 |
| ニュース | 各種 RSS | Publickey・ITmedia NEWS・InfoQ Japan・CodeZine |
| 論文 | arXiv API / J-STAGE API | どちらもキー不要。間隔は 3 秒以上空ける |

外部 API を叩くコードを書くときは、**User-Agent に個人のメールアドレスを入れないこと**。
連絡先が必要な場合はリポジトリ URL のみを記載する。

収集まわりの共通の守り(理由はコードのコメントにもある):

- **取り込む URL は `WebUrl`(Core)で http/https だけに絞る**。`Uri.TryCreate` は
  `javascript:` も絶対 URI として通し、画面の `href`/`img src` にそのまま出るため。
  新しい収集元を足すときもパーサで `WebUrl` を通すこと
- **HttpClient には `MaxResponseContentBufferSize` を掛ける**(`Program.cs` の
  `MaxResponseBytes`)。上限なしだと、収集先が侵害されて巨大応答を返したとき
  swap の無いホストで OOM になる
- **`System.Net.Http.HttpClient` カテゴリのログは Warning に落としてある**
  (appsettings.json)。既定の Information はリクエスト URI を出すので、
  クエリ文字列でキーを渡す API(Google Books・楽天)のキーがログに残るため

連携先とキーの状態は `/integrations`(設定 → 外部連携)にまとめて出す。一覧は
`IntegrationCatalog` に**手で並べてある** —— 「未設定だと何が起きるか」は設定値には書いて
いないし、キーの要否は収集元ごとの事情(申請の要不要・無料枠)で決まるため自動生成できない。
**外部 API を足したらここにも1行足すこと。** 画面に出すのは**キーの有無だけ**で、
値は長さも先頭数文字も出さない。

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

### 記事の種別(`ArticleKind`)

記事・ニュース・論文は**同じ `Article` として保存し、`Kind` で分ける**(画面と、
要約の対象かどうかにだけ効く)。フィードの種別は `Collection:Feeds` の `Kind` で指定する。

画面は**親子の2段**。`/recent`「直近動向」(親)が一望のハブで、**興味のあるトピックの
外も含めて**直近 7 日の話題をニュース→記事の順に新着で数件ずつ出す(話題度は並びではなく
カードの強調に出す)。全量は子ページ(`/news`・`/articles`・`/papers`、3ルートで
1 コンポーネント `ArticleListPage`)が**新着順**で受け持つ。カードは共通の
`Components/ArticleCard.razor` で、**興味のあるトピックに当たる記事はタイトル前の黄色い★**
(カード全体を染める強調は話題度専用にした —— 2つの意味で同じ強調を使うと読めない)。
収集・要約のボタンは親に、翻訳は `/papers` に置く。

- **論文は要約しない。** 本文(abstract)を取り込んでいないので材料が無く、タイトルだけ
  渡しても LLM の枠を使うだけ。`GetUnsummarizedAsync` が `Paper` を除外している
- **arXiv には英語の検索語を投げる**(`TopicCatalog.EnglishTermOf`)。日本語の正式表記を
  そのまま投げると 0 件になる(実測: `生成AI` で 0 件)。J-STAGE は和文の索引なのでそのまま
- **J-STAGE の Atom は `FeedParser` では読めない。** entry の中身が独自要素
  (`article_title`/`article_link`)で標準の `title`・`link` を持たないため、専用のパーサにしている

### ブックマーク数(`Article.BookmarkCount`)

**人気の指標は、はてなブックマーク数に一本化**している。数値を持つフィードが、はてブの
RSS(`hatena:bookmarkcount`)しか無いため —— Qiita の popular-items も Zenn の /feed も
**人気順の選別はあるが数値は乗らない**(実測)。他ソースの分は、記事収集の最後に
**はてなブックマーク件数取得 API**(キー不要・50 URL/リクエスト)で直近 7 日の
記事・ニュースをまとめて引き直す(`BookmarkCountRefresher`)。論文は対象外(はてブにほぼ載らない)。

- **null は「未取得」、0 は「ブックマークされていない」**で別物(書籍の `ReviewCount` と同じ)
- **並び順は変えない**(一覧も直近動向も新着順)。話題度は目立ち方に出す —— バッジが
  件数で3段階(10 users 未満 / 10〜99 / 100 以上)、直近動向ではカードの面にも同じ段階が出る
  (`ArticleCard` の `EmphasizeByPopularity`)
- 直近動向の窓を 7 日にしているのは、窓が無いと動きの止まった話題が居座り続けるため

### フィードのタグはタイトルから作る

**Zenn の RSS も Qiita の Atom も `category` 要素を持たない**(実測: Qiita 49 件・Zenn 23 件が
すべてタグ無しだった)。ニュースサイトも同様なので、収集元のタグだけに頼るとトピック横断にも
一覧の強調にも乗らない。`TopicCatalog.FindIn` で**タイトルに出てくるトピックをタグに足す**。

- 判定は `KeywordMatcher`(`AI` が `Rails`・`email` に誤爆しない)
- 見るのは**タイトルだけ**。本文まで見ると、文中で一度触れただけの語でタグが付く
- **再正規化も同じ規則で作り直す**(`RawTags` + タイトル)。そうしないと、この規則を入れる前に
  集めた記事がタグ無しのまま残る

## タグによる横断

記事(ニュース・論文を含む)・イベント・書籍は `TagNormalizer` を通した正規化済みタグを持ち、
`TopicService` がそれを突き合わせて `/topics` に出す。**3種がそろったタグを上位**に出す
(件数が多いだけのタグより、記事・イベント・書籍が全部あるタグを優先する)。

### 収集対象の選択(`IsSelected`)

**効くのはイベントと書籍だけ。記事は選択で絞らない。** イベント・書籍は選んだトピックを
検索語にして問い合わせる(選択が空なら 1 件も集まらない)が、記事の RSS は検索ではなく
巡回なので、絞っても収集先への負荷は変わらない —— 捨てると選んだトピックの外が
見えなくなるだけなので全部保存し、画面で選択トピックのものに★を付けている。

何を集めるかは `/settings` のトピック一覧の**行頭のチェックボックス**で決める(概要は
README「トピック収集」)。保存は `UpdateSelectionAsync` で、**一覧に出ている分で丸ごと
置き換える**(チェックの外れたものは POST に乗らないため)。

そのため **`GetTopicsAsync` は選択済みを話題度によらず先頭に固定する**。行を消さないだけでは
足りない —— 再収集で現れなかったトピックは話題度が 0 になり、上位 N 件から押し出されて画面から
消える。消えた状態で「選択を保存」を押すと、その選択まで外れて収集が止まる。

EF 版のタグ関連クエリだけ生 SQL にしている。`Tags` 列には値変換をかけていて LINQ から
翻訳できず、タグごとの件数集計には PostgreSQL の `unnest` が要るため。

### 正規化でやること・やらないこと

`TagNormalizer` が潰すのは**機械的な表記ゆれだけ**。

- NFKC で全角英数と半角カナをそろえ、小文字化する(`ＡＩ` と `AI`、`ｼﾞｪﾈﾚｰﾃｨﾌﾞ` と `ジェネレーティブ`)
- 区切り(空白・`-`・`_`・`・`・`/`)を落とす(`Claude Code` と `claudecode` を同じキーにする)。
  **`.` `#` `+` は残す** —— 落とすと `.net` が `net` に、`c#` が `c` になって別の語と衝突する
- **トピックでない語(ストップワード)を落とす。** 収集元の分類名や読み手の行動を表す語
  (`テクノロジー`・`あとで読む`・`初心者`)。実データでは 772 タグ中の上位 5 件のうち 2 件が
  これで、残すと話題度の上位を占めてトピック一覧が使い物にならない

**やらないこと**が 2 つある。`ai` と `人工知能` のような**同義語は機械的に潰せない**ので
別名カタログの仕事にする。`ai` ⊃ `生成ai` ⊃ `llm` は同義ではなく**粒度の違い**なので、
そもそも統合しない —— まとめると上位の語だけが巨大化して、何の話題か分からなくなる。

### 未知の語の LLM 分類(`ITopicClassifier`)

カタログに無い語は、**「トピックを再編成」の中で LLM が分類してツリーへ入れる**。
**候補は記事・イベント・書籍の収集で自然に溜まる**(`TopicCandidateFinder` が保存済みデータの
タグから導出し、設定画面の「新規トピック候補」にも出す)—— 候補集め(語彙)と話題度(鮮度)は
性質が別で、候補のために外部へ聞きに行く必要はない。同義語・粒度の違いの判定は語の意味を知らないとできないため、
機械的な正規化やカタログでは扱えない領域をここが受け持つ。方式は要約と同じ 2 つ
(`CLAUDE_CODE_OAUTH_TOKEN` 優先、無ければ `Anthropic__ApiKey`。両方無ければ分類なしで
収集だけ動く)。指示文とスキーマは `TopicClassificationPrompt` に集約。

- 1 語ごとに **alias(既存トピックの同義語)/ new(親付きの新トピック)/
  skip(トピック外と確信)/ unknown(知らない・新しすぎる)** の 4 択で返させ、
  `TopicClassificationValidator` が検証する —— 実在しない寄せ先・自分自身への寄せ・
  実在しない親は捨てる(**LLM の応答をそのまま信じない**)
- 通った分類は **DB(`TopicClassifications`)に保存し、起動時にカタログへ合成する**
  (`TopicCatalog.Extend`)。`topic-catalog.json` は「人が確定させた語彙」、DB は
  「LLM の自動分類」と役割を分け、キーが衝突したら JSON 側を優先する。
  分類を人手で直したいときは JSON に書けばよい(同じキーは JSON が勝つ)
- **Skip も保存する** —— 保存しないと、トピックでない語を毎回 LLM に聞き直して枠を無駄にする。
  **unknown(判断できない・検証落ち・応答に無かった語)は期限付きで保存する**
  (`UnknownRetryDays` = 7 日。期限内は聞かず、過ぎたら未分類に戻して再挑戦)。
  保存しないと毎回同じ語を聞き直して枠を無駄にし(実測: 毎回 ~20 語が再出題されていた)、
  無期限に確定させると、LLM の知識に無い新語(まさにツリーに入れたい語)が永久に平置きのまま残る
- **タグの再正規化は再編成の中で毎回走る**(LLM 分類の別名も、手で編集した
  カタログの別名も、ここで過去データへ反映してから一覧を作る)。数秒で冪等なので、
  専用ボタンを覚えて押してもらうより毎回やるほうが確実 —— かつての
  「タグを再正規化」ボタンはこれに吸収して消した
- **対象は「新規トピック候補」(タグとして 3 回以上付いた語。`TopicCandidateFinder.MinCount`)と
  「今回のトレンドに現れた語」**。1〜2 件の語は誤記や一過性のタグが多く、
  LLM の枠を使ってまで整理する価値が無い(平置きのまま残る)
- 1 回の実行で渡す語は 300 個まで(`MaxTagsPerClassification`。呼び出し回数の暴走を防ぐ枠。
  積み残しは次の実行で続きから)。**LLM への呼び出しは 60 語ずつのバッチ**(`BatchSize`)——
  200 語を 1 回に詰めたら応答の生成が 300 秒のタイムアウトを超えて丸ごと失敗した(実測)。
  後続バッチが失敗してもそれまでの分類は保存する
- **Skip と確定した語はトピック一覧から取り除く**(`ITopicStore.RemoveAsync`。選択済みは
  消さない)。ニュース・開発 のような一般語が話題度の上位を占めると一覧が読めないため。
  記事などのタグとしては残る —— 消すのはトピック一覧の行だけ
- **話題度は「単体」と「配下込み」の 2 列を持つ**(`TrendScore` / `SubtreeTrendScore`。
  合算は収集時の `AggregateTrendScores`)。「プログラミング言語」のような構造の語は単体の
  話題度がほぼ付かず、単体だけで並べると一覧の取得件数から押し出されて子が根として孤立する。
  一方、合算だけだと汎用的な親ばかりが上がる —— だから両方持って使い分ける。
  **取得の足切りとツリーの並びは合算、ランキング表示は単体**
- `/settings` の一覧は**ツリー(既定)とランキングの2表示**を `?view=ranking` のリンクで
  切り替える(静的 SSR のまま)。ツリーは `details`/`summary` で**子を折りたためる**。
  既定は全て開いた状態で、**閉じたノードは `wwwroot/topic-tree.js` が localStorage に覚えて
  次回再現する**(nav-menu.js と同じ流儀。enhanced navigation 後の当て直しも同じ)。
  閉じていてもチェックボックスは DOM に残るので、選択の保存(見えている分で置き換え)は
  そのまま成立する。見た目はテーブル型 —— 各行を同じ列構成のグリッドにして列を縦にそろえ、
  字下げは名前セルの中だけ、開閉の三角は標準マーカーを消して自前の `.caret` で描く
  (コンテナごと下げたり標準マーカーを残すと、話題度・日時の列がずれる)

### 生タグを保存する(`RawTags`)

記事・イベント・書籍は、収集元から受け取ったままのタグを `RawTags` に持つ。
**正規化後の値しか持たないと、規則を変えても過去のデータに反映できない**ため
(`Tags` が `init` ではなく `set` なのも、ここから作り直すため)。

規則を変えたら `/settings` の「トピックを再編成」を実行する(再編成が毎回
`TagRenormalizationRunner` を回す)。`RawTags` から `Tags` を作り直すだけで外部へは
出ないので、何度走らせても同じ結果になる。

`RawTags` を足したマイグレーション(`AddRawTags`)は、**既存行の `RawTags` を当時の `Tags` で
埋める**。元の表記はもう取り戻せないので、手元にある値で埋めて再正規化が空にならないように
してある。以降に収集した行には収集元の値がそのまま入る。

## 書籍収集

集めたいのは新刊ではなく**その分野で読んでおくべき本**。検索は関連度順で引き、
並びは「どのくらい読まれているか」(レビュー)で決める。

**openBD はキーワード検索を持たず ISBN 参照専用**なので、役割を分けている。

- キーワード検索は Google Books API(`IBookCatalog`)。**API キーは実質必須** ——
  キー無しのリクエストは Google 共有の匿名プロジェクトの枠に入り、その枠は1日あたり 0 件
  (`defaultPerDayPerProject` が 0)なので、最初の1回から 429 が返る。
  **`orderBy` は付けない**(既定の関連度順)—— 新着順は取りこぼしが大きく、実測で
  `機械学習` が 0 件(関連度順なら 300 件)だった
- openBD は検索結果の書誌情報を ISBN で補う後段(`IBookEnricher`)。
  **既に値がある項目は上書きせず、欠けている項目だけを埋める**
- 楽天ブックスは**レビュー専用**の後段(`RakutenBooksEnricher`)。書誌情報は触らない

### 薦められている度合い(`IBookRecommendationSource`)

「読むべき技術書」を挙げた記事から、薦められている本を拾う(`QiitaBookRecommendationSource`)。
**レビュー(読まれた量)とは別軸**で、こちらは「詳しい人が名指しで薦めたか」。並びでは
**推薦回数を優先**する —— レビュー数は一般向けの本ほど有利になるため。

- **公式 API(v2)を使う。** 検索が本文まで返すので記事ごとに引き直さない。未認証 60/時、
  トークンありで 1000/時。`Qiita:Queries` は**ストック数の下限**で絞る(読まれていない記事の
  推薦まで数えると指標が薄まる)
- **クエリは複数で、1クエリずつページングして読む**(1ページ 50 件、`Qiita:MaxArticles` まで)。
  検索は新着順に返るため、1ページで打ち切ると古い定番記事が読めない(実測: `tag:技術書
  stocks:>100` は 52 件あるのに旧実装は新着 20 件しか読んでいなかった)。タグ検索だけだと
  タグ無しの「読むべき本」系記事を取りこぼすので**本文検索のクエリも混ぜる**(実測 374 件)——
  ノイズは ISBN の検算が落とすので当たりの広さは害にならない。同じ記事が複数のクエリに
  当たっても URL の重複を落として1票
- 本の特定は**記事に貼られた Amazon リンクの ASIN**。書籍の ASIN は ISBN-10 そのものなので
  ISBN-13 に直せる(`Isbn.FromAsin`)。**チェックディジットまで検算する**ので、`B0…` で始まる
  Kindle 専売や電子機器は落ちる(実測: 90 個中 82 個が書籍)
- **保存するのは ISBN と出典記事の URL だけ**。記事本文は保存しない(複製にしないため)
- 拾えるのは ISBN だけなので、**タイトルは openBD が埋める**。`OpenBdEnricher` がタイトルの
  空欄だけを埋めるのはこのため。埋まらなかった本は保存しない(空行が並ぶだけになる)

### 読まれている度合い(`BookPopularity`)

`ReviewCount` / `ReviewAverage` から「読んでおくべき度」を出す。**評価をベイズ平均で件数に
応じて割り引き、件数の対数を掛ける** —— 件数だけだと評価の低い話題書が定番書を押しのけ、
評価だけだとレビュー 1 件で星 5 の本が最上位に来る。

- **`ReviewCount` の null と 0 は別物。** null は「取れていない」、0 は「読まれていない」。
  混ぜるとアプリ ID を設定する前に集めた本がまとめて最下位に沈む。`Score` も null を返す
- 楽天は**レビューが無いとき `reviewAverage` に `"0.0"`** を返す。そのまま平均に使うと
  評価が最低の本になるので、パーサが 0 を null に落としている
- **ISBN の一括指定ができない**(openBD と違う)ので 1 冊 1 リクエスト。既定 1 秒間隔
- **レビューを画面に出す場所には楽天ウェブサービスのクレジット表記が要る**(利用規約 Article 13)。
  指定の HTML は改変不可。今は `/books` にテキスト版を置いてある
- Google Books の `ratingsCount` は日本語書籍にほぼ入っていない(実測 20 件中 0 件)ため使わない

**検索語は設定ではなく選択中のトピック**(`ITopicStore.GetSelectedAsync` の `Display`)。
1 キーワードずつ問い合わせ、検索に使ったキーワードをそのまま書籍のタグにする
(正規化前の値が `RawTags`)。設定に検索キーワードは持たない —— かつての `Books:Keywords` は
トピック選択に一本化した時点で使われなくなっていたので消した。

重複判定は `BookKey`(ISBN-13 →(無ければ)詳細ページの URL →(無ければ)タイトル)。
**既存の本は書誌情報を上書きしないが、タグとレビューは取り込む**(`BookMerge`)—— 書籍は
トピックごとに検索するので 1 冊が複数のトピックで見つかる。捨てると最初のトピックにしか
出てこない。レビューだけ上書きするのは、時間とともに増える数値だから(取れなかった回に
null で上書きはしない)。
`Book.RawTags` が記事・イベントと違って `init` ではなく `set` なのはこのため。
**新しく `Book` を組み直す箇所では `RawTags` を写し忘れないこと** —— 落ちても収集直後は
正常に見え、タグの再正規化が走った瞬間にその本のタグが空になる(`OpenBdEnricher` で
実際に踏んだ)。

設定は `Books` セクション(`IntervalHours` / `DelayBetweenKeywordsSeconds` /
`GoogleBooksApiKey` / `UseOpenBd` / `MinReviewCount`)と `Rakuten` セクション
(`ApplicationId` / `AccessKey` / `DelaySeconds`。**実値はコミットしない**)。
`MinReviewCount` の足切りは**レビューが取れた本だけが対象**で、取れていない本は落とさない
—— アプリ ID の無い環境で 1 冊も保存されなくなるため。`GoogleBooksApiKey` が空でもジョブは登録する
(検索は毎回 429 で失敗する)。**ボタンごと消すより 429 の理由を画面に出すほうが打つ手が
分かる**ため、429 のときだけキー未設定かどうかを見分けたメッセージを投げている
(`GoogleBooksCatalog`)。

取り込むのは**書誌事実(タイトル・著者・出版社・刊行日・ISBN・リンク・書影 URL)だけ**で、
`description` や `textSnippet` といった出版社の著作物は取り込まない。書影は画像自体を保持せず
URL のリンクにとどめる。

`/books` は**トピックごとの折りたたみ**(`details`/`summary`)。全ページ静的 SSR なので、
開閉に JS も対話回線も使わない。1 冊が複数のトピックに出るのは意図した挙動(タグの数だけ
並ぶ)。タグが 1 つも無い本は最後の「トピックなし」に入れて、取りこぼしを見えるようにしている。

推薦の見せ方は2段構え(`/topics` の詳細ページも同じ):

- **「記事で薦められている本」の枠は推薦 2 本以上だけ**(`MinRecommendations`)。1 本だけの
  推薦は、まとめ記事が網羅的に貼ったリンクも含まれて薄い
- **トピックの折りたたみの中では推薦 1 本から強調**(左帯 + 薄い面。記事一覧の
  「選んだトピック」と同じ文法)。こちらはトピックで既に絞られているので下限を設けない

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
- **`--max-turns` は 2 にする。** 構造化出力自体が内部のツール呼び出しとして実装されていて
  1 ターン消費する(v2.1.220 で実測: 1 だと結果を出す前に `error_max_turns` で打ち切られ、
  終了コード 1・詳細なしで落ちる)
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

## 見た目(CSS)

**Bootstrap は使っていない。** Blazor の雛形が入れた
`lib/bootstrap/dist/css/bootstrap.min.css` への `<link>` は残っていたが**実体が無く、
毎回 404 を引いていた** —— `list-unstyled` が効かず箇条書きの点が出ていたのはこのため。
link を外し、razor が実際に使っているユーティリティ(`text-muted`・`badge`・`table`・
`btn`・`ms-1` など 20 個ほど)だけを `wwwroot/app.css` に自前で持っている。
**クラス名は Bootstrap のものに合わせてある**ので razor 側は書き換えていない。
新しいユーティリティを razor で使うときは、app.css の「ユーティリティ」節に足すこと
(足さないと無言で効かない)。

色・余白・角丸・影は `app.css` の `:root` に**カスタムプロパティ**として置き、
各 `*.razor.css` はそれを参照する。**個々の CSS に生の色を書かないこと** ——
1か所で調整できなくなる。

- **ダークモードは `prefers-color-scheme` で OS に追従する**。全ページ静的 SSR なので
  切り替えボタン(JS と保存先が要る)は置かない。`:root` に `color-scheme: light dark`
  を宣言してあるので、チェックボックス等のフォーム部品も一緒に切り替わる
- サイドバーはライト/ダークどちらでも暗い面のまま(`--nav-*`)。雛形の紺→紫
  グラデーションはやめ、単色にしてある
- 一覧(記事・イベント・書籍・トピック詳細)は**同じ形のカード**で出す。種別ごとに
  見た目を変えると、3種を並べて見るページで追いにくくなる
- **焦点の輪(`:focus-visible`)は操作できる要素にだけ付ける。** 全要素に付けると、
  `FocusOnNavigate`(`Routes.razor`)が読み上げの起点として h1 に `tabindex="-1"` を
  足して焦点を移すぶん、**ページを開くたび見出しが枠で囲まれて出る**(静的 SSR でも
  `blazor.web.js` が動くので起きる)。app.css は `[tabindex="-1"]` を除いている
- 和文の折り返しは `ch` ではなく `em` で測る。`ch` は "0" の字幅なので、全角が並ぶと
  **想定の半分の位置で折り返す**

## アイコン

ファビコンは**左上のロゴと同じアンテナ**(`Components/Layout/NavMenu.razor` の
`.brand-mark`)を、サイドバーと同じ紺〜紫のグラデーションの角丸プレートに載せたもの。

- `wwwroot/favicon.svg` — 対応ブラウザはこれを使う(拡大しても崩れない)
- `wwwroot/favicon.png`(32px) — fallback。`wwwroot/favicon.ico` は `<link>` を見ずに
  `/favicon.ico` を取りに来る相手向け(中身は同じ 32px の PNG)
- `wwwroot/apple-touch-icon.png`(180px) — iOS のホーム画面追加用。**角丸なしの全面塗り**で
  出す(角丸は OS 側がかけるので、こちらで丸めると二重に丸まって縁が痩せる)。
  ホーム画面での名前はページごとの `<title>` ではなく `apple-mobile-web-app-title` から取らせる

PNG / ICO は `tools/generate-icons.py` で生成する。ImageMagick も PIL も要らないよう、
距離関数でアンチエイリアスをかけて自前でラスタライズし、zlib で PNG を組んでいる
(標準ライブラリのみ)。

```
python3 tools/generate-icons.py src/TechAntenna.Web/wwwroot
```

**形の定義が `favicon.svg` と `tools/generate-icons.py` の 2 か所にある。**
片方だけ変えると SVG と PNG で絵が食い違うので、必ず両方そろえて直すこと。
ロゴ自体(`NavMenu.razor`)を変えたときも同じ。

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
- **compose の db は 7021 をホストへ公開しない**(本番の DB を外に出す理由が無いため)。
  ホストの開発サーバーから実データを読みたいときだけ、上書き定義
  `docker-compose.dev.yml` を重ねて `127.0.0.1:7021` に開ける(手順は README「開発環境」)。
  **開発サーバーも起動時にマイグレーションを自動適用する**ので、未コミットの
  マイグレーションを持ったまま本番の DB へ繋がないこと。SQL で覗くだけなら開ける必要は
  なく、`docker compose exec db psql -U techantenna -d techantenna` で足りる
- **`POSTGRES_PASSWORD` が効くのは `data/postgres` を作る初回起動のときだけ。** 後から
  変えても DB 側は変わらず、接続文字列だけがずれて起動しなくなる。compose には既定値を
  置いてあり、`.env`(standalone は 2 箇所の直書き)で上書きできる
- **テストは PostgreSQL を使わない**(`tests/` が触るのは `InMemory*Store` だけ)。
  DB を上げていなくても `dotnet test` は通る

## 本番の実行形態(Docker)

本番は「コンテナイメージを docker compose で動かす」形態。アプリの構成
(プロジェクト構成・必要な環境変数・待ち受けポート)を変えたら、同じコミットで
`Dockerfile`・`docker-compose.yml`・`.env.example` も追従させる。

イメージは **main への push で GitHub Actions がビルドし GHCR へ公開する**
(`.github/workflows/build-and-push-image.yml`)。タグは `latest` とコミット識別用の
`sha-xxxxxxx`、および `v*` タグを打ったときのバージョン。

**非公開リポジトリでは Actions の実行時間も GHCR のストレージ・転送量もプラン付属の枠を
消費する**(このイメージはベース層だけで圧縮後 90MB/アーキ)。枠を使いすぎないよう、
ワークフローには次の制限を掛けてある。外すときは枠の消費が増えることを承知して外すこと。

- **amd64 のみ**ビルドする。arm64 のネイティブランナーは公開リポジトリでないと無料枠で
  使えず、QEMU 上の `dotnet publish` は極端に遅い。arm64 が要るときは Dockerfile が
  クロスコンパイルに対応しているので手元で `docker buildx build --platform linux/arm64` する
- **`paths-ignore` で文書だけの変更を除外**する(`**.md`・`docker-compose*.yml`・
  `.env.example`)。イメージの中身が変わらない push で実行時間を使わないため
- **`concurrency` で同じ ref の古い実行を打ち切る**
- レイヤーキャッシュ(`type=gha`)を実行間で使い回す

- `Dockerfile` — マルチステージ。`sdk:10.0` で publish し、`aspnet:10.0` に成果物だけを載せて
  非 root(`USER $APP_UID`=1654)で `dotnet TechAntenna.Web.dll` を実行する。HTTP 7020 のみ待ち受け
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
- `docker-compose.yml` — 本番用(`app` + `db` + 起動前に一度だけ走る `init`)。この定義自体はビルドせず GHCR のイメージを
  参照する。設定は `.env` から環境変数で渡す。`docker-compose.build.yml` はその場でビルドする
  上書き定義(`:local` タグ)で、手元での動作確認や GHCR を使わない起動はこちらを重ねて使う
  - Postgres は `postgres:18-alpine`。**18 のイメージは PGDATA が
    `/var/lib/postgresql/18/docker`** で、マウントするのはその1段上の
    `/var/lib/postgresql`(17 以前と位置が違うので、マウント先を変えると初期化し直しになる)
  - **永続データはホストの `data/` へバインドする**(`data/postgres` と `data/keys`)。
    名前付きボリュームを使わないのは、`data/` を丸ごとコピーするだけでバックアップに
    なるようにするため —— Docker の中に置くと持ち出しに一手間かかる。`data/` は
    `.gitignore` 済み。standalone 側は `${...}` を解決できないので、雛形の先頭に
    ホストの絶対パスを書く欄(`x-postgres-dir` / `x-keys-dir`)を置いてある
  - **`init` サービスが `data/keys` を `chown` してから `app` を起動する**
    (`depends_on` の `service_completed_successfully`)。ホストに作られる
    ディレクトリはホストのユーザー所有になるが、`app` はイメージが持つ非 root
    (UID 1654)で動くのでそのままでは鍵を書けない。名前付きボリュームなら Docker が
    見てくれていた部分で、バインドにした代償がここに出る。
    db 側は不要 —— postgres のエントリポイントが root で起動して自分で所有者を揃える。
    `init` に**アプリのイメージではなく `alpine` を使う**のは、`app` を `:local` で
    ビルドする構成のときに GHCR を引きに行かせないため
- `docker-compose.standalone.example.yml` — **`.env` もシェルの環境変数も無い環境**向けの
  単体定義の雛形。管理画面に YAML を貼り付けて起動するタイプ(NAS のコンテナマネージャー等)
  では `${...}` を解決できないため、値を直接書いてある。**`docker-compose.yml` を変えたら
  こちらも追従させること** —— 値が直書きなぶん古くなりやすい。**書き換えが要るのは
  データの置き場だけ**で、そこは `/path/to/…` のプレースホルダーにしてある ——
  特定の NAS 製品の実パスを書くと、そのまま使えるように見えて他の環境で壊れる。
  **リポジトリに置くのは `.example` の付いた雛形だけ**で、実値を入れてコピーした
  `docker-compose.standalone.yml` は `.gitignore` してある(`.env.example` と `.env` の関係と
  同じ。この形式は値を直書きするので、追記した瞬間に秘密がコミット対象に入る)
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

**このアプリのポートは 7020 番台に固める**(開発ポートを 7000 番台へ 10 刻みで割り当てる
運用に合わせたもの。7020〜7029 がこのアプリの枠)。

| 番号 | 用途 |
|---|---|
| 7020 | アプリ。`dotnet run`・コンテナ内・compose の公開ポート(`PORT` の既定)すべて同じ |
| 7021 | PostgreSQL。コンテナ内も `PGPORT` で 7021(`docker-compose.dev.yml` で開くときも同じ) |
| 7022 | `dotnet watch`(ホットリロード) |
| 7023 | `https` プロファイルの HTTPS(開発でだけ使う) |

**ホスト側とコンテナ内の番号をそろえてある** —— compose の `"7020:7020"` を読むだけで
対応が分かるようにするため。アプリ側はベースイメージの既定(8080)を Dockerfile の
`ASPNETCORE_HTTP_PORTS=7020` で上書きし、DB 側は `PGPORT` で移している(`PGPORT` は
サーバーもクライアントも読むので、`pg_isready` や `psql` に `-p` を足す必要はない)。

同じホストで開発サーバーと本番コンテナを同時に上げることはできない(片方の `PORT` を
変える)。**`watch` プロファイルだけ 7022** なのは、本番同等のコンテナ(7020)を上げたまま
並べられるようにするため。**6000 番台は使わない** —— Chrome/Firefox が X11 用ポートとして
拒否し(`ERR_UNSAFE_PORT`)ブラウザから開けなくなるため。

## ホットリロード(開発中)

`dotnet watch` が `.razor` / `.razor.css` / `.cs` の変更を拾い、**プロセスを再起動せずに**
反映する。ブラウザ側も自動で更新される —— Development では ASP.NET Core が
`aspnetcore-browser-refresh.js` を注入するため(注入されるのは `Accept: text/html` の
リクエストだけなので、`curl` で確かめるときはヘッダを付ける)。

```
dotnet watch --project src/TechAntenna.Web --launch-profile watch
# → http://localhost:7022
```

- **効く**: Razor のマークアップ、scoped CSS(`*.razor.css`)、メソッド本体の C#
- **効かない**: DI の登録・`Program.cs` の構成・型の追加といった rude edit。
  この場合は watch がビルドし直してアプリを再起動する(数秒かかる)
- **コンテナには効かない。** 動いているコンテナに反映するにはイメージを作り直す
  (`docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`)
- 接続文字列を渡さなければ In-Memory ストアで起動するので **DB 無しで画面を触れる**
  (データは空。compose の db は 7021 をホストへ公開していないので、実データを見るならコンテナ側)
- **始める前に `free -h` を見る。** ビルドとコンテナを同時に走らせるとメモリを使い切り、
  swap の無い環境では OOM でホストごと巻き込まれる

## コマンド

- ビルド: `dotnet build`
- テスト: `dotnet test`
- Web 起動: `dotnet run --project src/TechAntenna.Web`(http://localhost:7020)
- ホットリロード付きで起動:
  `dotnet watch --project src/TechAntenna.Web --launch-profile watch`(http://localhost:7022)
- 本番同等の起動(GHCR から pull): `docker compose pull && docker compose up -d`
- 本番同等の起動(手元でビルド):
  `docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`
