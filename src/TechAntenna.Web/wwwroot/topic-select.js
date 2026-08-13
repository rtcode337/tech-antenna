// 収集対象のチェック(/settings/topics)を、**押した瞬間に保存する**。
//
// **上乗せの機能で、JS が動かなくても困らない** —— チェックボックスと「選択を保存」は
// 素の HTML フォームだけで成立している。ここが足すのは「1 個チェックするたびに
// 1000 行のページを再読み込みしなくてよくする」ぶんだけ。だから JS が動いたときだけ
// フォームのボタンを引っ込め(押す必要が無くなるため)、失敗したときは出し直す
// —— 保存する手段を画面から消さないこと。
//
// 保存先は `POST /api/topics/select`(1 件だけ切り替える)。フォームの POST が
// 「一覧に出ている分で丸ごと置き換える」のとは別の経路で、閉じた枝や検索で
// 絞られている行の選択を巻き添えにしない。
//
// 全ページ静的 SSR なので対話回線は張らない。**enhanced navigation の後にも当て直す** ——
// blazor.web.js を読んでいるとリンクの遷移で DOM だけ差し替わり、スクリプトは再実行されない。
(function () {
    var endpoint = '/api/topics/select';

    // **応答の追い越しに備える。** 同じ語を素早く2回押すと、先の要求の応答が後から
    // 返ることがある。キーごとに通し番号を持ち、最後に送った番号の応答だけを画面に反映する
    var latest = {};
    var ticket = 0;

    function status(form) {
        return form.querySelector('[data-select-status]');
    }

    function show(form, message, isError) {
        var box = status(form);
        if (!box) {
            return;
        }

        box.textContent = message;
        box.hidden = false;
        box.classList.toggle('error', !!isError);

        // 成功の知らせは残さない(押すたびに出るので、居座ると邪魔になる)。
        // 失敗は残す —— 気づかないまま「保存された」と思われるほうが困る
        if (box.dataset.timer) {
            clearTimeout(Number(box.dataset.timer));
            delete box.dataset.timer;
        }

        if (!isError) {
            box.dataset.timer = String(setTimeout(function () {
                box.hidden = true;
            }, 2500));
        }
    }

    // JS が効いている間はフォームのボタンを出さない(チェックした時点で保存済みのため)。
    // 失敗したら出し直して、従来どおりまとめて保存できるようにする
    function saveButton(form) {
        return form.querySelector('[data-save-selection]');
    }

    function token(form) {
        var field = form.querySelector('input[name="__RequestVerificationToken"]');

        return field ? field.value : '';
    }

    function label(box) {
        var row = box.closest('.topic-grid') || box.closest('tr');
        var name = row ? row.querySelector('.topic-name a') : null;

        return name ? name.textContent.trim() : box.value;
    }

    function save(form, box) {
        var key = box.value;
        var selected = box.checked;
        var mine = ++ticket;
        latest[key] = mine;

        var body = new FormData();
        body.append('key', key);
        body.append('selected', selected ? 'true' : 'false');
        body.append('__RequestVerificationToken', token(form));

        show(form, '保存中…', false);

        fetch(endpoint, { method: 'POST', body: body, credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) {
                    // 本文に理由が入っていれば拾う(語彙から消えた語・トークン切れ)
                    return response.json()
                        .catch(function () { return {}; })
                        .then(function (payload) {
                            throw new Error(payload.error || ('保存できませんでした（HTTP ' + response.status + '）。'));
                        });
                }

                return response.json();
            })
            .then(function (result) {
                if (latest[key] !== mine) {
                    return;
                }

                show(form, (selected ? '「' + label(box) + '」を収集対象にしました' : '「' + label(box) + '」を外しました')
                    + '（収集対象 ' + result.count + ' 件）。', false);
            })
            .catch(function (error) {
                if (latest[key] !== mine) {
                    return;
                }

                // **保存できなかったことを画面の状態にも出す** —— チェックだけ入ったままだと、
                // 収集対象になっていない語を選んだつもりになる
                box.checked = !selected;

                var button = saveButton(form);
                if (button) {
                    button.hidden = false;
                }

                show(form, error.message + '「選択を保存」で保存し直してください。', true);
            });
    }

    function apply() {
        document.querySelectorAll('form[data-topic-select]').forEach(function (form) {
            var button = saveButton(form);
            if (button) {
                button.hidden = true;
            }

            form.querySelectorAll('input[type="checkbox"][name="SelectedTopics"]')
                .forEach(function (box) {
                    // enhanced navigation のたびに呼ばれるので、二重に登録しない
                    if (box.dataset.topicSelect) {
                        return;
                    }

                    box.dataset.topicSelect = '1';
                    box.addEventListener('change', function () {
                        save(form, box);
                    });
                });
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
                // Blazor が使えない構成でも、通常の遷移なら apply() だけで足りる
                console.debug('topic-select: Blazor の enhancedload には接続しなかった');
            }
        });
    }
})();
