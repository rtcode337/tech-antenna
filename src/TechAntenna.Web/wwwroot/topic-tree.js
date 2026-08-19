// トピックのツリー(/topics)の見え方と操作を補う。JS が無くても一覧の機能は成立する
// (開閉は details/summary)ので、ここは上乗せ。
//
//   1. 開閉状態をブラウザに覚えて、次に開いたとき再現する
//
// チェックを配下へ広げる cascade は持たない。収集対象はチェックした
// トピックだけで、配下は表示・強調の側が広げる(保存も広げない)ため、
// 画面でチェックが広がって見えると保存結果と食い違う。
//
// 持つのは既定との差分だけ。既定はサーバー側のマークアップが決めていて
// (根は畳む・中は開く)、ここで覚えるのは「ユーザーが自分で開閉したノード」に限る ——
// 「閉じたノードの一覧」を持つ以前の作りだと、既定で畳んである根を毎回開いてしまっていた。
// 保存先は localStorage(トピックの一覧は何度も開き直すので、タブを閉じても覚えておきたい。
// nav-menu の sessionStorage とは寿命の要件が違う)。
//
// 復元は2か所で要るのは nav-menu.js と同じ事情:
//   1. 通常の読み込み(このスクリプトが走るとき)
//   2. Blazor の enhanced navigation の後(DOM だけ差し替わり、スクリプトは再実行されない)
(function () {
    var key = 'tech-antenna:topic-tree-closed';

    // { タグ: true(開いた) / false(閉じた) }。ここに無いノードは既定のまま
    function loadState() {
        try {
            var stored = JSON.parse(localStorage.getItem(key) || '{}');
            if (Array.isArray(stored)) {
                // 以前の形式(閉じたタグの配列)からの引き継ぎ
                var migrated = {};
                stored.forEach(function (tag) { migrated[tag] = false; });
                return migrated;
            }

            return stored && typeof stored === 'object' ? stored : {};
        } catch (e) {
            // プライベートモード等で localStorage が使えない場合は、復元しないだけ
            return {};
        }
    }

    function saveState(state) {
        try {
            localStorage.setItem(key, JSON.stringify(state));
        } catch (e) {
            // 保存できなくても開閉自体は動く
        }
    }

    function apply() {
        var nodes = document.querySelectorAll('.topic-tree details[data-tag]');
        if (nodes.length === 0) {
            return;
        }

        var state = loadState();
        nodes.forEach(function (node) {
            // 先に状態を当ててからリスナーを付ける(当てた瞬間の toggle で保存が走らないように)
            // 検索で開いた枝(data-force-open)は復元しない —— 以前に畳んだ枝が
            // 閉じ直されると、検索で当たった行が隠れてしまう
            var remembered = state[node.dataset.tag];
            if (typeof remembered === 'boolean' && node.dataset.forceOpen !== '1') {
                node.open = remembered;
            }

            // enhanced navigation のたびに呼ばれるので、二重に登録しない
            if (!node.dataset.treeRestore) {
                node.dataset.treeRestore = '1';
                node.addEventListener('toggle', function () {
                    var current = loadState();
                    current[node.dataset.tag] = node.open;
                    saveState(current);
                });
            }
        });
    }

    // 説明チップ(popover)の「?」は summary の中にも置くので、クリックが summary へ
    // 伝わると折りたたみまで動いてしまう。チップを出すだけにするため伝播を止める
    // (popover 自体は JS 無しで動く。ここは操作の取り違えを防ぐだけの上乗せ)
    function isolateNoteToggles() {
        document.querySelectorAll('.topic-tree .note-toggle').forEach(function (button) {
            if (button.dataset.isolated) {
                return;
            }

            button.dataset.isolated = '1';
            button.addEventListener('click', function (event) {
                event.stopPropagation();
            });
        });
    }

    // defer 付きで読むので、この時点で DOM は組み上がっている
    apply();
    isolateNoteToggles();

    // enhanced navigation 後にも当て直す(登録の仕方は nav-menu.js と同じ)
    function hookBlazor() {
        if (window.Blazor && typeof Blazor.addEventListener === 'function') {
            Blazor.addEventListener('enhancedload', function () {
                apply();
                isolateNoteToggles();
            });
            return true;
        }

        return false;
    }

    if (!hookBlazor()) {
        window.addEventListener('load', function () {
            if (!hookBlazor()) {
                console.debug('topic-tree: Blazor の enhancedload には接続しなかった');
            }
        });
    }
})();
