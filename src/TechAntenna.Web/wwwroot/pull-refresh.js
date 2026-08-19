// PWA(ホーム画面から起動した状態)で、画面の上端から下へ引っ張ったら読み込み直す。
//
// 入れる理由は iOS。ブラウザのタブなら引っ張り更新はブラウザが持っているが、
// iOS のホーム画面から起動した表示には無い(戻る/進むが無いのと同じ事情。
// history-nav.js 参照)。Android の PWA は標準で持っているので、こちらを有効にした
// ときは 標準の側を止める(CSS の `overscroll-behavior-y: contain`)——
// 両方が生きていると、1回の操作で二重に読み込み直すことになる。
//
// 出すのはスタンドアロン表示のときだけ。タブで開いているときは何もしない
// (ブラウザの引っ張り更新をそのまま使う)。
//
// 全ページ静的 SSR なので対話回線は張らない。登録は document に1回だけで、
// enhanced navigation で本文が差し替わっても効き続ける(nav-menu.js と同じ理由)。
(function () {
    // これだけ引いたら更新する(px)。短すぎると横スクロールや誤操作で発火する
    var THRESHOLD = 70;
    // 指の動きに対して印を動かす割合。1 のままだと指に貼り付いて重く見える
    var DAMPING = 0.5;

    function isStandalone() {
        try {
            var modes = ['standalone', 'minimal-ui', 'fullscreen'];
            for (var i = 0; i < modes.length; i++) {
                if (window.matchMedia('(display-mode: ' + modes[i] + ')').matches) {
                    return true;
                }
            }
        } catch (e) {
            // matchMedia が使えない環境では iOS の印だけで判断する
        }

        return window.navigator.standalone === true;
    }

    if (!isStandalone() || !('ontouchstart' in window)) {
        return;
    }

    var indicator = document.createElement('div');
    indicator.className = 'pull-refresh';
    // 読み上げには出さない(操作の途中の状態を読み上げても意味が無い)
    indicator.setAttribute('aria-hidden', 'true');
    indicator.textContent = '引っ張って更新';
    document.body.appendChild(indicator);

    var startY = null;
    var startX = 0;
    var pulled = 0;
    var refreshing = false;

    function show(distance) {
        var ready = distance >= THRESHOLD;
        indicator.textContent = ready ? '離して更新' : '引っ張って更新';
        indicator.classList.toggle('ready', ready);
        indicator.style.transform = 'translate(-50%, ' + Math.min(distance * DAMPING, 90) + 'px)';
        indicator.classList.add('visible');
    }

    function hide() {
        indicator.classList.remove('visible', 'ready');
        indicator.style.transform = '';
    }

    document.addEventListener('touchstart', function (event) {
        // 上端にいるときだけ受ける。途中から引いても更新しない(単なるスクロール)
        if (refreshing || event.touches.length !== 1 || window.scrollY > 0) {
            startY = null;
            return;
        }

        startY = event.touches[0].clientY;
        startX = event.touches[0].clientX;
        pulled = 0;
    }, { passive: true });

    document.addEventListener('touchmove', function (event) {
        if (startY === null || event.touches.length !== 1) {
            return;
        }

        var dy = event.touches[0].clientY - startY;
        var dx = Math.abs(event.touches[0].clientX - startX);
        // 縦の動きだけを拾う。横に払ったとき(表の横スクロール等)に反応しない
        if (dy <= 0 || dx > Math.abs(dy)) {
            if (pulled > 0) {
                hide();
            }

            pulled = 0;
            return;
        }

        pulled = dy;
        show(dy);
    }, { passive: true });

    document.addEventListener('touchend', function () {
        if (startY === null) {
            return;
        }

        var reached = pulled >= THRESHOLD;
        startY = null;
        pulled = 0;

        if (!reached) {
            hide();
            return;
        }

        // 押し込んだ位置に印を残したまま読み込み直す(消してから待たせると、
        // 反応しなかったように見える)
        refreshing = true;
        indicator.textContent = '更新中…';
        location.reload();
    }, { passive: true });

    // 標準の引っ張り更新を止めるのはここまで来たときだけ。
    // 先に止めて JS が動かないと、どちらの手段も無くなる
    document.documentElement.classList.add('pull-refresh-on');
})();
