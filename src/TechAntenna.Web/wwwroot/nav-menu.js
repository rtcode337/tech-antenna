// スマホ表示のメニュー(ハンバーガー)の開閉状態を、ページ遷移をまたいで保つ。
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
