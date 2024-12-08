using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using TMPro;


// 関節情報を推定するアルゴリズム
public class VNectBarracudaRunner : MonoBehaviour
{
    // ONNXモデルをインポートして出来たUnityアセットがNNモデル
    public NNModel NNModel;
    // ロードしたNNモデルが入る
    private Model _model;
    // Barracuda のワーカー
    private IWorker _worker;
    public WorkerFactory.Type WorkerType = WorkerFactory.Type.Auto;
    // Verbose を true にするとエラー特定やデバッグがしやすいらしい
    public bool Verbose = false;
    // 関節情報の自作モデル
    public VNectModel VNectModel;

    // 動画流すコンポ
    public VideoCapture videoCapture;
    // キャリブレーション用画像
    public Texture2D InitImg;
    // キャリブレーション用の入力画像テンソル
    Tensor InitialInput => new Tensor(InitImg);

    // 動画から取った入力画像テンソル
    Tensor VideoInput => new Tensor(videoCapture.MainTexture);
    // テキスト
    public TextMeshProUGUI Msg => GameObject.Find("Msg").GetComponent<TextMeshProUGUI>();

    // キャリブレーション完了フラグ
    private bool Calibrated = false;

    // 食わせる入力画像テンソルの3フレーム分。名前はこうじゃないといけないっぽい。モデルの都合？
    const string inputName_1 = "input.1";
    const string inputName_2 = "input.4";
    const string inputName_3 = "input.7";
    Dictionary<string, Tensor> inputs = new Dictionary<string, Tensor>() {
        { inputName_1, null },
        { inputName_2, null },
        { inputName_3, null },
    };

    // 関節のいろんな情報が入ったデータクラスの全関節分
    private VNectModel.JointPoint[] jointPoints;
    // 関節数
    private const int JointNum = 24;
    private int JointNum_Squared = JointNum * 2;
    private int JointNum_Cube = JointNum * 3;

    // 動画テクスチャを切り抜いてモーキャプに使うサイズ
    // 大きすぎても小さすぎても制度が悪く、
    // 多分動画内の人間と同じくらいにするといい感じっぽい VideoCapture 内で初期化するらしい。
    public int InputImageSize;
    private float InputImageSizeHalf => InputImageSize / 2f;
    // ヒートマップカラム数に対するインプットイメージサイズの割合
    // 1カラム辺りなんピクセルを担うかとかなのか？
    private float ImageScale => InputImageSize / (float)HeatMapCol;// 224f / (float)InputImageSize;

    // ヒートマップカラム
    public int HeatMapCol;     //　数値変えたらアバターの挙動がやばくなった
    // ヒートマップカラム2乗
    private int HeatMapCol_Squared => HeatMapCol * HeatMapCol;     //画像、動画のヒートマップカラム
    // ヒートマップカラム3乗
    private int HeatMapCol_Cube => HeatMapCol * HeatMapCol * HeatMapCol;  // アバターのヒートマップカラム

    // バッファ ( 関節の個数 * ヒートマップカラムの3乗 ) 個の要素を持つfloat配列
    private float[] heatMap3D; // VNectに使う

    // バッファ ( 関節の個数 * ヒートマップカラムの3乗 * 3 ) 個の要素を持つfloat配列
    private float[] offset3D;
    /*【オフセット】
    プログラミング開発の中でオフセットとは、別の場所と比較データの２点間の距離です。
    オフセットは２つのメモリの位置の間の距離を表すためにつかわれます。 */


    // ヒートマップカラム * 関節の個数
    private int HeatMapCol_JointNum => HeatMapCol * JointNum;
    // ヒートマップカラム * ( 関節の個数*2 )
    private int CubeOffsetLinear => HeatMapCol * JointNum_Cube;
    // ヒートマップカラムの2乗 * ( 関節の個数*2 )
    private int CubeOffsetSquared => HeatMapCol_Squared * JointNum_Cube;


    // フィルタ用パラメータ
    public float KalmanParamQ;
    public float KalmanParamR;
    public float LowPassParam;
    // ローパスフィルタを使うかどうか
    public bool UseLowPassFilter;



    IEnumerator Start()
    {
        heatMap3D = new float[JointNum * HeatMapCol_Cube];
        offset3D = new float[JointNum * HeatMapCol_Cube * 3];

        // 端末のスリープ禁止
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // NNモデルロード
        _model = ModelLoader.Load(NNModel, Verbose);
        // ロードしたNNモデルからワーカー作成
        _worker = WorkerFactory.CreateWorker(WorkerType, _model, Verbose);

        // キャリブレーション
        yield return StartCoroutine(Calibrate());

        // UIの文字をクリア
        Msg.text = "";


    }


    void Update()
    {
        // 更新
        if (Calibrated) StartCoroutine(UpdateVNectModel());
    }


    IEnumerator Calibrate()
    {
        // 更新
        yield return StartCoroutine(UpdateVNectModel());

        // VideoCapture 初期化
        videoCapture.Init(InputImageSize, InputImageSize);

        // キャリブレーション完了フラグ
        Calibrated = true;
    }


    IEnumerator ExecuteModelAsync(Dictionary<string, Tensor> inputs)
    {
        // 推論
        yield return _worker.StartManualSchedule(inputs);

        // 推論結果を保存するバッファ
        Tensor[] b_outputs = new Tensor[4];

        // 推論結果取得
        for (var i = 2; i < _model.outputs.Count; i++) b_outputs[i] = _worker.PeekOutput(_model.outputs[i]);

        // 使える推論結果を抜き取る
        offset3D = b_outputs[2].data.Download(b_outputs[2].shape);
        heatMap3D = b_outputs[3].data.Download(b_outputs[3].shape);

        // メモリ開放
        for (var i = 2; i < b_outputs.Length; i++) b_outputs[i].Dispose();
    }


    IEnumerator UpdateVNectModel()
    {
        // インプットデータを更新
        if (!Calibrated)
        {
            inputs[inputName_1] = InitialInput;
            inputs[inputName_2] = InitialInput;
            inputs[inputName_3] = InitialInput;
        }
        else
        {
            inputs[inputName_3].Dispose();
            inputs[inputName_3] = inputs[inputName_2];
            inputs[inputName_2] = inputs[inputName_1];
            inputs[inputName_1] = VideoInput;
        }

        // ログ：入力テンソルの形状
        //foreach (var input in inputs) Debug.Log($"Input {input.Key}: Shape = {input.Value.shape}");

        // 推論実行
        yield return StartCoroutine(ExecuteModelAsync(inputs));

        // VNectModel 初期化
        if (!Calibrated) jointPoints = VNectModel.Init();

        // 姿勢推定
        PredictPose();
    }



    //姿勢推定
    void PredictPose()
    {
        // ======== 各間接に対して何かしてる ========
        for (var j = 0; j < JointNum; j++)
        {
            // この4パラメータを決めるためのループっぽい
            jointPoints[j].score3D = 0.0f;
            var maxXIndex = 0;
            var maxYIndex = 0;
            var maxZIndex = 0;

            var jj = j * HeatMapCol; // 謎
            for (var z = 0; z < HeatMapCol; z++)
            {
                var zz = jj + z;　// 謎
                for (var y = 0; y < HeatMapCol; y++)
                {
                    var yy = y * HeatMapCol_Squared * JointNum + zz;　// 謎
                    for (var x = 0; x < HeatMapCol; x++)
                    {
                        float v = heatMap3D[yy + x * HeatMapCol_JointNum]; // 謎
                        if (v > jointPoints[j].score3D)
                        {
                            jointPoints[j].score3D = v;
                            maxXIndex = x;
                            maxYIndex = y;
                            maxZIndex = z;
                        }
                    }
                }
            }

            jointPoints[j].Now3D.x = // =======
            (
                offset3D
                [
                    maxXIndex * CubeOffsetLinear +
                    maxYIndex * CubeOffsetSquared +
                    maxZIndex +
                    HeatMapCol * j
                ]
                + 0.5f
                + (float)maxXIndex
            ) * ImageScale -
            InputImageSizeHalf;

            jointPoints[j].Now3D.y = // =======
            InputImageSizeHalf -
            (
                offset3D
                [
                    maxXIndex * CubeOffsetLinear +
                    maxYIndex * CubeOffsetSquared +
                    maxZIndex +
                    HeatMapCol * (j + JointNum)
                ]
                + 0.5f
                + (float)maxYIndex
            ) * ImageScale;

            jointPoints[j].Now3D.z = // =======
            (
                offset3D
                [
                    maxXIndex * CubeOffsetLinear +
                    maxYIndex * CubeOffsetSquared +
                    maxZIndex +
                    HeatMapCol * (j + JointNum_Squared)
                ]
                + 0.5f
                + (float)(maxZIndex - 14)
            ) * ImageScale;
        }

        // ========== 追加の関節位置を計算 ==========
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


        // ================ フィルタ ================
        // カルマンフィルタ
        foreach (var jp in jointPoints)
        {
            KalmanUpdate(jp);
        }
        // ローパスフィルタ
        if (UseLowPassFilter)
        {
            foreach (var jp in jointPoints)
            {
                LowPassFilter(jp);
            }
        }
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
        {
            jp.PrevPos3D[i] = jp.PrevPos3D[i] * LowPassParam + jp.PrevPos3D[i - 1] * (1f - LowPassParam);
        }
        jp.Pos3D = jp.PrevPos3D[jp.PrevPos3D.Length - 1];
    }
}
