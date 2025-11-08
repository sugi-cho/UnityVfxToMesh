## VFX → SDF → Mesh パイプライン

このプロジェクトは Unity 6 (6000.2.8f1) + URP + Visual Effect Graph 17.2.0 を前提に、VFX Graph 上で更新したパーティクルを GPU `GraphicsBuffer` に書き出し、Compute Shader 側でボリューム SDF と Naive Surface Nets メッシュを生成して `Graphics.DrawProceduralIndirect` で描画するための最小構成です。

### 主なアセット

- `Shaders/VfxToMesh.compute`  
  VFX Graph から渡された `StructuredBuffer<float4>`（xyz: 位置, w: 半径）を参照し、SDF クリア → パーティクルの球スタンプ → セル毎のゼロクロス検出 → Naive Surface Nets 頂点/インデックス生成を GPU で行います。
- `Shaders/SurfaceNetIndirect.shader` + `Materials/SurfaceNet.mat`  
  Compute で構築した頂点・法線・三角インデックス・バリセントリック座標バッファをそのまま `SV_VertexID`／`SV_InstanceID` で解釈し、ワイヤ／シェーディングの両方を 1 パスで描画します。
- `Shaders/SdfSliceDebug.shader` + `Materials/SdfSliceDebug.mat`  
  3D RenderTexture に蓄積した SDF を任意軸でスライス表示する URP Unlit シェーダーです。`debugSliceAxis` / `debugSliceDepth` を `VfxToMeshPipeline` から制御します。
- `Assets/VfxToMesh/VFX/ParticleField.vfx`  
  パーティクルの初期化・アップデート・描画を担う VFX Graph。本プロジェクトでは **Update コンテキスト内に `Custom HLSL` ブロック（`Write Particle Buffer`）を配置し、`GraphicsBuffer` `ParticlePositions` へ各パーティクルの Position/Size を毎フレーム書き出します。**
- `Scripts/VfxToMeshPipeline.cs`  
  すべての `GraphicsBuffer` と 3D RenderTexture の確保・Compute Shader のディスパッチ・`DrawProceduralIndirect` 呼び出し・デバッグスライス更新を 1 つの `MonoBehaviour` で管理します。
- `Editor/PipelineBootstrap.cs`  
  `Tools/Vfx To Mesh/Rebuild Playground` あるいは CI から `Unity.exe -executeMethod VfxToMesh.Editor.PipelineBootstrap.BuildSceneHeadless` を呼び出すことで、基本シーン・カメラ・ライティング・`VfxToMeshPipeline`・`ParticleField.vfx`・スライス板を再生成します。

## 使い方

1. **Playground を再生成**  
   プロジェクトを Unity で開き、`Tools/Vfx To Mesh/Rebuild Playground` を実行して `Assets/VfxToMesh/Scenes/VfxToMesh.unity` を上書きします。既存シーンを保持したい場合はバックアップを取ってください。

2. **VFX Graph の Blackboard / Update 設定**  
   - Blackboard に以下の Exposed プロパティを追加します。  
     `GraphicsBuffer ParticlePositions`（モード: GraphicsBuffer）、`int ParticleCount`。  
     `ParticleCount` は Spawner の `Constant Spawn Rate` や `Trigger Event` など好きな箇所で利用できます。
   - Initialize コンテキストでは通常どおり位置・速度・サイズを決めてください（ランダムキューブやノイズなど構成自由）。Compute Shader 側のシミュレーションは廃止済みなので、動きは VFX Graph で完結させます。
   - Update コンテキストにある `Gravity` ブロックの後段に `Custom HLSL` ブロック **`Write Particle Buffer`** を追加し、`GraphicsBuffer` 入力を `ParticlePositions` に接続します。既定の HLSL は次の通りです（アセットにも埋め込み済み）。**`attributes.alive == 0` の場合でも `float4(0,0,0,-1)` を書き込み、死んだパーティクルがバッファに残らないようにしています。**  
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
     これにより VisualEffect コンポーネントが `SetGraphicsBuffer("ParticlePositions", buffer)` で渡した GPU バッファへ、各パーティクルの位置と半径（size → radius 変換）を書き戻すことができます。
    `ParticleCount` を Custom HLSL ブロックの int 入力に接続しておけば、C# から渡した上限でバッファサイズが循環し、Kill 済みの粒子も新しいデータで順次上書きされます。実装ファイルは `Assets/VfxToMesh/Shaders/VfxWriteParticleBuffer.hlsl` です。

3. **`VfxToMeshPipeline` の主なパラメータ**  
   - `gridResolution`（64〜160 推奨）: SDF ボリュームの 1 辺あたりボクセル数。値を上げるとメッシュ精度が上がる一方、`gridResolution^3` の RFloat 3D テクスチャが必要になります。
   - `particleCount`: VFX Graph に生成させる粒子総数と `GraphicsBuffer` の確保数。VFX Graph 側の Spawn 数と一致させてください。
  - `boundsSize`: パーティクルを押し込める境界サイズ。粒子半径は VFX Graph から書き込まれる `ParticlePositions.w` をそのまま使用するため、パイプライン側にスライダーはありません。
   - `isoValue` / `sdfFar`: Surface Nets が零点を探すしきい値、およびボリューム初期値。
   - `sliceMaterial`: `null` であればスライスを更新しません。UI で切り替えると即座に 3D RT に反映されます。

4. **実行フロー（毎フレーム）**
- VFX Graph が Update → Custom HLSL ブロック経由で `ParticlePositions` に `float4(position, radius)` を書き出し、kill 済み ID は `radius = -1` で無効化する
- `VfxToMeshPipeline` が SDF クリア → 粒子スタンプ → セルデータ初期化 → Naive Surface Nets 頂点/法線/インデックス生成 → カウンタ読み戻し → `DrawProceduralIndirect`
   - 任意で `SdfSliceDebug` マテリアルに 3D RT を渡し、スライス可視化

## デバッグとヒント

- SDF が更新されているか確認するには `SdfSliceDebug` のマテリアルをシーン内の `SDF Slice` オブジェクトに割り当て、`debugSliceAxis` / `debugSliceDepth` を調整してください。
- メッシュが描画されない場合は `counterBuffer` の 2 要素目（インデックス数）が 0 になっていないか確認します。`VfxToMeshPipeline` は `argsBuffer` へ書き戻す前に `GetData` で CPU 側へ同期しているため、必要であればログを追加して可視化できます。
- `gridResolution` を大きくするとメモリ帯域と `Dispatch` 回数が急増します。パフォーマンスが厳しい場合は `particleCount` とセットでトレードオフを検討してください。
- VFX Graph 側でパーティクル挙動を作り込みたい場合は、既存の `Gravity` / `Forces` ブロックや `Simple Noise` オペレータを組み合わせてください。Compute Shader 側にはフロー場の概念が残っていないため、モーションはすべて VFX Graph に集約されます。
