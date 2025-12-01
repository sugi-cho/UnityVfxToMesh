# Codexログ概要 (UnityVfxToMesh)

- ログ保存先: `C:\Users\sugino\.codex\sessions\2025\**\rollout-*.jsonl` （各行の`cwd`がこのプロジェクトの場合のみ採用）。
- 簡易抽出例: `Get-ChildItem C:\Users\sugino\.codex\sessions\2025 -Recurse -Filter rollout-*.jsonl | Select-String -Pattern "UnityVfxToMesh" -Context 0,2`。
- 本ファイルはセッション終了時に要約を追記して履歴化する運用。

## タイムラインまとめ
- **2025-11-07** 初期セットアップ: Unity用.gitignore作成、リポジトリ生成と初回プッシュ。VFX→SDF→メッシュ化の要件定義、atomicAdd未宣言エラーなどCompute Shader初期トラブル調査。
- **2025-11-10** パーティクル→SDF経路改善: `VfxWriteParticleBuffer`の死パーティクルクリア、`VfxToSdf`半径チェック、メタボール風ブレンド検討、SmoothUnion切替パラメータのInspector化。ショーケース動画のエンコード依頼も対応。度々コミット/プッシュ。
- **2025-11-12** `OrientedBoxUtils`カスタムHLSL分割（UVW出力と内外判定を別関数化）、CustomHLSLノードの安定化。関連シーン更新をコミット。
- **2025-11-14** DepthテクスチャからSDF生成パイプライン追加: `DepthToSdfVolume`コンポーネント/Compute、新サンプルシーン、Editor拡張。`SdfBooleanCombine`でLSEベースのスムーズ交差、`smoothBeta`説明と性能相談、関連エラー対応。
- **2025-12-01 午前** `VfxStripToSdf`のカラー補間・Head→Trailの滑らかな色変化、ストリップ境界判定のロジック確認。`SdfVolumeSource`の共通定義化とGizmo表示、SmoothUnion強度の微調整範囲明確化。変更をコミット/プッシュ。
- **2025-12-01 午後** （本セッション）Codexログ収集と記録運用の仕組みづくりの依頼。

## 今後の運用
- 新しいCodexセッション後に、上記フォルダから該当ログを確認し、この`codex-log.md`へ日付別に箇条書きで追記する。
- 依頼があれば要約だけでなく、関連ファイル・コミットIDも併記する。
