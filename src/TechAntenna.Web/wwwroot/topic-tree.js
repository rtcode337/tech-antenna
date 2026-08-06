// トピックのツリー(/topics)の開閉状態をブラウザに覚えて、次に開いたとき再現する。
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

    // defer 付きで読むので、この時点で DOM は組み上がっている
    apply();

    // enhanced navigation 後にも当て直す(登録の仕方は nav-menu.js と同じ)
    function hookBlazor() {
        if (window.Blazor && typeof Blazor.addEventListener === 'function') {
            Blazor.addEventListener('enhancedload', apply);
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
