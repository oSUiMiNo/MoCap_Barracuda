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
public class VNectBarracudaRunner_Old : MonoBehaviour
{
    // ONNXモデルをインポートして出来たUnityアセットがNNモデル
    [SerializeField] NNModel NNModel;
    // ロードしたNNモデルが入る
    Model model;
    // Barracuda のワーカー
    IWorker worker;
    [SerializeField] WorkerFactory.Type WorkerType = WorkerFactory.Type.Auto;
    // Verbose を true にするとエラー特定やデバッグがしやすいらしい
    [SerializeField] bool Verbose = false;
    // 関節情報の自作モデル
    [SerializeField] VNectModel vNectModel;
    // 動画流すコンポーネント
    [SerializeField] VideoCapture videoCapture;
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
    // 動画から取った入力画像テンソル
    Tensor VideoInput => new Tensor(videoCapture.InputTexture);
    // メッセージ表示用テキスト
    TextMeshProUGUI Msg => GameObject.Find("Msg").GetComponent<TextMeshProUGUI>();


    // 食わせる入力画像テンソルの3フレーム分。名前はこうじゃないといけないっぽい。NNモデルの都合？
    const string inputName_1 = "input.1";
    const string inputName_2 = "input.4";
    const string inputName_3 = "input.7";
    Dictionary<string, Tensor> inputs = new Dictionary<string, Tensor>() {
        { inputName_1, null },
        { inputName_2, null },
        { inputName_3, null },
    };


    // 関節のいろんな情報が入ったデータクラスの全関節分
    VNectModel.JointPoint[] jointPoints;
    // 関節数
    const int JointNum = 24;
    int JointNum_Squared => JointNum * 2;
    int JointNum_Cube => JointNum * 3;


    // 動画テクスチャを切り抜いてモーキャプに使うサイズ
    // ネットワークが受け取るザイズが448*448で作られてるので448
    [SerializeField] int InputImgSize = 448;
    float InputImgSizeHalf => InputImgSize / 2f;
    // ヒートマップカラム数に対するインプットイメージサイズの割合
    // 1カラム辺りなんピクセルを担うかとかなのか？
    float ImgScale => InputImgSize / (float)HeatMapCol;            // 224f / (float)InputImageSize;
    
    
    // ヒートマップカラム おそらく関節の個数と一致
    const int HeatMapCol = 28;                          // 数値変えたらアバターの挙動がやばくなった
    // ヒートマップカラム2乗
    int HeatMapCol_Squared => HeatMapCol * HeatMapCol;             //画像、動画のヒートマップカラム
    // ヒートマップカラム3乗
    int HeatMapCol_Cube => HeatMapCol * HeatMapCol * HeatMapCol;   // アバターのヒートマップカラム
    // ヒートマップカラム * 関節の個数
    int HeatMapCol_JointNum => HeatMapCol * JointNum;
    // ヒートマップカラム * ( 関節の個数 * 2 )
    int CubeOffsetLinear => HeatMapCol * JointNum_Cube;
    // ヒートマップカラムの2乗 * ( 関節の個数 * 2 )
    int CubeOffsetSquared => HeatMapCol_Squared * JointNum_Cube;

    //// バッファ ( 関節の個数 * ヒートマップカラムの3乗 ) 個の要素を持つfloat配列
    //// [Joint数] x 28 x 28 x 28 に相当する4次元テンソルが入る（実際には1次元配列に格納しているが添字計算で4次元相当になっている)
    //// 各ボクセルに「関節 i がここに存在する確率 (スコア)」が格納されているイメージ
    //// そして (x, y, z) を全探索し、一番スコアが高いボクセルを「関節 i の推定位置」として検出
    //// if (v > jointPoints[i].Score3D) {...} のように最大値を探している
    //float[] heatMap3D; // VNectに使う
    //// バッファ ( 関節の個数 * ヒートマップカラムの3乗 * 3 ) 個の要素を持つfloat配列
    //float[] offset3D;
    ///*【オフセット】
    //プログラミング開発の中でオフセットとは、別の場所と比較データの２点間の距離です。
    //オフセットは２つのメモリの位置の間の距離を表すためにつかわれます。*/

    
    // フィルタ用パラメータ
    [SerializeField] float KalmanParamQ = 0.001f;
    [SerializeField] float KalmanParamR = 0.0015f;
    [SerializeField] float LowPassParam = 0.1f;
    // ローパスフィルタを使うかどうか
    [SerializeField] bool UseLowPassFilter = true;


    // 初期化完了フラグ
    bool Initialized = false;


    IEnumerator Start()
    {
        // VNectModel 初期化
        jointPoints = vNectModel.Init();
        // 謎
        heatMap3D = new float[JointNum * HeatMapCol_Cube];
        offset3D = new float[JointNum * HeatMapCol_Cube * 3];
        //heatMap4D = new float[JointNum, HeatMapCol, HeatMapCol, HeatMapCol];
        //offset5D = new float[JointNum, HeatMapCol, HeatMapCol, HeatMapCol, 3];
        // NNモデルロード
        model = ModelLoader.Load(NNModel, Verbose);
        // ロードしたNNモデルからワーカー作成
        worker = WorkerFactory.CreateWorker(WorkerType, model, Verbose);
        // VideoCapture 初期化
        videoCapture.Play(InputImgSize, InputImgSize);
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
        // 更新
        if (Initialized) StartCoroutine(Exe());
    }


    // 実行
    IEnumerator Exe()
    {
        // NNへの入力データを更新
        if (!Initialized) // 初回
        {
            inputs[inputName_1] = InitialInput;
            inputs[inputName_2] = InitialInput;
            inputs[inputName_3] = InitialInput;
        }
        else             // 初回以降
        {
            inputs[inputName_3].Dispose();
            inputs[inputName_3] = inputs[inputName_2];
            inputs[inputName_2] = inputs[inputName_1];
            inputs[inputName_1] = VideoInput;
        }
        // 推論
        yield return StartCoroutine(Predict(inputs));
        // 推論結果をモデルに適用
        Apply();

        // 更新されたモデルをアバターに適用
        vNectModel.PoseUpdate();
    }



   
    float[] heatMap3D;
    float[] offset3D;
    // 推論
    IEnumerator Predict(Dictionary<string, Tensor> inputs)
    {
        // ログ：入力テンソルの形状
        //foreach (var input in inputs) Debug.Log($"Input {input.Key}: Shape = {input.Value.shape}")
        // 推論
        yield return worker.StartManualSchedule(inputs);
        // 推論結果を保存するバッファ
        Tensor[] b_outputs = new Tensor[4];
        // 推論結果取得
        for (var i = 2; i < model.outputs.Count; i++) b_outputs[i] = worker.PeekOutput(model.outputs[i]);
        // 使える推論結果を抜き取る
        offset3D = b_outputs[2].data.Download(b_outputs[2].shape);
        heatMap3D = b_outputs[3].data.Download(b_outputs[3].shape);
        // メモリ開放
        for (var i = 2; i < b_outputs.Length; i++) b_outputs[i].Dispose();
    }


    //NNの推論結果から得たスコアと関節位置を各関節に適用s
    void Apply()
    {
        // 各デフォルト関節について
        for (var i = 0; i < JointNum; i++)
        {
            //----------------------------------
            // 各デフォルト関節の存在確率が
            // 一番高いボクセルとその確率を特定
            //----------------------------------
            // 各関節の推論スコア(関節の存在確率が一番高いボクセルの確率)
            jointPoints[i].Score3D = 0.0f;
            // 最高スコアが出たボクセルのインデックス
            var maxXIndex = 0;
            var maxYIndex = 0;
            var maxZIndex = 0;

            // heatMap3Dは概念的には次のような 4 次元構造を持っている heatMap3D[joint i][z][y][x] ただし1次元配列 に並べている
            // i が 1 増えるごとに “HeatMapCol 個分” 配列が先に進む
            // 関節 i ごとの先頭インデックスを決めるイメージ
            var jj = i * HeatMapCol;
            for (var z = 0; z < HeatMapCol; z++)
            {
                // 先ほどの i *HeatMapCol に対して z を加算
                // つまり 「関節 i の中で、z を 1 増やすと配列が1個進む」 
                // ここまでで “(i, z) の組み合わせ” を 1次元へマッピング
                var zz = jj + z;
                for (var y = 0; y < HeatMapCol; y++)
                {
                    var yy = y * HeatMapCol_Squared * JointNum + zz;　// 謎
                    for (var x = 0; x < HeatMapCol; x++)
                    {
                        // ヒートマップ中で関節位置の箇所のスコアを取得
                        float v = heatMap3D[yy + x * HeatMapCol_JointNum];
                        // スコアが閾値(今回は初期値の0.0)よりも高い場合は採用 (関節データに適用)
                        // 全探索して最大スコアのインデックスを探すために、Score3Dがより大きければ更新
                        if (v > jointPoints[i].Score3D)
                        {
                            jointPoints[i].Score3D = v;
                            maxXIndex = x;
                            maxYIndex = y;
                            maxZIndex = z;
                        }
                    }
                }
            }



            //----------------------------------
            // 各デフォルト関節の位置計算してモデルに適用
            //----------------------------------
            //  関節[i]のX座標
            jointPoints[i].Now3D.x =
            (
                offset3D
                [
                    maxXIndex * CubeOffsetLinear +
                    maxYIndex * CubeOffsetSquared +
                    maxZIndex +
                    HeatMapCol * i
                ]
                + 0.5f
                + (float)maxXIndex
            ) * ImgScale -
            InputImgSizeHalf;

            // 関節[i]のY座標
            jointPoints[i].Now3D.y =
            InputImgSizeHalf -
            (
                offset3D
                [
                    maxXIndex * CubeOffsetLinear +
                    maxYIndex * CubeOffsetSquared +
                    maxZIndex +
                    HeatMapCol * (i + JointNum)
                ]
                + 0.5f
                + (float)maxYIndex
            ) * ImgScale;

            //  関節[i]のZ座標
            jointPoints[i].Now3D.z =
            (
                offset3D
                [
                    maxXIndex * CubeOffsetLinear +
                    maxYIndex * CubeOffsetSquared +
                    maxZIndex +
                    HeatMapCol * (i + JointNum_Squared)
                ]
                + 0.5f
                + (float)(maxZIndex - 14)
            ) * ImgScale;
        }

        //----------------------------------
        // 追加の関節位置計算
        //----------------------------------
        // 尻位置 = 中間 [ 左右もも中間 , 腹上部 ]
        jointPoints[PositionIndex.hip.Int()].Now3D = ((
                jointPoints[PositionIndex.rThighBend.Int()].Now3D + // 右もも
                jointPoints[PositionIndex.lThighBend.Int()].Now3D   // 左もも
                ) / 2f
                + jointPoints[PositionIndex.abdomenUpper.Int()].Now3D // 腹上部
            ) / 2f;

        // 首位置 = 左右肩中間
        jointPoints[PositionIndex.neck.Int()].Now3D = (
            jointPoints[PositionIndex.rShldrBend.Int()].Now3D + // 左肩
            jointPoints[PositionIndex.lShldrBend.Int()].Now3D   // 右肩
            ) / 2f;

        // 脊椎位置 = 腹上部
        jointPoints[PositionIndex.spine.Int()].Now3D = jointPoints[PositionIndex.abdomenUpper.Int()].Now3D;

        // (首からの相対)頭方向 = ノーマライズ [ 左右耳中間 - 首 ]
        var headDir = Vector3.Normalize((
                jointPoints[PositionIndex.rEar.Int()].Now3D + // 右耳
                jointPoints[PositionIndex.lEar.Int()].Now3D   // 左耳
                ) / 2f
                - jointPoints[PositionIndex.neck.Int()].Now3D // 首
            );

        // (首からの相対)鼻ベクトル = [ 鼻 - 首 ]
        var noseVec =
            jointPoints[PositionIndex.Nose.Int()].Now3D - // 鼻
            jointPoints[PositionIndex.neck.Int()].Now3D;  // 首
        // (首からの相対)頭位置 = 頭方向 * 内積 [ 鼻ベクトル , 頭方向 ]
        var localHeadPos = headDir * Vector3.Dot(headDir, noseVec);
        // 頭位置 = 首 + (首からの相対)頭位置
        jointPoints[PositionIndex.head.Int()].Now3D = jointPoints[PositionIndex.neck.Int()].Now3D + localHeadPos;

        //----------------------------------
        // フィルタ
        //----------------------------------
        // カルマンフィルタ
        foreach (var jp in jointPoints) KalmanUpdate(jp);
        // ローパスフィルタ
        if (UseLowPassFilter)
            foreach (var jp in jointPoints) LowPassFilter(jp);
    }



    // カルマンフィルタ
    void KalmanUpdate(VNectModel.JointPoint jp)
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


    // ローパスフィルタ
    void LowPassFilter(VNectModel.JointPoint jp)
    {
        jp.PrevPos3D[0] = jp.Pos3D;
        for (var i = 1; i < jp.PrevPos3D.Length; i++)
            jp.PrevPos3D[i] = jp.PrevPos3D[i] * LowPassParam + jp.PrevPos3D[i - 1] * (1f - LowPassParam);
        jp.Pos3D = jp.PrevPos3D[jp.PrevPos3D.Length - 1];
    }
}
