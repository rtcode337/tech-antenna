// 興味トピックの並び(/interests/books のグループ)を、見出しを長押ししてドラッグで
// 並べ替える。離した時点で `POST /api/topics/order` に丸ごと保存する。
//
// 上乗せの機能で、JS が動かなくても困らない —— 各グループには ▲▼ のボタンがあり、
// 素の HTML フォームだけで 1 つずつ動かせる。ここが足すのは「10 個並んだトピックを
// 一息で好きな順にする」ぶんだけ。だから JS が動いたときだけ ▲▼ を引っ込め、
// 失敗したときは出し直す —— 保存する手段を画面から消さないこと。
//
// **掴み代(☰)を置かず、行のどこを押しても掴める。** 代わりに、
// - 長押しの成立前に指が動いたら、ただのスクロールとして見送る
// - 成立したら軽く振動させ、以降ページはスクロールさせない
// - 指を離した直後の click(折りたたみの開閉)は 1 回だけ握りつぶす
// の 3 つが要る。掴み代を置くと、スマホでは指で押すには小さすぎる的になる。
//
// 全ページ静的 SSR なので対話回線は張らない。enhanced navigation 後にも当て直す
// (topic-select.js と同じ流儀)。
(function () {
    var endpoint = '/api/topics/order';
    // これだけ押し続けたらドラッグ開始。これより短い押下は開閉のクリックとして通す(ms)
    var LONG_PRESS_MS = 400;
    // 長押しの成立前に指がこれ以上動いたら「スクロールしたいのだ」と判断して中止する(px)
    var MOVE_TOLERANCE = 8;
    // ドラッグ直後の click を握りつぶす保険の有効時間(ms)
    var CLICK_GUARD_MS = 400;

    var pending = null;  // 長押し待ち。まだドラッグではない
    var drag = null;     // ドラッグ中の状態
    var swallowClick = false;
    var guardTimer = 0;

    function rows(form) {
        return Array.prototype.slice.call(form.querySelectorAll('[data-topic-row]'));
    }

    function status(form) {
        return form.querySelector('[data-order-status]');
    }

    function show(form, message, isError) {
        var box = status(form);
        if (!box) {
            return;
        }

        box.textContent = message;
        box.hidden = false;
        box.classList.toggle('error', !!isError);

        if (box.dataset.timer) {
            clearTimeout(Number(box.dataset.timer));
            delete box.dataset.timer;
        }

        // 成功の知らせは残さない(動かすたびに出るので居座ると邪魔)。
        // 失敗は残す —— 気づかないまま「保存された」と思われるほうが困る
        if (!isError) {
            box.dataset.timer = String(setTimeout(function () {
                box.hidden = true;
            }, 2500));
        }
    }

    function token(form) {
        var field = form.querySelector('input[name="__RequestVerificationToken"]');

        return field ? field.value : '';
    }

    // JS が効いている間は ▲▼ を出さない(ドラッグで足りるため)。
    // 失敗したら出し直して、従来どおり 1 つずつ動かせるようにする
    function moveButtons(form, hidden) {
        form.querySelectorAll('[data-topic-move]').forEach(function (box) {
            box.hidden = hidden;
        });
    }

    function save(form) {
        var keys = rows(form).map(function (row) { return row.dataset.topicRow; });
        var body = new FormData();
        keys.forEach(function (key) { body.append('keys', key); });
        body.append('__RequestVerificationToken', token(form));

        show(form, '保存中…', false);

        fetch(endpoint, { method: 'POST', body: body, credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) {
                    return response.json()
                        .catch(function () { return {}; })
                        .then(function (payload) {
                            throw new Error(payload.error || ('保存できませんでした（HTTP ' + response.status + '）。'));
                        });
                }

                return response.json();
            })
            .then(function () {
                show(form, 'トピックの並びを保存しました。', false);
            })
            .catch(function (error) {
                // **並びを戻さない。** 画面の見た目だけ元に戻すと、どこへ動かしたかを
                // 覚え直して操作をやり直すことになる —— 代わりに ▲▼ を出し直して、
                // そちらで保存できるようにする(読み込み直せばサーバーの並びに戻る)
                moveButtons(form, false);
                show(form, error.message + '▲▼ で動かし直すか、読み込み直してください。', true);
            });
    }

    // 長押しが成立したあとはページをスクロールさせない。touch-action はジェスチャの
    // 開始時に確定してしまい後から効かせられないので、touchmove を非パッシブで止める
    function preventTouchScroll(event) {
        event.preventDefault();
    }

    function start(event) {
        if (pending || drag) {
            return;  // 2 本目の指は無視する
        }

        if (event.pointerType === 'mouse' && event.button !== 0) {
            return;
        }

        var summary = event.target.closest('summary');
        if (!summary || event.target.closest('[data-no-drag]')) {
            return;  // ▲▼ を押したときは掴まない
        }

        var row = summary.parentElement;
        var form = row && row.closest('form[data-topic-order]');
        if (!row || !row.dataset.topicRow || !form) {
            return;
        }

        pending = {
            form: form,
            row: row,
            x: event.clientX,
            y: event.clientY,
            timer: setTimeout(begin, LONG_PRESS_MS)
        };

        // 押している間は document で追う。行に張ると、並び替えで行を動かした瞬間に
        // ポインタキャプチャが解放されて以降のイベントを取りこぼす
        document.addEventListener('pointermove', move);
        document.addEventListener('pointerup', end);
        document.addEventListener('pointercancel', end);
    }

    function unwatch() {
        document.removeEventListener('pointermove', move);
        document.removeEventListener('pointerup', end);
        document.removeEventListener('pointercancel', end);
    }

    function cancelPending() {
        if (!pending) {
            return;
        }

        clearTimeout(pending.timer);
        pending = null;
        unwatch();
    }

    // 長押し成立。ここからが本当のドラッグ
    function begin() {
        var p = pending;
        if (!p) {
            return;
        }

        pending = null;
        drag = {
            form: p.form,
            row: p.row,
            // 掴んだ位置が行の上端からどれだけ下か。ここを指に合わせ続ける
            grabOffsetY: p.y - p.row.getBoundingClientRect().top,
            translateY: 0,
            // 元の並び。変わっていなければ保存しない
            original: rows(p.form).map(function (row) { return row.dataset.topicRow; })
        };

        // 引っ張って更新(pull-refresh.js)と食い合うので、ドラッグ中だと分かるようにする
        document.documentElement.dataset.dragging = '1';
        document.addEventListener('touchmove', preventTouchScroll, { passive: false });
        document.body.style.userSelect = 'none';
        document.body.style.cursor = 'grabbing';
        // マウスで押し込んでいる間に走った選択は消しておく
        var selection = document.getSelection();
        if (selection) {
            selection.removeAllRanges();
        }

        if (navigator.vibrate) {
            navigator.vibrate(10);
        }

        p.row.classList.add('dragging');
        // 掴んだ行を持ち上げて、以降は指に追従させる
        p.row.style.position = 'relative';
        p.row.style.zIndex = '2';
        p.row.style.willChange = 'transform';
        // 指の下にあるのが「運んでいる行」自身にならないようにする
        // (イベントは document で受けるので影響しない)
        p.row.style.pointerEvents = 'none';
    }

    // 掴んでいる行を指の位置へ貼り付ける。並び替えで行のレイアウト位置が変わるたびに
    // 当て直すので、「レイアウト上の位置(= いまの rect から今の translate を引いた値)」
    // を基準に計算する
    function follow(pointerY) {
        if (!drag) {
            return;
        }

        var layoutTop = drag.row.getBoundingClientRect().top - drag.translateY;
        var translateY = pointerY - (layoutTop + drag.grabOffsetY);
        drag.row.style.transform = 'translateY(' + translateY + 'px)';
        drag.translateY = translateY;
    }

    function move(event) {
        if (pending) {
            // 長押しの成立前に動いた = スクロールしたいのでドラッグにしない
            if (Math.abs(event.clientX - pending.x) > MOVE_TOLERANCE ||
                Math.abs(event.clientY - pending.y) > MOVE_TOLERANCE) {
                cancelPending();
            }

            return;
        }

        if (!drag) {
            return;
        }

        // ポインタの Y がどの行の中心線より上かで挿入先を決める。掴んでいる行は
        // 指に追従して変位しているので、レイアウト上の位置に戻して測る
        var list = rows(drag.form);
        var target = null;
        for (var i = 0; i < list.length; i++) {
            var rect = list[i].getBoundingClientRect();
            var top = list[i] === drag.row ? rect.top - drag.translateY : rect.top;
            if (event.clientY < top + rect.height / 2) {
                target = list[i];
                break;
            }
        }

        if (target !== drag.row) {
            // DOM を動かすのがそのまま並び替え(保存もここから読む)
            if (target) {
                drag.row.parentElement.insertBefore(drag.row, target);
            } else {
                drag.row.parentElement.appendChild(drag.row);
            }
        }

        follow(event.clientY);
    }

    function end() {
        if (pending) {
            // 長押しの成立前に離した = ただのクリック。開閉はそのまま通す
            cancelPending();

            return;
        }

        if (!drag) {
            return;
        }

        var state = drag;
        drag = null;

        // 持ち上げを戻す(保存の成否によらず、掴んでいた見た目は必ず解除する)
        state.row.classList.remove('dragging');
        state.row.style.transform = '';
        state.row.style.position = '';
        state.row.style.zIndex = '';
        state.row.style.willChange = '';
        state.row.style.pointerEvents = '';
        delete document.documentElement.dataset.dragging;
        document.removeEventListener('touchmove', preventTouchScroll);
        document.body.style.userSelect = '';
        document.body.style.cursor = '';
        unwatch();

        // 離した直後に飛んでくる click(折りたたみの開閉)を止める
        swallowClick = true;
        clearTimeout(guardTimer);
        guardTimer = setTimeout(function () { swallowClick = false; }, CLICK_GUARD_MS);

        var now = rows(state.form).map(function (row) { return row.dataset.topicRow; });
        var changed = now.some(function (key, i) { return key !== state.original[i]; });
        if (changed) {
            save(state.form);
        }
    }

    function guardClick(event) {
        if (!swallowClick) {
            return;
        }

        swallowClick = false;
        event.preventDefault();
        event.stopPropagation();
    }

    function apply() {
        document.querySelectorAll('form[data-topic-order]').forEach(function (form) {
            moveButtons(form, true);
        });
    }

    // **登録は document に 1 回だけ。** グループの DOM は enhanced navigation や
    // POST のたびに差し替わるので、行に張ると付け直しが要る(nav-menu.js と同じ流儀)
    document.addEventListener('pointerdown', start);
    document.addEventListener('click', guardClick, true);

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
                console.debug('topic-order: Blazor の enhancedload には接続しなかった');
            }
        });
    }
})();
