// スマホ表示のメニュー(ハンバーガー)の開閉を、ページ遷移をまたいで扱う。
//
// **リンクで移ったら閉じる。** 開いたままだと遷移先の本文がメニューに隠れ、読むたびに
// 閉じる操作が要る。**閉じるのはリンクを押したときだけ**で、グループの開閉(summary)や
// メニューの外側では閉じない —— 子を探している最中に閉じられると困る。
//
// メニューの開閉は CSS だけで組んである(チェックボックス `.navbar-toggler` の :checked)。
// 開閉の状態は DOM にしか無いので、**ページが差し替わると閉じた状態に戻る**。
// メニューから続けて別のページへ移りたいのに毎回開き直すことになるため、
// 状態を sessionStorage に覚えて復元する。
//
// **復元は2か所で要る。**
//   1. 通常の読み込み(このスクリプトが走るとき)
//   2. **Blazor の enhanced navigation の後**。blazor.web.js を読んでいるとリンクの遷移が
//      横取りされ、ページ全体を読み直さずに DOM だけ差し替わる。スクリプトは再実行されず、
//      チェックボックスはサーバーが返した「未チェック」の markup で上書きされるので、
//      `enhancedload` を拾って当て直す必要がある
//
// タブを閉じたら忘れてよいので localStorage ではなく sessionStorage。
// PC 表示ではチェックボックス自体が display:none で、開閉に関係しない。
(function () {
    var key = 'tech-antenna:nav-open';

    function isOpen() {
        try {
            return sessionStorage.getItem(key) === '1';
        } catch (e) {
            // プライベートモード等で sessionStorage が使えない場合は、復元しないだけ
            return false;
        }
    }

    function remember(open) {
        try {
            sessionStorage.setItem(key, open ? '1' : '0');
        } catch (e) {
            // 保存できなくても開閉自体は動く
        }
    }

    function apply() {
        var toggler = document.getElementById('navbar-toggler');
        if (!toggler) {
            return;
        }

        toggler.checked = isOpen();

        // enhanced navigation のたびに呼ばれるので、二重に登録しない
        if (!toggler.dataset.navRestore) {
            toggler.dataset.navRestore = '1';
            toggler.addEventListener('change', function () {
                remember(toggler.checked);
            });
        }
    }

    // **リンクで移ったらメニューを閉じる。**
    //
    // 押した時点で閉じ、記憶も閉じた状態にする —— 遷移先で apply() が復元するので、
    // 記憶を更新しないと開き直ってしまう。
    // **document に1回だけ付ける**(メニューの DOM は enhanced navigation で差し替わるので、
    // メニュー側に付けると遷移のたびに付け直しが要る)。
    // PC 幅ではチェックボックスが効かない(display:none)ので、閉じても見た目は変わらない。
    document.addEventListener('click', function (event) {
        var link = event.target.closest && event.target.closest('.nav-scrollable a');
        if (!link) {
            return;
        }

        var toggler = document.getElementById('navbar-toggler');
        if (toggler) {
            toggler.checked = false;
        }
        remember(false);
    });

    // defer 付きで読むので、この時点で DOM は組み上がっている
    apply();

    // enhanced navigation 後にも当て直す。blazor.web.js の読み込み順に左右されないよう、
    // Blazor が使えるようになってから登録する
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
                console.debug('nav-menu: Blazor の enhancedload には接続しなかった');
            }
        });
    }
})();
