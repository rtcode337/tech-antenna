// タグの画面(/tags)の「この一覧を全選択」。
//
// 上乗せの機能で、JS が動かなくても困らない —— 1 語ずつのチェックボックスと
// 「チェックした語を除外する」は素の HTML フォームだけで成立している。ここが足すのは
// 「300 語を手で 1 つずつ押さなくてよくする」ぶんだけ。
//
// 全ページ静的 SSR なので対話回線は張らない。enhanced navigation の後にも当て直す ——
// blazor.web.js を読んでいるとリンクの遷移で DOM だけ差し替わり、スクリプトは再実行されない。
(function () {
    function apply() {
        document.querySelectorAll('.tag-group .select-all').forEach(function (toggle) {
            // enhanced navigation のたびに呼ばれるので、二重に登録しない
            if (toggle.dataset.tagSelect) {
                return;
            }

            toggle.dataset.tagSelect = '1';
            toggle.addEventListener('change', function () {
                // 開いている一覧の中だけを対象にする(details をまたいで選ぶと、
                // 見えていない語まで除外することになる)
                var group = toggle.closest('.tag-group');
                if (!group) {
                    return;
                }

                group.querySelectorAll('input[type="checkbox"][name="Selected"]')
                    .forEach(function (box) {
                        box.checked = toggle.checked;
                    });
            });
        });
    }

    // defer 付きで読むので、この時点で DOM は組み上がっている
    apply();

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
                // Blazor が使えない構成でも、通常の遷移なら apply() だけで足りる
                console.debug('tag-select: Blazor の enhancedload には接続しなかった');
            }
        });
    }
})();
