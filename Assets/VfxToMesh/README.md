## VFX から SDF から Mesh パイプライン

このプロジェクトは Unity 6 (6000.2.8f1) + URP + Visual Effect Graph 17.2.0 を前提に、VFX Graph 上で更新したパーティクルを GPU `GraphicsBuffer` に書き出し、Compute Shader で SDF → Naive Surface Nets メッシュ化し、生成した `Mesh` を複数の `MeshRenderer` に共有するまでを自動化する最小構成です。描画マテリアルは各 `MeshRenderer` 側で自由に差し替えられます。

### 主なアセット

- `Shaders/VfxToMesh.compute`  
  VFX Graph から流入した `StructuredBuffer<float4>`（xyz: 位置, w: 半径）を SDF 化し、Naive Surface Nets でメッシュを構築します。
- **Material (optional)**  
  パイプラインは Mesh の生成と `MeshFilter` への割り当てのみを行うため、どのシェーダー/マテリアルを使うかは `MeshRenderer` 側で決めます。
- `Assets/VfxToMesh/VFX/ParticleField.vfx`  
  パーティクルの初期化・アップデート・描画設定をまとめた VFX Graph。Update コンテキストに `Custom HLSL` ブロック（`Write Particle Buffer`）を挿入し、`GraphicsBuffer` `ParticlePositions` へ Position/Size を書き戻します。
- `Scripts/VfxToMeshPipeline.cs`  
  SDF バッファ、カウンタ、`Mesh` バッファの確保と Compute Shader のディスパッチ、`targetVfx` / `targetRenderers` へのバインドを 1 つの `MonoBehaviour` で管理します。
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

3. **`VfxToMeshPipeline` の主なプロパティ**  
   - `pipelineCompute`: `Shaders/VfxToMesh.compute` を割り当てます。  
   - `targetVfx`: `ParticlePositions` を書き出す `VisualEffect`。`ConfigureVisualEffect` で GPU バッファをセットします。  
   - `targetRenderers`: `MeshRenderer` と `MeshFilter` のペアを複数登録できます。全て同じ `Mesh` を共有し、`subMeshDescriptor.indexCount` が 0 の間は自動で `renderer.enabled = false` になります。  
   - `gridResolution`: SDF ボリューム解像度。高くすると精度は上がりますがメモリ/コストも増えます。  
   - `particleCount`: VFX Graph 側の最大粒子数と一致させます。  
   - `boundsSize`: メッシュ化する領域のワールドサイズ。粒子半径は VFX Graph の size から決まります。  
   - `isoValue` / `sdfFar`: Surface Nets がゼロ交差を求める閾値と SDF の遠距離クランプ値。  
   - `allowUpdateInEditMode`: エディタ停止中でも Update を走らせる場合にオンにします。

4. **処理フロー**  
   1. VFX Graph が `ParticlePositions` バッファへ `float4(position, radius)` を書き戻す。  
   2. `VfxToMeshPipeline` が SDF クリア → 粒子スタンプ → セル初期化 → Naive Surface Nets 頂点/法線/インデックス生成を Compute Shader で実行。  
   3. カウンタバッファからインデックス数を読み出し、生成した `Mesh` の `SubMeshDescriptor` を更新。  
   4. `targetRenderers` の各 `MeshFilter` に同じ `Mesh` を共有させ、任意のマテリアルで描画する。

## トラブルシューティング

- メッシュが描画されない場合は `targetRenderers` に `MeshRenderer` / `MeshFilter` のペアが設定されているか、`targetVfx` がセットされているかを確認してください。  
- `counterBuffer` の 2 要素（頂点数/インデックス数）が 0 のままの場合、SubMesh の indexCount が更新されずレンダラーが無効化されます。`gridResolution` や `particleCount` の設定ミスがないか確認します。  
- `gridResolution` を大きくすると `Dispatch` 回数が急増します。パフォーマンスが落ちる場合は解像度か粒子数を抑えてください。  
- VFX Graph 側でパーティクルが書き込まれていない場合は `Custom HLSL` ブロックの接続や `ParticleCount` の値を再確認してください。
