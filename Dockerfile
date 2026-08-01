# 本番用イメージ。リポジトリが非公開の間は GitHub Actions を置かず、デプロイ先か手元で
# ビルドする(README「本番運用」参照)。
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

# Data Protection の鍵置き場と、claude CLI が設定を書くためのホーム。実行ステージで RUN を
# 使わずに済むよう(RUN があると arm64 向けビルドにエミュレーションが必要になる)、
# 空ディレクトリだけここで作って COPY する
RUN mkdir -p /app/keys /home/app

# Claude Code CLI。要約を API の従量課金ではなくサブスクリプションの枠で回すときに使う
# (CLAUDE_CODE_OAUTH_TOKEN を渡すとアプリがこちらを選ぶ)。
#
# 配布物は Node に依存しない単体のネイティブバイナリなので、実行イメージに Node は入れない。
# npm パッケージはアーキごとに分かれており、名前で明示すればビルドホストから対象アーキ用を
# ダウンロードできるため、ここも $BUILDPLATFORM で動かしてエミュレーションを避ける。
FROM --platform=$BUILDPLATFORM node:22-slim AS claude
ARG TARGETARCH
ARG CLAUDE_CODE_VERSION=2.1.220
WORKDIR /tmp/claude
RUN arch="$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
    && npm pack --silent "@anthropic-ai/claude-code-linux-${arch}@${CLAUDE_CODE_VERSION}" \
    && tar xzf *.tgz \
    && install -m 0755 package/claude /usr/local/bin/claude

# 実行用。ソースもビルドツールも含めず publish の成果物だけを載せる。
# Alpine ではなく Debian ベースを使うのは ICU(日本語の日付・文字列の書式)を含むため
# (Alpine 版は globalization invariant モードが既定で、ja-JP の書式が効かない)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
# antiforgery と Blazor が使う Data Protection の鍵の保存先(Program.cs が読む)。
# コンテナを作り直しても鍵が消えないよう docker-compose.yml でボリュームをマウントする
ENV DataProtection__KeysDirectory=/app/keys
COPY --from=build /app/publish ./
COPY --from=build --chown=$APP_UID:$APP_UID /app/keys ./keys
COPY --from=claude /usr/local/bin/claude /usr/local/bin/claude
# claude CLI は起動時にホーム配下へ設定を書く。非 root で動かすため書ける場所を用意しておく
COPY --from=build --chown=$APP_UID:$APP_UID /home/app /home/app
ENV HOME=/home/app
# ベースイメージが用意している非 root ユーザー(UID 1654)で動かす
USER $APP_UID
# ベースイメージの既定(ASPNETCORE_HTTP_PORTS=8080)に合わせる。TLS は前段のリバース
# プロキシで終端する前提で、コンテナ自身は HTTP だけを待ち受ける
EXPOSE 8080
ENTRYPOINT ["dotnet", "TechAntenna.Web.dll"]
