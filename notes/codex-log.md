# Codexログ概要 (UnityVfxToMesh)

- ログ保存先: `C:\Users\sugino\.codex\sessions\2025\**\rollout-*.jsonl` （各行の`cwd`がこのプロジェクトの場合のみ採用）。
- 簡易抽出例: `Get-ChildItem C:\Users\sugino\.codex\sessions\2025 -Recurse -Filter rollout-*.jsonl | Select-String -Pattern "UnityVfxToMesh" -Context 0,2`。
- 本ファイルはセッション終了時に要約を追記して履歴化する運用。

## タイムラインまとめ（11月以降）
- **2025-11-07** 初期セットアップ: Unity用.gitignore作成、リポジトリ生成と初回プッシュ。VFX→SDF→メッシュ化の要件定義、atomicAdd未宣言エラーなどCompute Shader初期トラブル調査。
- **2025-11-08** 環境確認とVFX基盤整理: Unity 6000.2.8f1/URP&VFX 17.2 を確認、PropertyID整理。Kill済みパーティクルを`float4(0,0,0,-1)`でマーキングしSDF残留を防止、パイプライン/READMEを現状に合わせ更新。
- **2025-11-09** パイプライン再構成と色付きSDF: `VfxToSdf`/`SdfToMesh`/`SdfVolumeSource`に分割、Mesh出力を`targetRenderers`/`targetMeshes`で管理。ComputeをVfxToSdf・SdfToMeshに分離、ボクセル範囲拡張やカラー/半径フェード係数追加、ParticleColorバッファ対応HLSLを導入。`generatedMesh`にHideFlags付与、`VfxToMesh.unity`をLFS解除しPush、READMEにGIF追加（複数コミット）。
- **2025-11-10** パーティクル→SDF経路改善: `VfxWriteParticleBuffer`の死パーティクルクリア、`VfxToSdf`半径チェック、メタボール風ブレンド検討、SmoothUnion切替パラメータのInspector化。ショーケース動画のエンコード依頼も対応。度々コミット/プッシュ。
- **2025-11-12** `OrientedBoxUtils`カスタムHLSL分割（UVW出力と内外判定を別関数化）、CustomHLSLノードの安定化。関連シーン更新をコミット。
- **2025-11-14** DepthテクスチャからSDF生成パイプライン追加: `DepthToSdfVolume`コンポーネント/Compute、新サンプルシーン、Editor拡張。`SdfBooleanCombine`でLSEベースのスムーズ交差、`smoothBeta`説明と性能相談、関連エラー対応。
- **2025-11-30** ストリップSDF実装: `VfxStripToSdf.compute`と`VfxWriteStripBuffer.hlsl`を追加しStripPoint/Segmentを書き込み、InterlockedMaxやpointCount SRV問題を修正。SmoothUnion係数の安全域(0.01–0.1)設定、デバッグ用Dump/Readback整理、ストリップ滑らかさ向上コミット（e338b45）。
- **2025-12-01 午前** `VfxStripToSdf`のカラー補間・Head→Trailの滑らかな色変化、ストリップ境界判定のロジック確認。`SdfVolumeSource`の共通定義化とGizmo表示、SmoothUnion強度の微調整範囲明確化。変更をコミット/プッシュ。
- **2025-12-01 午後** （本セッション）Codexログ収集と記録運用の仕組みづくりの依頼。
- **2025-12-01 夕方** 本PCでのログ保存先を`C:\\Users\\git\\.codex\\sessions\\2025`に確認し、最新ファイル`rollout-2025-12-02T00-02-52-019ada70-1fb6-7a80-bef5-727602dbf7b1.jsonl`（52行）を特定。UTF-8出力設定で`codex-log.md`を再確認し、追記準備のみ（コード変更なし）。
- **2025-12-02** SdfToMeshにエディタ保存ボタン追加。GPUの生成メッシュを三角形で使われる頂点だけに間引いてアセット保存（CaptureStats付）。
## 今後の運用
- 新しいCodexセッション後に、上記フォルダから該当ログを確認し、この`codex-log.md`へ日付別に箇条書きで追記する。
- 依頼があれば要約だけでなく、関連ファイル・コミットIDも併記する。
