using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using TMPro;

/*
■■ 用語定義 ■■
◆モデル：VNectModel
◆アバター：人型３Dモデル
◆NN：ニューラルネットワーク, ONNX
*/


// 関節情報を推定するアルゴリズム
public class VNectBarracudaRunner : MonoBehaviour
{
    // 関節情報の自作モデル
    [SerializeField] VNectModel VNect;
    // ONNXモデルをインポートして出来たUnityアセットがNNモデル
    [SerializeField] NNModel NN;
    // ロードしたNNモデルが入る
    Model model;
    // Barracuda のワーカー
    IWorker worker;
    [SerializeField] WorkerFactory.Type WorkerType = WorkerFactory.Type.Auto;


    // 動画流すコンポーネント
    [SerializeField] VideoCapture Video;
    // 動画から取った入力画像テンソル
    Tensor VideoInput => new Tensor(Video.InputTexture);

    // キャリブレーション用画像 全身が映った分かりやすい映像が望ましい
    [SerializeField] Texture2D InitImg;
    // キャリブレーション用の入力画像テンソル
    Tensor InitialInput {
        get {
            // 画像が設定されていた場合はそのテクスチャが初回キャプチャに使われる
            if (InitImg) return new Tensor(InitImg);
            // 画像が無い場合はいきなり入力動画のテクスチャを使う
            else return VideoInput;
        }
    }
    

    // 食わせる入力画像テンソルの3フレーム分。名前はこうじゃないといけないっぽい。NNモデルの都合？
    const string inputName1 = "input.1";
    const string inputName2 = "input.4";
    const string inputName3 = "input.7";
    Dictionary<string, Tensor> Inputs = new Dictionary<string, Tensor>() {
        { inputName1, null },
        { inputName2, null },
        { inputName3, null },
    };


    // 関節のいろんな情報が入ったデータクラスの全関節分
    VNectModel.JointPoint[] JointPoints;
    // 関節数
    const int JointNum = 24;


    // ヒートマップの１辺の目盛数
    // モデルの都合で28個 おそらく関節の個数と一致
    const int HeatMapCol = 28;
    // 各ボクセルに関節の存在確率が入った3次元のヒートマップ
    // [Joint数] x 28 x 28 x 28 に相当する4次元テンソルが入る（実際には1次元配列に格納しているが添字計算で4次元相当になっている)
    // 各ボクセルに「関節 i がここに存在する確率 (スコア)」が格納されているイメージ
    // そして (x, y, z) を全探索し、一番スコアが高いボクセルを「関節 i の推定位置」として検出
    // if (v > jointPoints[i].Score3D) {...} のように最大値を探している
    float[] HeatMap = new float[JointNum * HeatMapCol * HeatMapCol * HeatMapCol];
    // ヒートマップのボクセルの目が荒いのでもう少し細かく関節位置を決めるためのオフセット
    float[] Offset = new float[JointNum * HeatMapCol * HeatMapCol * HeatMapCol * 3];


    // 動画テクスチャを切り抜いてモーキャプに使うサイズ
    // ネットワークが受け取るザイズが448*448で作られてるので448
    [SerializeField] int InputImgSize = 448;
    float InputImgSizeHalf => InputImgSize / 2f;
    // ヒートマップカラム数に対するインプットイメージサイズの割合
    // 1カラム辺りなんピクセルを担うかとかなのか？
    float ImgScale => InputImgSize / (float)HeatMapCol;


    // フィルタ用パラメータ
    [SerializeField] float KalmanParamQ = 0.001f;
    [SerializeField] float KalmanParamR = 0.0015f;
    [SerializeField] float LowPassParam = 0.1f;
    // ローパスフィルタを使うかどうか
    [SerializeField] bool UseLowPassFilter = true;


    // 初期化完了フラグ
    bool Initialized = false;
    // メッセージ表示用テキスト
    TextMeshProUGUI Msg => GameObject.Find("Msg").GetComponent<TextMeshProUGUI>();




    IEnumerator Start()
    {
        // VNectModel 初期化
        JointPoints = VNect.Init();
        // NNモデルロード
        model = ModelLoader.Load(NN);
        // ロードしたNNモデルからワーカー作成
        worker = WorkerFactory.CreateWorker(WorkerType, model);
        // VideoCapture 初期化
        Video.Play(InputImgSize, InputImgSize);
        
        
        // 初回キャプチャ(キャリブレーション)
        yield return StartCoroutine(Exe());
       
        
        // UIの文字をクリア
        Msg.text = "";
        // 初期化完了フラグ
        Initialized = true;
        // 端末のスリープ禁止
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void Update()
    {
        if (Initialized) StartCoroutine(Exe());
    }


    //==============================================================
    // 各キャプチャフレームでの処理
    //==============================================================
    IEnumerator Exe()
    {
        // NNへの入力データを更新
        if (!Initialized) // 初回
        {
            Inputs[inputName1] = InitialInput;
            Inputs[inputName2] = InitialInput;
            Inputs[inputName3] = InitialInput;
        }
        else
        {
            Inputs[inputName3].Dispose();
            Inputs[inputName3] = Inputs[inputName2];
            Inputs[inputName2] = Inputs[inputName1];
            Inputs[inputName1] = VideoInput;
        }
        
        // 推論
        yield return StartCoroutine(Predict(Inputs));
        
        // 推論結果をモデルに適用
        ApplyDefaultJoint();
        ApplyAdditionalJoint();
        
        // カルマンフィルタ
        foreach (var jp in JointPoints) KalmanFilter(jp);
        
        // ローパスフィルタ
        if (UseLowPassFilter)
        foreach (var jp in JointPoints) LowPassFilter(jp);

        // 更新されたモデルをアバターに適用
        VNect.PoseUpdate();
    }


    //==============================================================
    // 推論
    //==============================================================
    IEnumerator Predict(Dictionary<string, Tensor> inputs)
    {
        // ログ：入力テンソルの形状
        //foreach (var input in inputs) Debug.Log($"Input {input.Key}: Shape = {input.Value.shape}")
        // 推論
        yield return worker.StartManualSchedule(inputs);
        // 推論結果取得
        Tensor[] b_outputs = new Tensor[4];
        for (var i = 2; i < model.outputs.Count; i++) b_outputs[i] = worker.PeekOutput(model.outputs[i]);
        // 使えるデータを抽出
        Offset = b_outputs[2].data.Download(b_outputs[2].shape);
        HeatMap = b_outputs[3].data.Download(b_outputs[3].shape);
    }


    //==============================================================
    // NNの推論結果から得たスコアと関節位置をモデルに適用
    //==============================================================
    void ApplyDefaultJoint()
    {
        // スコアヒートマップの添字計算
        int HeatMapIndex(int i, int z, int y, int x)
        {
            // (i,z,y,x) → heatMap3D[...] の1次元index
            return i * HeatMapCol
                 + x * (HeatMapCol * JointNum)
                 + y * (HeatMapCol * HeatMapCol * JointNum)
                 + z;
        }
        // オフセット添字計算:
        int OffsetIndex(int i, int x, int y, int z, string component)
        {
            // offset3D[ x * CubeOffsetLinear + y * CubeOffsetSquared + z + HeatMapCol * (i + ???)]
            int iOffset = i;
            if (component == "x") iOffset = i;                // Xオフセット
            else
            if (component == "y") iOffset = i + JointNum;     // Yオフセット
            else
            if (component == "z") iOffset = i + JointNum * 2; // Zオフセット

            // (i, x, y, z, c = x/y/z) → offset3D[...] の1次元index
            return iOffset * HeatMapCol
                 + x * HeatMapCol * JointNum * 3
                 + y * HeatMapCol * HeatMapCol * JointNum * 3
                 + z;
        }

        // 各関節について 最大スコアボクセル＆オフセット取得
        for (int i = 0; i < JointNum; i++)
        {
            //----------------------------------
            // 各関節の存在確率が一番高いボクセルと
            // その確率を特定
            //----------------------------------
            // 各関節の推論スコア(関節の存在確率が一番高いボクセルの確率)
            float maxScore = 0f;
            // 最高スコアが出たボクセルのインデックス
            int indexX = 0, indexY = 0, indexZ = 0;
            // ヒートマップを全探索
            for (int z = 0; z < HeatMapCol; z++)
            for (int y = 0; y < HeatMapCol; y++)
            for (int x = 0; x < HeatMapCol; x++)
            {
                // 1次元版index を算出
                int index = HeatMapIndex(i, z, y, x);
                // ヒートマップ中で関節位置の箇所のスコアを取得
                float score = HeatMap[index];
                // スコアが閾値(今回は初期値の0.0)よりも高い場合は採用 (関節データに適用)
                // 全探索して最大スコアのインデックスを探すために、Score3Dがより大きければ更新
                if (score > maxScore)
                {
                    maxScore = score;
                    indexX = x; 
                    indexY = y;
                    indexZ = z;
                }
            }
            // 見つかった maxX,maxY,maxZ でオフセット計算
            float offsetX = Offset[OffsetIndex(i, indexX, indexY, indexZ, "x")];
            float offsetY = Offset[OffsetIndex(i, indexX, indexY, indexZ, "y")];
            float offsetZ = Offset[OffsetIndex(i, indexX, indexY, indexZ, "z")];

            //----------------------------------
            // 関節[i]の存在確率が一番高いボクセルの
            // 関節[i]の存在確率を一応保存
            //----------------------------------
            JointPoints[i].Score3D = maxScore;

            //----------------------------------
            // 関節[i]の位置確定
            //----------------------------------
            //  関節[i]のX座標
            JointPoints[i].Now3D.x =
            (
                offsetX 
                + 0.5f
                + indexX
            ) * ImgScale
            - InputImgSizeHalf;

            //  関節[i]のY座標
            JointPoints[i].Now3D.y =
            InputImgSizeHalf -
            (
                offsetY
                + 0.5f
                + indexY
            ) * ImgScale;

            //  関節[i]のZ座標
            JointPoints[i].Now3D.z =
            (
                offsetZ
                + 0.5f
                + (indexZ - 14)
            ) * ImgScale;
        }
    }


    //==============================================================
    // 追加ポジを計算しモデルに適用
    //==============================================================
    void ApplyAdditionalJoint()
    {
        //----------------------------------
        // 追加ポジ計算
        //----------------------------------
        // 尻位置 = 中間 [ 左右もも中間 , 腹上部 ]
        JointPoints[PositionIndex.hip.Int()].Now3D = ((
                JointPoints[PositionIndex.rThighBend.Int()].Now3D +   // 右もも
                JointPoints[PositionIndex.lThighBend.Int()].Now3D     // 左もも
                ) / 2f
                + JointPoints[PositionIndex.abdomenUpper.Int()].Now3D // 腹上部
            ) / 2f;

        // 首位置 = 左右肩中間
        JointPoints[PositionIndex.neck.Int()].Now3D = (
            JointPoints[PositionIndex.rShldrBend.Int()].Now3D + // 左肩
            JointPoints[PositionIndex.lShldrBend.Int()].Now3D   // 右肩
            ) / 2f;

        // 脊椎位置 = 腹上部
        JointPoints[PositionIndex.spine.Int()].Now3D = JointPoints[PositionIndex.abdomenUpper.Int()].Now3D;

        // (首からの相対)頭方向 = ノーマライズ [ 左右耳中間 - 首 ]
        var headDir = Vector3.Normalize((
                JointPoints[PositionIndex.rEar.Int()].Now3D + // 右耳
                JointPoints[PositionIndex.lEar.Int()].Now3D   // 左耳
                ) / 2f
                - JointPoints[PositionIndex.neck.Int()].Now3D // 首
            );

        // (首からの相対)鼻ベクトル = [ 鼻 - 首 ]
        var noseVec =
            JointPoints[PositionIndex.Nose.Int()].Now3D - // 鼻
            JointPoints[PositionIndex.neck.Int()].Now3D;  // 首
        // (首からの相対)頭位置 = 頭方向 * 内積 [ 鼻ベクトル , 頭方向 ]
        var localHeadPos = headDir * Vector3.Dot(headDir, noseVec);
        // 頭位置 = 首 + (首からの相対)頭位置
        JointPoints[PositionIndex.head.Int()].Now3D = JointPoints[PositionIndex.neck.Int()].Now3D + localHeadPos;
    }


    //==============================================================
    // カルマンフィルタ
    //==============================================================
    void KalmanFilter(VNectModel.JointPoint jp)
    {
        // MeasurementUpdate
        jp.K.x = (jp.P.x + KalmanParamQ) / (jp.P.x + KalmanParamQ + KalmanParamR);
        jp.K.y = (jp.P.y + KalmanParamQ) / (jp.P.y + KalmanParamQ + KalmanParamR);
        jp.K.z = (jp.P.z + KalmanParamQ) / (jp.P.z + KalmanParamQ + KalmanParamR);
        jp.P.x = KalmanParamR * (jp.P.x + KalmanParamQ) / (KalmanParamR + jp.P.x + KalmanParamQ);
        jp.P.y = KalmanParamR * (jp.P.y + KalmanParamQ) / (KalmanParamR + jp.P.y + KalmanParamQ);
        jp.P.z = KalmanParamR * (jp.P.z + KalmanParamQ) / (KalmanParamR + jp.P.z + KalmanParamQ);
        // メイン処理
        jp.Pos3D.x = jp.X.x + (jp.Now3D.x - jp.X.x) * jp.K.x;
        jp.Pos3D.y = jp.X.y + (jp.Now3D.y - jp.X.y) * jp.K.y;
        jp.Pos3D.z = jp.X.z + (jp.Now3D.z - jp.X.z) * jp.K.z;
        jp.X = jp.Pos3D;
    }


    //==============================================================
    // ローパスフィルタ
    //==============================================================
    void LowPassFilter(VNectModel.JointPoint jp)
    {
        jp.PrevPos3D[0] = jp.Pos3D;
        for (var i = 1; i < jp.PrevPos3D.Length; i++)
            jp.PrevPos3D[i] = jp.PrevPos3D[i] * LowPassParam + jp.PrevPos3D[i - 1] * (1f - LowPassParam);
        jp.Pos3D = jp.PrevPos3D[jp.PrevPos3D.Length - 1];
    }
}
