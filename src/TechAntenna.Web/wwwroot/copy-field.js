// 入力欄の値をクリップボードへ写す「コピー」ボタン(外部連携の ntfy トピック名など)。
//
// 上乗せの機能で、JS が動かなくても困らない —— 値は入力欄に出ているので、
// 手で選んでコピーすればよい。そのためボタンは hidden で描き、ここで出す ——
// 押しても何も起きないボタンを見せないため。
//
// 全ページ静的 SSR なので対話回線は張らない。enhanced navigation の後にも当て直す ——
// blazor.web.js を読んでいるとリンクの遷移やフォームの POST で DOM だけ差し替わり、
// スクリプトは再実行されない。
(function () {
    var restoreMs = 1500;

    function copy(input) {
        var text = input.value;
        if (!text) {
            return Promise.reject(new Error('empty'));
        }

        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }

        // navigator.clipboard は secure context(https / localhost)でしか生えない。
        // LAN の http で開いているときはここに落ちるので、入力欄を選択して旧 API に渡す
        input.focus();
        input.select();
        var copied = document.execCommand && document.execCommand('copy');

        return copied ? Promise.resolve() : Promise.reject(new Error('execCommand'));
    }

    function flash(button, label) {
        if (button.dataset.copyLabel === undefined) {
            button.dataset.copyLabel = button.textContent;
        }

        button.textContent = label;
        window.clearTimeout(Number(button.dataset.copyTimer));
        button.dataset.copyTimer = window.setTimeout(function () {
            button.textContent = button.dataset.copyLabel;
        }, restoreMs);
    }

    function apply() {
        document.querySelectorAll('button.copy-field').forEach(function (button) {
            // JS が動いた環境でだけ見せる
            button.hidden = false;

            // enhanced navigation のたびに呼ばれるので、二重に登録しない
            if (button.dataset.copyField) {
                return;
            }

            button.dataset.copyField = '1';
            button.addEventListener('click', function () {
                var input = document.getElementById(button.dataset.copyTarget);
                if (!input) {
                    return;
                }

                copy(input).then(
                    function () { flash(button, 'コピー済み'); },
                    function () { flash(button, 'コピー不可'); });
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
                console.debug('copy-field: Blazor の enhancedload には接続しなかった');
            }
        });
    }
})();
