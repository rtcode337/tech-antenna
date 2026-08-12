// PWA としてインストールできるようにするための最小のサービスワーカー。
//
// **ページもデータもキャッシュしない。** このアプリは集めた情報を毎回サーバーから読む
// 画面で、古い HTML を返すと「昨日の一覧を今日のものとして見せる」ことになる
// (しかも本人には見分けが付かない)。オフラインで動くことより、出ているものが
// 現在の状態であることを優先する。
//
// ここが引き受けるのは2つだけ:
//   1. インストールの条件を満たすこと(ブラウザは fetch を扱うワーカーを求める)
//   2. **通信できないときに素っ気ないエラー画面ではなく offline.html を返すこと**
//
// 静的ファイル(CSS・JS・アイコン)もキャッシュしない —— Blazor の静的アセットは
// ファイル名に指紋が付いていて HTTP のキャッシュがそのまま効くので、ここで二重に
// 持つと更新の経路が2つになる。
const VERSION = 'v1';
const CACHE = `tech-antenna-shell-${VERSION}`;
const OFFLINE_URL = '/offline.html';

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(CACHE)
            // reload を明示して、HTTP キャッシュの古い offline.html を焼き付けない
            .then(function (cache) { return cache.add(new Request(OFFLINE_URL, { cache: 'reload' })); })
            .then(function () { return self.skipWaiting(); }));
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (keys) {
                return Promise.all(keys
                    .filter(function (key) { return key !== CACHE; })
                    .map(function (key) { return caches.delete(key); }));
            })
            .then(function () { return self.clients.claim(); }));
});

self.addEventListener('fetch', function (event) {
    var request = event.request;

    // **横取りするのはページの移動(GET)だけ。** フォームの POST や画像・API は
    // そのまま通す —— ここで触ると、保存の失敗を「オフライン画面」で覆い隠してしまう
    if (request.method !== 'GET' || request.mode !== 'navigate') {
        return;
    }

    event.respondWith(
        fetch(request).catch(function () {
            return caches.match(OFFLINE_URL, { cacheName: CACHE });
        }));
});
