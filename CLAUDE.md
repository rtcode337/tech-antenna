# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 回答言語

ユーザーとの会話・説明・コミットメッセージ等は常に日本語で行うこと(コード自体・コード中の識別子・英語サイトからの引用・エラーメッセージの原文などはこの限りではない)。

## このリポジトリの目的

技術情報を自動収集し、**記事・イベント・書籍の3つを1つの導線にまとめて提示する**Webアプリ。
「今このトピックが伸びている → 関連する勉強会が近くである → 深掘りするならこの本」をつなぐ。

**C# / .NET の学習を兼ねた個人開発プロジェクト**であることに留意する。既存OSSで代替できる場面でも、
学習価値がある部分は自前で実装する方針を取ることがある。ライブラリ導入を提案する際は、
「何を学べなくなるか」もあわせて示すこと。

## 技術スタック

| 領域 | 採用 |
|---|---|
| ランタイム | .NET 10 (LTS, 2028-11 まで) |
| Web | ASP.NET Core + Blazor |
| 収集ジョブ | `BackgroundService` |
| DB | PostgreSQL + EF Core |
| LLM 要約 | Anthropic API |

.NET 9 (STS) と .NET 8 (LTS) はどちらも 2026-11 に EOL のため採用しない。

## 開発環境

.NET SDK はワークスペース共有の devcontainer(`/workspaces/vscode/.devcontainer/`)で
`ghcr.io/devcontainers/features/dotnet:2` により提供される。**このリポジトリ配下に devcontainer 定義は持たない**。
SDK のバージョンを変えるときは共有側の `devcontainer.json` を編集し、コンテナをリビルドする。

## 外部データソース

| 用途 | ソース | 備考 |
|---|---|---|
| イベント | connpass API / Doorkeeper API | どちらも公式・無料 |
| 書籍 | openBD / Google Books API | openBD は日本の書誌情報が無料 |
| 記事 | 各種 RSS / Atom | Qiita・Zenn・はてブ テクノロジー等 |

外部 API を叩くコードを書くときは、**User-Agent に個人のメールアドレスを入れないこと**。
連絡先が必要な場合はリポジトリ URL のみを記載する。

## コマンド

まだプロジェクトを作成していないため、ビルド・テストコマンドは存在しない。
`dotnet new` でソリューションを作成したら、このセクションを実際のコマンドで埋めること。
