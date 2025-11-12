# VfxToMesh SDF → VFX バインディング

このプロジェクトには `Assets/VfxToMesh/Scripts/SdfVolumeBinder.cs` が含まれており、`SdfVolumeSource` が提供する SDF テクスチャ／カラーボリューム／バウンディング情報を `VisualEffect` の exposed property に自動で書き込むことができます。Binder は `VisualEffect` と同じ GameObject（または `SdfVolumeSource` を含む親）にアタッチし、`sdfSource` に対象を紐づけてください。

## 必須プロパティ（VFX Graph で事前に Blackboard に用意）

| プロパティ名 | 型 | 説明 |
| --- | --- | --- |
| `SdfVolumeTexture` | Texture3D | SDF（RFloat）を保持するテクスチャ |
| `SdfColorTexture` | Texture3D | 累積されたカラー（ARGBFloat） |
| `SdfOrientedBoxCenter` | Vector3 | Oriented Box の中心（ワールド空間） |
| `SdfOrientedBoxSize` | Vector3 | Oriented Box のサイズ |
| `SdfOrientedBoxRotation` | Vector4 | Oriented Box の回転（Quaternion：x,y,z,w） |

※これらはバインド時に必ず存在する必要があり、不足していると `SdfVolumeBinder` がエラーログを出し、バインドをスキップします。

## 任意プロパティ（必要な場合のみ Graph に追加）

- `SdfBoundsCenter`（Vector3）: バウンディングの中心位置
- `SdfLocalToWorld`（Matrix4x4）: ローカルからワールドへの逆変換
- `SdfVoxelSize`（Float）: `BoundsSize / GridResolution`
- `SdfIsoValue`（Float）: iso-value
- `SdfFar`（Float）: SDF の far-plane 相当
- `SdfGridResolution`（Int）: SDF テクスチャの解像度
- `SdfCellResolution`（Int）: セル（グリッド解像度-1）数

存在するプロパティに対してのみ `Set` を行うため、Graph に含めなくてもエラーになりません。

## VFX Graph 側での利用例

1. `Sample Texture3D` ノードに `SdfVolumeTexture` / `SdfColorTexture` を接続し、`Field Transform` ブロックの `OrientedBox` 入力に `SdfOrientedBoxCenter`・`SdfOrientedBoxSize`・`SdfOrientedBoxRotation` を与えることで、Box Transform 内部で UV が正規化され、ワールド空間の位置から SDF を直接サンプリングできます。
2. `IsoValue` や `SdfFar` を使ってフェード・マスク・パーティクルの生存判定を作ることもできます（プロパティが存在すれば Binder が自動で `SetFloat` します）。
3. `SdfBoundsCenter` / `LocalToWorld` を使えば、VFX 内でボリューム境界の中心からの距離やワールド空間の回転を手軽に利用できます。

## Binder の振る舞い

- `SdfVolumeSource.Version` を監視し、変更があるたびに `SdfVolume` を再取得してバインドします。
- SDF またはカラーのテクスチャが取得できない場合、対応する `Texture3D` を `null` でクリアします。
- 任意プロパティは存在チェック付きでのみ設定するため、Graph の exposed property を削除しても問題ありません。
- Binder を削除／無効化するときは `ResetBinding` によりテクスチャが `null` に戻されます。

## セットアップ手順

1. `VisualEffect` に `SdfVolumeBinder` をアタッチし、`sdfSource` に `VfxToSdf`（または任意の `SdfVolumeSource`）を設定。
2. VFX Graph の Blackboard に上記必須プロパティを追加し、`Field Transform` ブロックの `OrientedBox` 入力に接続してボリューム空間を再現する構成を作ります。
3. `IsoValue` などの任意プロパティを必要なノードに繋ぎ、SDF を使ったフェードやマスク処理を作ります。

このドキュメントに沿って binder を仕込むことで、SDF のテクスチャ・カラー・バウンディング・座標変換を VFX 側でもシームレスに利用できます。
