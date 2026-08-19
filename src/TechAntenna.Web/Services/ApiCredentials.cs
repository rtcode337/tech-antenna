using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>
/// 外部 API のキー・トークンを実行時に解決する。設定の入口は画面(外部連携)だけで、
/// 環境変数や .env では渡せない —— かつてはフォールバックとして読んでいたが、入口が
/// 2 つあると「どちらの値が効いているのか」を画面で説明し続けることになるためやめた。
///
/// - 値は Data Protection で暗号化して DB に保存する(purpose を変えると復号できなくなる)。
///   復号に失敗した行は「未設定」として扱う —— 鍵ディレクトリを永続化していない環境で
///   コンテナを作り直すと起きる。値そのものは戻せないので、画面から入れ直してもらう
/// - 読み取りは差し替え済みの辞書を見るだけなのでロック不要。書き込み(保存・削除)の
///   たびに全件を読み直して辞書を差し替え、<see cref="Version"/> を進める ——
///   LLM ゲートウェイはこの版数で組み直しを判定する
/// </summary>
public class ApiCredentials(
    ISecretStore store,
    IDataProtectionProvider dataProtection,
    ILogger<ApiCredentials> logger)
{
    /// <summary>暗号化の purpose。変えると保存済みの値が全部復号できなくなる。</summary>
    const string ProtectorPurpose = "TechAntenna.ApiCredentials";

    readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);
    readonly SemaphoreSlim _writeGate = new(1, 1);

    // 読み取りはこの参照を差し替えるだけ(Get をロックなしで呼べるようにする)
    volatile IReadOnlyDictionary<string, string> _secrets =
        new Dictionary<string, string>();

    /// <summary>画面からの保存・削除のたびに進む版数。LLM ゲートウェイのキャッシュ判定用。</summary>
    public int Version { get; private set; }

    /// <summary>キーを解決する。未設定なら null。</summary>
    public string? Get(string name) =>
        _secrets.TryGetValue(name, out var value) ? value : null;

    public bool Has(string name) => _secrets.ContainsKey(name);

    /// <summary>DB から全件を読み直して復号し、キャッシュを差し替える(起動時と保存後に呼ぶ)。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var secrets = await store.GetAllAsync(cancellationToken);
        var decrypted = new Dictionary<string, string>();
        foreach (var secret in secrets)
        {
            try
            {
                decrypted[secret.Name] = _protector.Unprotect(secret.Value);
            }
            catch (CryptographicException)
            {
                // 鍵ディレクトリを永続化せずコンテナを作り直すと起きる。値は戻せないので
                // 未設定として扱い、画面から入れ直してもらう(行は消さない —— 鍵を
                // 戻せば読めるようになる可能性を残す)
                logger.LogWarning(
                    "保存済みのキー {Name} を復号できません。Data Protection の鍵が変わって"
                    + "います(DataProtection__KeysDirectory を永続化し、画面から設定し直して"
                    + "ください)", secret.Name);
            }
        }

        _secrets = decrypted;
        Version++;
    }

    /// <summary>画面からの保存。空白のみの値は受け付けない(クリアは Remove で)。</summary>
    public async Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await store.SetAsync(name, _protector.Protect(value.Trim()), cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>画面で設定した値を消す。</summary>
    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await store.RemoveAsync(name, cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
