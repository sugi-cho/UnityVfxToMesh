## VFX から SDF から Mesh パイプライン

このプロジェクトは Unity 6 (6000.2.8f1) + URP + Visual Effect Graph 17.2.0 を前提に、VFX Graph 上で更新したパーティクルを GPU `GraphicsBuffer` に書き出し、Compute Shader で SDF → Naive Surface Nets メッシュ化し、生成した `Mesh` を複数の `MeshRenderer` に共有するまでを自動化する最小構成です。描画マテリアルは各 `MeshRenderer` 側で自由に差し替えられます。

### 主なアセット

- `Shaders/VfxToSdf.compute`
  VFX Graph から出力された `StructuredBuffer<float4>`（xyz: 位置, w: 半径）をもとに SDF を生成する Compute Shader です。`ClearSdf` / `StampParticles` の 2 カーネルのみを持ち、SDF 書き込みに専念します。
- `Shaders/SdfToMesh.compute`
  作成済みの SDF テクスチャを読み取り、Naive Surface Nets で頂点・法線・インデックスを構築する Compute Shader です。`ClearCells` / `BuildSurfaceVertices` / `BuildSurfaceIndices` を含み、Mesh 出力専用に分離しています。
- **Material (optional)**  
  パイプラインは Mesh の生成と `MeshFilter` への割り当てのみを行うため、どのシェーダー/マテリアルを使うかは `MeshRenderer` 側で決めます。
- `Assets/VfxToMesh/VFX/ParticleField.vfx`  
  パーティクルの初期化・アップデート・描画設定をまとめた VFX Graph。Update コンテキストに `Custom HLSL` ブロック（`Write Particle Buffer`）を挿入し、`GraphicsBuffer` `ParticlePositions` へ Position/Size を書き戻します。
- `Scripts/SdfVolumeSource.cs`  
  SDFボリュームのデータ構造と `SdfVolumeSource` 基底クラス、Compute Shader へ共通パラメータを渡すヘルパーをまとめています。将来的な SDF 生成・加工コンポーネントはここを継承します。
- `Scripts/VfxToSdf.cs`  
  Visual Effect Graph の粒子バッファを読み取り、SDF 3D テクスチャを生成して `SdfVolume` として公開する実装です。
- `Scripts/SdfToMesh.cs`  
  任意の `SdfVolumeSource` (標準では `VfxToSdf`) を受け取り、Naive Surface Nets で Mesh を構築して複数の `MeshFilter` (`targetMeshes`) に共有します。描画は各 `MeshRenderer` 側で通常どおり行えます。
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

3. **`VfxToSdf` / `SdfToMesh` の主なプロパティ**  
   - `VfxToSdf`: `sdfCompute` (SDF 用 Compute Shader)、`targetVfx`、`gridResolution`、`particleCount`、`boundsSize`、`isoValue`、`sdfFar`、`allowUpdateInEditMode` などで SDF 出力を制御します。  
   - `SdfToMesh`: `meshCompute`、`sdfSource` (任意の `SdfVolumeSource`) 、`targetMeshes` (`MeshFilter` のリスト)、`allowUpdateInEditMode` を設定すると、入力 SDF から生成した Mesh を複数の `MeshFilter` へ共有できます。  
4. **処理フロー**  
   1. VFX Graph で `ParticlePositions` バッファへ `float4(position, radius)` を書き出します。  
   2. `VfxToSdf` が SDF をクリア→粒子をスタンプし、必要なら中間 `SdfVolumeSource` で加工してから `SdfToMesh` へ渡します。  
   3. `SdfToMesh` がカウンタバッファからインデックス数を読み出し、生成済み `Mesh` の `SubMeshDescriptor` を更新します。  
   4. `targetMeshes` の各 `MeshFilter` に同じ `Mesh` をアサインし、任意のマテリアルで描画します。

## トラブルシューティング

- メッシュが描画されない場合は `targetMeshes` に必要な `MeshFilter` が登録されているか、紐づく `MeshRenderer` が有効か、`targetVfx` が設定されているかを確認してください。
- `counterBuffer` の 2 要素（頂点数/インデックス数）が 0 のままの場合、SubMesh の indexCount が更新されずレンダラーが無効化されます。`gridResolution` や `particleCount` の設定ミスがないか確認します。  
- `gridResolution` を大きくすると `Dispatch` 回数が急増します。パフォーマンスが落ちる場合は解像度か粒子数を抑えてください。  
- VFX Graph 側でパーティクルが書き込まれていない場合は `Custom HLSL` ブロックの接続や `ParticleCount` の値を再確認してください。
