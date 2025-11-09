## VFX から SDF から Mesh パイプライン

このプロジェクトは Unity 6 (6000.2.8f1) + URP + Visual Effect Graph 17.2.0 を前提に、VFX Graph 上で更新したパーティクルを GPU `GraphicsBuffer` に書き出し、Compute Shader で SDF → Naive Surface Nets メッシュ化し、生成した `Mesh` を複数の `MeshRenderer` に共有するまでを自動化する最小構成です。描画マテリアルは各 `MeshRenderer` 側で自由に差し替えられます。

### 主なアセット

- `Shaders/VfxToMesh.compute`  
  VFX Graph から流入した `StructuredBuffer<float4>`（xyz: 位置, w: 半径）を SDF 化し、Naive Surface Nets でメッシュを構築します。
- **Material (optional)**  
  パイプラインは Mesh の生成と `MeshFilter` への割り当てのみを行うため、どのシェーダー/マテリアルを使うかは `MeshRenderer` 側で決めます。
- `Assets/VfxToMesh/VFX/ParticleField.vfx`  
  パーティクルの初期化・アップデート・描画設定をまとめた VFX Graph。Update コンテキストに `Custom HLSL` ブロック（`Write Particle Buffer`）を挿入し、`GraphicsBuffer` `ParticlePositions` へ Position/Size を書き戻します。
- `Scripts/SdfVolumeSource.cs`    SDF ボリュームのデータ構造、`SdfVolumeSource` 基底クラス、Compute Shader へ一括でパラメータを送るヘルパーをまとめています。今後追加する SDF 生成 / 加工コンポーネントはここを継承します。
- `Scripts/VfxToSdf.cs`    Visual Effect Graph の粒子バッファを受け取り、SDF 3D テクスチャを生成して `SdfVolume` として公開するコンポーネントです。
- `Scripts/SdfToMesh.cs`    任意の `SdfVolumeSource` (標準では `VfxToSdf`) を入力に取り、Naive Surface Nets で Mesh を構築して複数の `MeshRenderer` に適用します。
- `Editor/PipelineBootstrap.cs`  
  `Tools/Vfx To Mesh/Rebuild Playground` からプレイグラウンドシーンを生成し、VFX/パイプライン/レンダラーの関連付けを自動でセットアップします。

## 使い方

1. **Playground シーンの再生成**  
   `Tools > Vfx To Mesh > Rebuild Playground` を実行すると `Assets/VfxToMesh/Scenes/VfxToMesh.unity` が上書きされ、必要なコンポーネントが配置されます。
2. **VFX Graph の Blackboard / Update 設定**  
   - Blackboard に `GraphicsBuffer ParticlePositions`（型: GraphicsBuffer）と `int ParticleCount` を追加します。`ParticleCount` は Spawner で参照できます。  
   - Initialize コンテキストではパーティクルの初期位置・速度・サイズなどを任意に決めてください。Compute Shader には size を半径として渡します。  
   - Update コンテキストに `Custom HLSL` ブロック **`Write Particle Buffer`** を追加し、`GraphicsBuffer` 入力に `ParticlePositions`、`int` 入力に `ParticleCount` を接続します。HLSL 本体は `Assets/VfxToMesh/Shaders/VfxWriteParticleBuffer.hlsl`（下記抜粋）です。Kill されたパーティクルは `radius = -1` でマークしてバッファを自然に再利用します。

    ```hlsl
    void WriteParticleBufferBlock(inout VFXAttributes attributes,
                                  RWStructuredBuffer<float4> particleBuffer,
                                  uint particleCapacity)
    {
        uint particleIndex = particleCapacity == 0 ? 0 : (uint)attributes.particleId % max(1u, particleCapacity);

        if (attributes.alive == 0)
        {
            particleBuffer[particleIndex] = float4(0, 0, 0, -1);
            return;
        }

        float radius = max(attributes.size, 0.0001f) * 0.5f;
        particleBuffer[particleIndex] = float4(attributes.position, radius);
    }
    ```

3. **`VfxToSdf` / `SdfToMesh` の主なプロパティ**     - `VfxToSdf`: `sdfCompute` (SDF 用 Compute Shader)、`targetVfx`、`gridResolution`、`particleCount`、`boundsSize`、`isoValue`、`sdfFar`、`allowUpdateInEditMode` などを持ち、`SdfVolume` を生成して共有します。     - `SdfToMesh`: `meshCompute`、`sdfSource` (任意の `SdfVolumeSource`) 、`targetRenderers`、`allowUpdateInEditMode` を設定すると、入力 SDF から Mesh を生成して複数のレンダラーに出力します。  4. **処理フロー**  
   1. VFX Graph が `ParticlePositions` バッファへ `float4(position, radius)` を書き戻す。  
   2. `VfxToSdf` が SDF をクリア→粒子をスタンプし、必要なら中間 `SdfVolumeSource` で加工してから `SdfToMesh` に渡します。`SdfToMesh` はその SDF から Naive Surface Nets で頂点/法線/インデックスを生成します。
  3. カウンタバッファからインデックス数を読み出し、生成した `Mesh` の `SubMeshDescriptor` を更新。  
   4. `targetRenderers` の各 `MeshFilter` に同じ `Mesh` を共有させ、任意のマテリアルで描画する。

## トラブルシューティング

- メッシュが描画されない場合は `targetRenderers` に `MeshRenderer` / `MeshFilter` のペアが設定されているか、`targetVfx` がセットされているかを確認してください。  
- `counterBuffer` の 2 要素（頂点数/インデックス数）が 0 のままの場合、SubMesh の indexCount が更新されずレンダラーが無効化されます。`gridResolution` や `particleCount` の設定ミスがないか確認します。  
- `gridResolution` を大きくすると `Dispatch` 回数が急増します。パフォーマンスが落ちる場合は解像度か粒子数を抑えてください。  
- VFX Graph 側でパーティクルが書き込まれていない場合は `Custom HLSL` ブロックの接続や `ParticleCount` の値を再確認してください。
