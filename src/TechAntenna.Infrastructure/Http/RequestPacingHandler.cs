namespace TechAntenna.Infrastructure.Http;

/// <summary>
/// その HttpClient から出る要求の間隔を強制する。
///
/// 収集元の側で `Task.Delay` を挟む書き方だと、呼び出す場所が増えたときに守られなくなる
/// —— connpass は「キーワード検索」「グループの購読」「サブドメインの引き直し」「面掃き」の
/// 4 経路が同じ相手を叩いていて、それぞれが自分のぶんの待ちしか知らない。
/// ここに置けば、同じ名前付き HttpClient を使う限りどの経路からでも守られる。
///
/// <b>connpass の API 利用申請のページには「5 秒に 1 リクエストを超えないよう」とある。</b>
/// 相手のお願いなので、こちらの都合(何ページ読みたいか)で破らない。
///
/// 待つあいだ<b>ゲートを握ったまま</b>にするのが要点 —— 解放してから待つと、
/// 同時に入ってきた要求が揃って通り抜けて間隔が守られない。
/// </summary>
public sealed class RequestPacingHandler(TimeSpan minInterval, TimeProvider clock) : DelegatingHandler
{
    readonly SemaphoreSlim _gate = new(1, 1);

    DateTimeOffset _last = DateTimeOffset.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (minInterval > TimeSpan.Zero)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var wait = _last + minInterval - clock.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, clock, cancellationToken);
                }

                // 送る直前を起点にする。応答が返った時刻を起点にすると、
                // 遅い応答のぶんだけ間隔が伸びて全体が必要以上に遅くなる
                _last = clock.GetUtcNow();
            }
            finally
            {
                _gate.Release();
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }
}
