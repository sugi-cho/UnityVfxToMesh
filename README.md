## VFX から SDF から Mesh パイプライン

このプロジェクトは Unity 6 (6000.2.8f1) + URP + Visual Effect Graph 17.2.0 を前提に、VFX Graph で更新した粒子情報を GPU `GraphicsBuffer` に保持し、Compute Shader で SDF＋カラーボリュームへスタンプしたあと Naive Surface Nets でメッシュ化するまでを自動化する最小構成です。`SdfToMesh` 側で生成した単一の `Mesh` を複数の `MeshFilter` に共有し、描画マテリアルは各 `MeshRenderer` 側で自由に差し替えられます。カラー情報は VFX から `ParticleColors` バッファで伝搬し、メッシュの頂点カラーとして反映されます。

![vfxToMesh](https://github.com/user-attachments/assets/2ed1b544-5926-4840-8080-cd7b7fcd42c5)

### 主なアセット

- `Shaders/VfxToSdf.compute`  
  `ClearSdf` / `StampParticles` / `ClearParticleBuffers` / `NormalizeColorVolume` の 4 カーネルを持ち、SDF とカラーボリュームを同時にリセット・スタンプします。`smoothUnionStrength` によるスムースユニオン、`ColorRadiusMultiplier`／`ColorFadeMultiplier` による色の広がり、カラーの集計を正規化する `NormalizeColorVolume`（`ColorBlendMode.Normalized` 時のみ）を組み込んでおり、`_ParticleColors` から蓄積した重みを色空間へ還元します。
- `Shaders/SdfToMesh.compute`  
  `Naive Surface Nets` を使って `SdfVolume` の SDF／カラー情報から頂点位置・法線・インデックス・頂点カラーを生成します。`ColorVolume` をサンプリングしてカラーを `float4` へ変換し、`SdfToMesh` の `MeshFilter` に割り当てた単一の `Mesh` にバッファを直接書き込みます。  
- `Shaders/HLSL/VfxWriteParticleBuffer.hlsl`  
  VFX Graph の `Custom HLSL` ブロックから呼び出す `WriteParticleBufferBlock`（位置・半径）と `WriteParticleColorBlock`（カラー + α ＝重み）を収録しています。`weight = attributes.alpha` でカラーの寄与度を決め、`alpha == 0` なら再利用されるようにデータをクリアします。
- `Assets/VfxToMesh/VFX/ParticleField.vfx`  
  粒子の初期化～更新～描画をまとめた Visual Effect Graph。`Write Particle Buffer` / `Write Particle Color` の `Custom HLSL` ブロックを Update に配置し、`GraphicsBuffer ParticlePositions` / `ParticleColors` に接続すれば、VFX 側の色・サイズ・生存状態を `VfxToSdf` に渡せます。
- `Assets/VfxToMesh/Scripts/VfxToSdf.cs`  
  `gridResolution` ・`particleCount`・`boundsSize`・`isoValue`・`sdfFar` に加えて、`sdfRadiusMultiplier`／`sdfFadeMultiplier`／`colorRadiusMultiplier`／`colorFadeMultiplier`／`smoothUnionStrength`／`colorBlendMode`（`Normalized` / `Accumulated`）を公開し、SDF 演算とカラー集計を細かく調整できます。`GraphicsBuffer`・3D `RenderTexture`（RFloat／ARGBFloat）を自動で確保し、描画ループでは SDF をクリア→パーティクルスタンプ→カラー正規化→バッファクリアの順で Dispatch します。
- `Assets/VfxToMesh/Scripts/SdfToMesh.cs`  
  `SdfVolumeSource`（標準では `VfxToSdf`）から受け取った SDF／カラーをもとに、セルバッファ・カウンタバッファ・頂点/法線/カラー/インデックスバッファを確保し、Compute Shader によってメッシュを再構築します。`targetMeshes`（`MeshFilter` リスト）に一括で `generatedMesh` を共有し、`MeshRenderer` 側で好きなマテリアルを渡せます。`ColorVolume` から取得した色は、α > 0 のセルのみ頂点カラーとして適用されます。
- `Assets/VfxToMesh/Scripts/SdfVolumeSource.cs`  
  `SdfVolume` 構造体には `Texture`（SDF）に加えて `ColorTexture` が追加され、`SdfShaderParams.Push` で共通パラメータ（グリッド解像度 / バウンディング / iso / SDF far / トランスフォーム）を Compute Shader へ渡します。
- `Assets/VfxToMesh/Editor/PipelineBootstrap.cs`  
  `Tools > Vfx To Mesh > Rebuild Playground` から `Scenes/VfxToMesh.unity` を再生成するユーティリティ。URP カメラ／ライト／`VfxToSdf`＋`SdfToMesh` リグを配置し、ベースの Compute Shader をアサインしてデフォルト設定（`gridResolution = 128` / `particleCount = 512` / `boundsSize = (6,6,6)` など）を適用します。
- `Assets/VfxToMesh/Settings/URP3D.asset` / `URP3D_Renderer.asset`  
  URP のアセットを含む Minimal のレンダーパイプライン設定。パッケージやシーンが URP 依存で動作するように用意されています。
- `Assets/VfxToMesh/Scenes/VfxToMesh.unity`  
  管理されたプレイグラウンドシーン。リビルド時は上書きされ、VFX・SDF・メッシュが同期された状態からスタートできます。

## 使い方

1. **Playground シーンの再生成**  
   `Tools > Vfx To Mesh > Rebuild Playground` を実行すると `Assets/VfxToMesh/Scenes/VfxToMesh.unity` が上書きされ、必要な GameObject（カメラ / ライト / VFX → `VfxToSdf` → `SdfToMesh` → `MeshRenderer`）と設定が自動で組み上がります。CI などでは `VfxToMesh.Editor.PipelineBootstrap.BuildSceneHeadless` を呼び出せます。
2. **VFX Graph の Blackboard / Update 設定**  
   - Blackboard に `GraphicsBuffer ParticlePositions`（型: GraphicsBuffer）、`GraphicsBuffer ParticleColors`、`int ParticleCount` を追加します。`ParticleCount` はバッファの書き込み上限として VFX・Compute Shader 両側で使います。  
   - Update コンテキストでは `Custom HLSL` ブロックを 2 つ追加します。`ParticlePositions` には `WriteParticleBufferBlock` を、`ParticleColors` には以下の `WriteParticleColorBlock` を接続します。`weight = attributes.alpha` を使ってカラーの寄与をコントロールし、カラーなしのパーティクルは α=0 でクリアされます。

    ```hlsl
    void WriteParticleColorBlock(inout VFXAttributes attributes,
                                 RWStructuredBuffer<float4> particleColorBuffer,
                                 uint particleCapacity)
    {
        uint index = WrapParticleIndex((uint)attributes.particleId, particleCapacity);

        if (attributes.alive == 0)
        {
            particleColorBuffer[index] = float4(0, 0, 0, 0);
            return;
        }

        float weight = attributes.alpha;
        particleColorBuffer[index] = float4(attributes.color.rgb, weight);
    }
    ```

3. **`VfxToSdf` / `SdfToMesh` の主なプロパティ**  
   - `VfxToSdf`：`sdfCompute`（`VfxToSdf.compute`）、`targetVfx`、`gridResolution`（64～160）・`particleCount`（512～20000）・`boundsSize`、`isoValue`、`sdfFar`、`sdfRadiusMultiplier` / `sdfFadeMultiplier`（SDF 拡張）、`colorRadiusMultiplier` / `colorFadeMultiplier`（カラーの影響範囲）、`smoothUnionStrength`（スムース結合の強さ）、`colorBlendMode`（`Normalized` で `NormalizeColorVolume` を走らせ、`Accumulated` で加算値を保持）を調整できます。`allowUpdateInEditMode` をチェックすればエディター上でも毎フレーム更新されます。  
   - `SdfToMesh`：`meshCompute`（`SdfToMesh.compute`）、`sdfSource`（通常は `VfxToSdf`）、`targetMeshes`（共有したい `MeshFilter` 一覧）、`allowUpdateInEditMode`。`Mesh` を動的に再構築し、`SdfVolume.ColorTexture` から頂点色を取得します。  

4. **処理フロー**  
   1. VFX Graph で `ParticlePositions`（xyz: 位置, w: 半径）と `ParticleColors`（rgb + α ＝重み）をバッファへ書き出します。Kill されたら radius/weight を 0 にしてバッファを再利用します。  
   2. `VfxToSdf` が `ClearSdf` / `ClearParticleBuffers` でボリュームとバッファを初期化し、`StampParticles` で粒子を SDF＋カラー空間へ投影。`smoothUnionStrength` によってパーティクル同士の SDF をなめらかに合成し、`colorRadius`・`fadeRadius` で色の拡がりを制御します。`colorBlendMode` が `Normalized` の場合は `NormalizeColorVolume` を呼び出してカラーを平均化します。  
   3. `SdfToMesh` が `ClearCells` → `BuildSurfaceVertices` → `BuildSurfaceIndices` を Dispatch し、Naive Surface Nets で頂点・法線・頂点カラー・インデックスを構築。`colorTexture` を `SdfVolume` に含めることで色情報を直接 `Mesh` に渡します。  
   4. 各 `MeshFilter`（`targetMeshes`）に同じ `Mesh` を共有し、`MeshRenderer` 側でマテリアルを差し替えて描画します。

## トラブルシューティング

- メッシュが表示されないときは `targetMeshes` に登録した `MeshFilter` が有効か、`MeshRenderer` が有効化されているか、`SdfToMesh.meshCompute` / `VfxToSdf.sdfCompute` がアサインされているかを確認してください。  
- `counterBuffer` の要素（頂点数/インデックス数）が 0 のままなら `gridResolution` や `particleCount` が適切か、`VfxToSdf` 側でバッファが更新されているか（`ParticleCount` を正しく渡すこと）を見直してください。  
- VFX のカラーが反映されない場合は `WriteParticleColorBlock` を必ず接続し、`attributes.alpha` で重みを設定しているか、`colorBlendMode` が `Normalized` なら `NormalizeColorVolume` が走るため `ParticleColors` の α が 0 でないことを確認します。`Accumulated` モードでは α を加算してから後処理で分割するため、バッファに値が残っていることが描画に必要です。  
- `gridResolution` を大きくすると `Dispatch` 回数や頂点数が増えるので、エディター上で `SdfToMesh.targetMeshes` の `Mesh` が正しく再割り当てされているか、`generatedMesh` の容量（頂点/インデックス）が足りているかもチェックしてください。
