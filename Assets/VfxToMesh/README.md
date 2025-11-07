## VFX → SDF → Naive Surface Nets → Indirect Draw

このフォルダには、VFX Graph で再生している GPU 粒子と同じ `StructuredBuffer` を Compute Shader に共有し、SDF → Naive Surface Nets → `DrawProceduralIndirect` でメッシュ化するための最低限のアセットがまとまっています。

### 含まれる主要アセット

- `Shaders/VfxToMesh.compute`  
  粒子の初期化/Integrate、SDF のクリアとパーティクルスタンプ、Naive Surface Nets の頂点＆インデックス生成をすべて GPU 上で実行します。  
  頂点はセル単位で 1 つずつ生成し、面の接続は Naive Surface Nets の原著論文に沿って 3 軸分のクアッドを張るシンプルな実装です。
- `Shaders/SurfaceNetIndirect.shader` + `Materials/SurfaceNet.mat`  
  `StructuredBuffer` に格納された頂点・法線・バリセントリックを直接サンプリングし、`Graphics.DrawProceduralIndirect` の `SV_VertexID` からインデックスを引いて描画します。線表示はバリセントリック座標から簡易的に生成しています。
- `Shaders/SdfSliceDebug.shader` + `Materials/SdfSliceDebug.mat`  
  3D RenderTexture のスライスを Visualize するための URP シェーダーです。`VfxToMeshPipeline` からスライス軸と深度を更新します。
- `Scripts/VfxToMeshPipeline.cs`  
  すべてのバッファ管理、Compute Shader 呼び出し、`DrawProceduralIndirect`、VFX Graph への `GraphicsBuffer` バインド、デバッグマテリアルの更新を 1 つの MonoBehaviour で制御します。
- `Editor/PipelineBootstrap.cs`  
  メニュー `Tools/Vfx To Mesh/Rebuild Playground` や、`Unity.exe -executeMethod VfxToMesh.Editor.PipelineBootstrap.BuildSceneHeadless` から呼び出せるセットアップスクリプトです。サンプルシーンを生成し、カメラ・ライト・VFX・デバッグ用クアッドを自動配置します。

## 使い方

1. **サンプルシーン生成**  
   Unity を開いた状態で `Tools/Vfx To Mesh/Rebuild Playground` を実行すると、`Assets/VfxToMesh/Scenes/VfxToMesh.unity` が再生成されます。  
   同コマンドはシーンを上書きするので、変更済みの際はコミット/バックアップしてください。  

2. **VFX Graph 側のセットアップ**  
   `Assets/VfxToMesh/VFX/ParticleField.vfx` はテンプレートとして同梱されています。以下のように Blackboard と Context を調整すると、`VfxToMeshPipeline` から渡している `GraphicsBuffer` をそのまま表示に利用できます。
   - Blackboard に `GraphicsBuffer` プロパティ `_ParticlePositions`（Exposed）を追加します。
   - 同様に `int` プロパティ `_ParticleCount` を追加し、Spawner の `Set Spawn Count` に接続します。
   - Initialize Context で `Sample Buffer` オペレーターを作成し、`_ParticlePositions` を対象、`Mode = Indexed` に変更、`Index` に `particleId`、`Count` に `_ParticleCount` を入力します。
   - `Set Position` ブロックの入力に `Sample Buffer` の結果（`float3`）をつなげば、Compute Shader が書き込む粒子位置をそのまま GPU で描画できます。
   - 必要に応じて `Set Lifetime` や `Set Color` を追加して見た目を整えてください。

   > **補足:** 現行の VFX Graph API ではシミュレーション済みバッファを外部へ直接エクスポートできないため、本プロジェクトでは「Compute Shader が粒子位置を共有バッファに書き込み、VFX Graph とメッシャが双方で参照する」という構成を採用しています。

3. **`VfxToMeshPipeline` のパラメータ**  
   - `gridResolution` … 64〜128 程度を推奨。値を上げると 3D テクスチャとセル配列が指数的に増えるため、VRAM の空き容量に注意してください。
   - `particleCount` … 10,000 まで想定。Compute Shader の 1D グループ（256 スレッド）単位でディスパッチされます。
   - `boundsSize` / `particleRadius` … 粒子の可動域と SDF の基準スケールです。`_VoxelSize` は現在 X 成分から算出しているので、等方なボックスを使うと精度が安定します。
   - `noiseFrequency` / `noiseStrength` / `velocityDamping` … `IntegrateParticles` カーネル内のフィールドを調整し、VFX Graph の動きと SDF を同期させます。
   - `sliceMaterial` を指定すると、`SdfSliceDebug` シェーダーで 3D テクスチャの断面を確認できます。

4. **描画フロー**  
   `Update` 毎に以下を実行します。
   - 粒子をフィールドで更新し、`GraphicsBuffer` `_ParticlePositions` に書き戻す。
   - SDF をクリア → 粒子ごとに周辺ボクセルをスタンプ。
   - Naive Surface Nets で zero-crossing セルに頂点を作成し、隣接セルからインデックスを生成。
   - 頂点数/インデックス数を CPU へ 2 要素だけリードバックし、`IndirectArguments` に設定。
   - `Graphics.DrawProceduralIndirect` で `SurfaceNetIndirect` シェーダーを描画。

## デバッグ

- `SdfSliceDebug` マテリアルを割り当てたクアッドは透過表示で SDF のサインを視覚化します。`debugSliceAxis`（0:X, 1:Y, 2:Z）と `debugSliceDepth` を `VfxToMeshPipeline` から変更可能です。
- `SurfaceNetIndirect` のワイヤーフレームはバリセントリック座標ベースの簡易実装なので、詳細なエッジ確認が必要な場合は `wireThickness` を増やすか、`Gizmos.DrawWireCube` などを併用してください。

## 既知の制限

- Visual Effect Graph 側で粒子属性バッファを公式にエクスポートする API が無いため、パーティクルの生成・更新は `VfxToMeshPipeline` の Compute Shader が実質的に担っています。VFX Graph は共有バッファを入力として描画のみ行います。
- `GraphicsBuffer.GetData` で頂点/インデックスのカウンタを CPU に戻しているため、フレーム毎に 2 個の `uint` を同期します。必要であれば `AsyncGPUReadback` に置き換えるか、`Counter` 付き `AppendBuffer` に変更してください。
- VFX Graph のアセット `ParticleField.vfx` はテンプレート状態で格納されています。上記手順で Buffer を読み込むブロックを追加してからご利用ください。
