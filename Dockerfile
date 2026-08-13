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

# Data Protection の鍵置き場と、CLI ブリッジと共有する設定の置き場、非 root ユーザーの
# ホーム。実行ステージで RUN を使わずに済むよう(RUN があると arm64 向けビルドに
# エミュレーションが必要になる)、空ディレクトリだけここで作って COPY する
#
# **Claude Code の CLI はこのイメージに入れない。** 要約をサブスクリプションの枠で回すときは
# 別コンテナの CLI ブリッジ(chiezo-bridge)へ HTTP で頼み、トークンは /app/state 経由で渡す
# (docker-compose.yml の bridge サービス)。CLI の実体は 100MB 超あり、版を上げるたびに
# このイメージを焼き直すことになるため、更新はブリッジのコンテナ側に任せる
RUN mkdir -p /app/keys /app/state /home/app

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
# CLI ブリッジと共有するディレクトリ(設定 DB を書き、ブリッジが読み取り専用で読む)。
# compose ではホストのディレクトリをマウントするので、ここで作るのは単体で動かしたときの受け皿
COPY --from=build --chown=$APP_UID:$APP_UID /app/state ./state
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
