// PWA(ホーム画面から起動した状態)で消えるブラウザの「戻る/進む」を、画面の中に置き直す。
//
// **出すのはスタンドアロン表示のときだけ。** ブラウザのタブで開いているときは
// アドレスバーの戻る/進むがあるので、同じものを二重に見せない。
// 判定は display-mode(標準)と navigator.standalone(iOS の古い印)の両方を見る。
//
// **ボタンは hidden 相当(CSS)で描いておき、ここで出す** —— 押しても何も起きない
// ボタンを見せないため(history を動かすので JS が要る)。
//
// 全ページ静的 SSR なので対話回線は張らない。**enhanced navigation の後にも当て直す** ——
// blazor.web.js を読んでいるとリンクの遷移で DOM だけ差し替わり、スクリプトは再実行されない。
// (html の class は差し替わらないので、当て直すのはボタンの登録だけ)
(function () {
    function isStandalone() {
        try {
            // display-mode: minimal-ui / fullscreen もブラウザの戻る進むが無い(または隠れる)
            var modes = ['standalone', 'minimal-ui', 'fullscreen'];
            for (var i = 0; i < modes.length; i++) {
                if (window.matchMedia('(display-mode: ' + modes[i] + ')').matches) {
                    return true;
                }
            }
        } catch (e) {
            // matchMedia が使えない環境では iOS の印だけで判断する
        }

        // iOS のホーム画面から起動したとき(display-mode を返さない版がある)
        return window.navigator.standalone === true;
    }

    function apply() {
        // 表示の可否は html の class で決める(CSS 側は .standalone のときだけ出す)
        document.documentElement.classList.toggle('standalone', isStandalone());

        bind('.history-back', function () { history.back(); });
        bind('.history-forward', function () { history.forward(); });
    }

    function bind(selector, go) {
        document.querySelectorAll(selector).forEach(function (button) {
            // enhanced navigation のたびに呼ばれるので、二重に登録しない
            if (button.dataset.historyNav) {
                return;
            }

            button.dataset.historyNav = '1';
            // **行き先が無いときに無効化はしない。** 履歴のどこにいるかを知る API が無く、
            // history.length では「これ以上戻れるか」を判定できない(戻っても減らない)。
            // 押しても動かないことはあるが、押せるように見えて動かないのは
            // ブラウザの戻る進むも同じなので、状態を偽って出すよりよい
            button.addEventListener('click', go);
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
                console.debug('history-nav: Blazor の enhancedload には接続しなかった');
            }
        });
    }
})();
