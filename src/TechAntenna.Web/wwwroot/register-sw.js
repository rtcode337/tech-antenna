// サービスワーカーの登録。インストールできる PWA にするために要る
// (ブラウザは fetch を扱うワーカーがあるかを見る)。中身は service-worker.js。
//
// 安全なコンテキスト(https か localhost)でしか登録できない。LAN の IP へ
// http で当てている場合は登録されず、その環境ではインストールもできない —— これは
// ブラウザ側の決まりなので、登録できないこと自体は異常ではない(静かに何もしない)。
(function () {
    if (!('serviceWorker' in navigator)) {
        return;
    }

    // 起動直後の描画とスクリプトの取得を奪い合わないよう、load の後に登録する
    window.addEventListener('load', function () {
        // 指紋なしのパスで登録する —— 制御できる範囲(scope)は URL のパスで決まり、
        // 名前が版ごとに変わると登録が別物として増えるため
        navigator.serviceWorker.register('/service-worker.js').catch(function (error) {
            console.debug('service worker の登録は見送られた', error);
        });
    });
})();
