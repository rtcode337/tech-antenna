// 定期実行のチェック(/settings/jobs)を、**触った瞬間に保存する**。
//
// **上乗せの機能で、JS が動かなくても困らない** —— チェックボックスと
// 「定期実行のチェックを保存」は素の HTML フォームだけで成立している。ここが足すのは
// 「1 個チェックするたびにページを作り直さなくてよくする」ぶんだけ。
//
// JS が効いているあいだは「定期実行のチェックを保存」を**行ごと**引っ込め
// (押す必要が無いため)、**その場保存が失敗したときは出し直す** ——
// 保存する手段を画面から消さないこと。**ボタンだけを隠さない**のは、添えてある説明が
// 「このボタン」の話で、ボタンが無いと何を指しているのか読めないため。
// 時刻を保存するボタンは別(入力欄の隣)なので、こちらを隠しても時刻は保存できる。
//
// 保存先は `POST /api/jobs/schedule`(1 件だけ切り替える)。応答の `summary` は
// 「次は … に N 件のジョブが走ります」の文で、**画面と同じ組み立て**(サーバ側の
// ScheduleSettings.Describe)が返ってくる —— 文言を JS 側で組み直すと、
// 時刻が未設定のときなどに画面と食い違う。
//
// 全ページ静的 SSR なので対話回線は張らない。**enhanced navigation の後にも当て直す** ——
// blazor.web.js を読んでいるとリンクの遷移で DOM だけ差し替わり、スクリプトは再実行されない。
(function () {
    var endpoint = '/api/jobs/schedule';

    // **応答の追い越しに備える。** 同じジョブを素早く2回押すと、先の要求の応答が後から
    // 返ることがある。キーごとに通し番号を持ち、最後に送った番号の応答だけを画面に反映する
    var latest = {};
    var ticket = 0;

    // JS が効いている間は出さない行(ボタンと、それに添えた説明の両方)。
    // 失敗したら出し直して、従来どおりまとめて保存できるようにする
    function saveRow(form) {
        return form.querySelector('[data-save-jobs]');
    }

    function show(form, message, isError) {
        var box = form.querySelector('[data-schedule-status]');
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

    // 「次は … に N 件」の1行を差し替える。**この行が古いままだと、
    // チェックを入れたのに「走るジョブがありません」と出続ける**
    function updateSummary(summary) {
        var box = document.querySelector('[data-schedule-summary]');
        if (box && typeof summary === 'string') {
            box.textContent = summary;
        }
    }

    function token(form) {
        var field = form.querySelector('input[name="__RequestVerificationToken"]');

        return field ? field.value : '';
    }

    function label(box) {
        // 行の中の実行ボタンの文字がジョブの名前(表でも div でも同じ)
        var row = box.closest('tr') || box.closest('.job-row');
        var button = row ? row.querySelector('button[name="Action"]') : null;

        return button ? button.textContent.trim() : box.value;
    }

    function save(form, box) {
        var key = box.value;
        var enabled = box.checked;
        var mine = ++ticket;
        latest[key] = mine;

        var body = new FormData();
        body.append('key', key);
        body.append('enabled', enabled ? 'true' : 'false');
        body.append('__RequestVerificationToken', token(form));

        show(form, '保存中…', false);

        fetch(endpoint, { method: 'POST', body: body, credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) {
                    // 本文に理由が入っていれば拾う(消えたジョブ・トークン切れ)
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

                updateSummary(result.summary);
                show(form, '「' + label(box) + '」を定期実行に'
                    + (enabled ? '入れました' : '入れないようにしました')
                    + '（対象 ' + result.count + ' 件）。', false);
            })
            .catch(function (error) {
                if (latest[key] !== mine) {
                    return;
                }

                // **保存できなかったことを画面の状態にも出す** —— チェックだけ入ったままだと、
                // 定期実行に入っていないジョブを入れたつもりになる
                box.checked = !enabled;

                var row = saveRow(form);
                if (row) {
                    row.hidden = false;
                }

                show(form, error.message + '「定期実行のチェックを保存」で保存し直してください。', true);
            });
    }

    function apply() {
        document.querySelectorAll('form[data-job-schedule]').forEach(function (form) {
            var row = saveRow(form);
            if (row) {
                row.hidden = true;
            }

            form.querySelectorAll('input[type="checkbox"][name="Jobs"]').forEach(function (box) {
                // enhanced navigation と自動リロードのたびに呼ばれるので、二重に登録しない
                if (box.dataset.jobSchedule) {
                    return;
                }

                box.dataset.jobSchedule = '1';
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
        window.addEventListener('load', hookBlazor);
    }
})();
