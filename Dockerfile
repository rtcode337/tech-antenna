# 本番用イメージ。main への push で GitHub Actions がビルドし GHCR へ公開する
# (.github/workflows/build-and-push-image.yml)。手元でビルドすることもできる。
#
# ビルドは常にビルドホストのアーキで行い、別アーキ向け(--platform linux/arm64 など)は
# .NET のクロスコンパイル(-a arm64)で出力する。QEMU エミュレーション下の
# dotnet publish は極端に遅いため。
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# buildx が渡す出力先のアーキ(amd64 / arm64)。.NET の -a は x64 / arm64 表記なので読み替える
ARG TARGETARCH

# csproj だけを先に置いて復元する。ソースを変えても NuGet の復元層は使い回せる
COPY src/TechAntenna.Core/TechAntenna.Core.csproj src/TechAntenna.Core/
COPY src/TechAntenna.Infrastructure/TechAntenna.Infrastructure.csproj src/TechAntenna.Infrastructure/
COPY src/TechAntenna.Web/TechAntenna.Web.csproj src/TechAntenna.Web/
RUN dotnet restore src/TechAntenna.Web/TechAntenna.Web.csproj \
      -a "$(echo "$TARGETARCH" | sed 's/amd64/x64/')"

COPY src/ src/
RUN dotnet publish src/TechAntenna.Web/TechAntenna.Web.csproj \
      -c Release --no-restore \
      -a "$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
      -o /app/publish

# Data Protection の鍵置き場と非 root ユーザーのホーム。実行ステージで RUN を使わずに済むよう
# (RUN があると arm64 向けビルドにエミュレーションが必要になる)、空ディレクトリだけここで
# 作って COPY する
RUN mkdir -p /app/keys /home/app

# Claude Code の CLI を取り出すステージ。npm の配布物には**プラットフォームごとの
# ネイティブな単一実行ファイル**が入っており、それ単体で動く(node は要らない)ので
# 1 ファイルだけ抜き出す —— 配布物ぜんぶ(600MB 超。glibc/musl 両方 + ラッパー)を
# 積むと最終イメージが倍近くなる。
#
# **ビルドホストのアーキで走らせる**(`--platform=$BUILDPLATFORM`)。対象アーキの版は
# パッケージ名で選べる(`claude-code-linux-x64` / `-arm64`)ので、arm64 向けを作るときも
# エミュレーションは要らない —— やっているのは取得と展開だけ。
#
# バージョンは固定する —— latest だと同じイメージタグでも中身が変わり、
# 「昨日まで動いていた要約が落ちる」を再現できなくなる。上げるときはここを変える。
FROM --platform=$BUILDPLATFORM node:24-slim AS claude-cli
ARG TARGETARCH
ARG CLAUDE_CODE_VERSION=2.1.234
RUN arch="$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
    && cd /tmp \
    && npm pack "@anthropic-ai/claude-code-linux-${arch}@${CLAUDE_CODE_VERSION}" \
    && tar -xzf "anthropic-ai-claude-code-linux-${arch}-${CLAUDE_CODE_VERSION}.tgz" \
    && mkdir -p /out && cp package/claude /out/claude && chmod +x /out/claude

# 実行用。ソースもビルドツールも含めず publish の成果物だけを載せる。
# Alpine ではなく Debian ベースを使うのは ICU(日本語の日付・文字列の書式)を含むため
# (Alpine 版は globalization invariant モードが既定で、ja-JP の書式が効かない)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production

# **Claude Code の CLI を同梱する。** 要約・翻訳・タグの仕分け・今日のサマリーを
# サブスクリプションの枠で回す経路で、アプリがプロセスとして起動する
# (`ClaudeCodeCliBridge`。トークンは画面から設定し、子プロセスの環境変数で渡す)。
#
# かつては別コンテナの CLI ブリッジ(chiezo-bridge)へ HTTP で頼み、CLI をこのイメージから
# 外していた —— 実体が大きくイメージが倍近くなるためだったが、公開リポジトリに
# なって容量を気にする理由が薄れた。**同梱のほうが、試す人が別コンテナを立てずに済む。**
#
# 入れるのは**ネイティブの単一実行ファイル1つだけ**(node も npm も要らない)。
# 実行ステージに RUN を置かずに済むので、arm64 向けビルドのエミュレーションも増えない
COPY --from=claude-cli /out/claude /usr/local/bin/claude

# antiforgery と Blazor が使う Data Protection の鍵の保存先(Program.cs が読む)。
# コンテナを作り直しても鍵が消えないよう docker-compose.yml でボリュームをマウントする
ENV DataProtection__KeysDirectory=/app/keys
COPY --from=build /app/publish ./
COPY --from=build --chown=$APP_UID:$APP_UID /app/keys ./keys
# .NET が書き込み先にホームを使うことがある(証明書ストアなど)。非 root で動かすので用意しておく
COPY --from=build --chown=$APP_UID:$APP_UID /home/app /home/app
ENV HOME=/home/app
# ベースイメージが用意している非 root ユーザー(UID 1654)で動かす
USER $APP_UID
# ベースイメージの既定(8080)ではなく 7020 を待ち受ける。ホスト側の公開ポートと
# 番号をそろえて、compose の "7020:7020" を読むだけで対応が分かるようにするため。
# TLS は前段のリバースプロキシで終端する前提で、コンテナ自身は HTTP だけを待ち受ける
ENV ASPNETCORE_HTTP_PORTS=7020
EXPOSE 7020
ENTRYPOINT ["dotnet", "TechAntenna.Web.dll"]
