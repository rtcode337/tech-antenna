// 押したボタンの位置に留まる。
//
// **なぜ要るか。** 全ページ静的 SSR なので、収集元の「止める / 動かす」も
// 外部連携のキーの保存もフォームの POST で、押すと画面が作り直される
// (enhanced navigation でも先頭へ戻り、`FocusOnNavigate` が h1 へ焦点を移す)。
// 収集の表は数十行あるので、**押した行が画面外へ消えて「どうなったか」が読めない**。
//
// **やり方は「同じボタンへ焦点を戻す」**。スクロール位置を数値で覚える手もあるが、
// 行が増減すると別の行の位置になる —— ボタンの `value`(設定パスが入っていて一意)で
// 探し直せば、行が動いてもそのボタンのところへ戻る。焦点を当てると
// ブラウザがその要素まで送ってくれるので、位置の計算も要らない。
//
// **上乗せの機能で、動かなくても操作はできる**(先頭に戻るだけ)。
(function () {
    var KEY = 'keep-focus';

    // 押されたボタンを覚える。**submit の時点で拾う** —— クリックの後に
    // DOM が差し替わると、どれが押されたのか分からなくなる
    document.addEventListener('click', function (event) {
        // クリックの的が要素でないことがある(document への合成イベント等)
        if (!(event.target instanceof Element)) {
            return;
        }

        var button = event.target.closest('button[name="Action"][value], button[type="submit"][value]');
        if (!button || !button.closest('form[data-keep-focus]')) {
            return;
        }

        try {
            sessionStorage.setItem(KEY, JSON.stringify({
                path: location.pathname,
                value: button.value,
            }));
        } catch (e) {
            // プライベートモード等で保存できなくても、操作そのものは成立する
        }
    }, true);

    function restore() {
        var saved;
        try {
            saved = JSON.parse(sessionStorage.getItem(KEY) || 'null');
        } catch (e) {
            saved = null;
        }
        if (!saved || saved.path !== location.pathname) {
            return;
        }

        // **一度きり。** 残すと、次に同じページを普通に開いたときにも飛ぶ
        try {
            sessionStorage.removeItem(KEY);
        } catch (e) { /* 消せなくても以下は動く */ }

        var target = document.querySelector('[value="' + CSS.escape(saved.value) + '"]');
        if (!target) {
            return;
        }

        // **少し待ってから当てる。** `FocusOnNavigate` が h1 に焦点を移すので、
        // 同じタイミングで当てると先頭へ引き戻される
        setTimeout(function () {
            target.focus({ preventScroll: true });
            target.scrollIntoView({ block: 'center' });
        }, 0);
    }

    restore();

    function hookBlazor() {
        if (window.Blazor && typeof Blazor.addEventListener === 'function') {
            Blazor.addEventListener('enhancedload', restore);

            return true;
        }

        return false;
    }

    if (!hookBlazor()) {
        window.addEventListener('load', hookBlazor);
    }
})();
