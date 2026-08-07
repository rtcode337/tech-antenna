// トピックのツリー(/topics)の見え方と操作を補う。JS が無くても一覧の機能は成立する
// (開閉は details/summary、選択の保存はサーバー側で配下へ広げる)ので、ここは上乗せ。
//
//   1. 開閉状態をブラウザに覚えて、次に開いたとき再現する
//   2. 親のチェックを配下のトピックへ広げる(保存時にサーバーが広げるのと同じ結果を先に見せる)
//
// 既定は**全て開いた状態**で、ユーザーが閉じたノードだけを localStorage に持つ
// (トピックの一覧は何度も開き直すものなので、タブを閉じても覚えておきたい ——
// nav-menu の sessionStorage とは寿命の要件が違う)。
//
// **復元は2か所で要る**のは nav-menu.js と同じ事情:
//   1. 通常の読み込み(このスクリプトが走るとき)
//   2. Blazor の enhanced navigation の後(DOM だけ差し替わり、スクリプトは再実行されない)
(function () {
    var key = 'tech-antenna:topic-tree-closed';

    function loadClosed() {
        try {
            return new Set(JSON.parse(localStorage.getItem(key) || '[]'));
        } catch (e) {
            // プライベートモード等で localStorage が使えない場合は、復元しないだけ
            return new Set();
        }
    }

    function saveClosed(closed) {
        try {
            localStorage.setItem(key, JSON.stringify(Array.from(closed)));
        } catch (e) {
            // 保存できなくても開閉自体は動く
        }
    }

    function apply() {
        var nodes = document.querySelectorAll('.topic-tree details[data-tag]');
        if (nodes.length === 0) {
            return;
        }

        var closed = loadClosed();
        nodes.forEach(function (node) {
            // 先に状態を当ててからリスナーを付ける(当てた瞬間の toggle で保存が走らないように)
            node.open = !closed.has(node.dataset.tag);

            // enhanced navigation のたびに呼ばれるので、二重に登録しない
            if (!node.dataset.treeRestore) {
                node.dataset.treeRestore = '1';
                node.addEventListener('toggle', function () {
                    var set = loadClosed();
                    if (node.open) {
                        set.delete(node.dataset.tag);
                    } else {
                        set.add(node.dataset.tag);
                    }

                    saveClosed(set);
                });
            }
        });
    }

    // 親のチェックを配下へ広げる。**保存の正体はサーバー側**(ExpandWithDescendants)で、
    // ここでやるのは「保存したらこうなる」を押した瞬間に見せること。
    // 外す方向も配下へ広げる —— 親を外したのに子が残ると、消したつもりの収集が続く
    function cascade() {
        var nodes = document.querySelectorAll('.topic-tree details[data-tag]');
        nodes.forEach(function (node) {
            var box = node.querySelector('summary input[name="SelectedTopics"]');
            if (!box || box.dataset.cascade) {
                return;
            }

            box.dataset.cascade = '1';
            box.addEventListener('change', function () {
                // summary の外(= 配下の行)のチェックボックスだけを揃える
                node.querySelectorAll('input[name="SelectedTopics"]').forEach(function (child) {
                    if (child !== box) {
                        child.checked = box.checked;
                    }
                });
            });
        });
    }

    // 説明チップ(popover)の「?」は summary の中にも置くので、クリックが summary へ
    // 伝わると折りたたみまで動いてしまう。**チップを出すだけにする**ため伝播を止める
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
    cascade();
    isolateNoteToggles();

    // enhanced navigation 後にも当て直す(登録の仕方は nav-menu.js と同じ)
    function hookBlazor() {
        if (window.Blazor && typeof Blazor.addEventListener === 'function') {
            Blazor.addEventListener('enhancedload', function () {
                apply();
                cascade();
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
