using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Codice.Client.Commands.WkTree.WorkspaceTreeNode;
using static PlasticGui.LaunchDiffParameters;


// ポジションのインデクス
public enum PositionIndex : int
{
    //------------------------------------
    // 関節ポジ
    //------------------------------------
    // 腕
    rShldrBend = 0, // 0 上腕ボーンのTransformなので肩
    rForearmBend,　 // 1 前腕ボーンのTransformなので肘
    rHand,          // 2 手ボーンのTransformなので手首
    rThumb2,        // 3 親指中節骨ボーンのTransformなのでMP関節
    rMid1,          // 4 中指末節骨ボーンのTransformなのでMP関節
    lShldrBend,     // 5
    lForearmBend,   // 6
    lHand,          // 7
    lThumb2,        // 8
    lMid1,          // 9
    // 首から上     
    lEar,           // 10 左耳(実際は頭ボーンのTransformなので頭)
    lEye,           // 11 左目ボーンのTransformなので左目
    rEar,           // 12 右耳(実際は頭ボーンのTransformなので頭)
    rEye,           // 13 右目ボーンのTransformなので右目
    Nose,           // 14 鼻(手動で追加)
    // 脚
    rThighBend,     // 15 太ももボーンのTransformなので股関節
    rShin,          // 16 すねボーンのTransformなので膝
    rFoot,          // 17 足ボーンのTransformなので足首
    rToe,           // 18 つま先ボーンのTransformなのでつま先
    lThighBend,     // 19
    lShin,          // 20
    lFoot,          // 21
    lToe,           // 22
    // 胴   
    abdomenUpper,   // 23 腹部上部(実際は背骨第一ボーンのTransform)

    //------------------------------------
    // 追加ポジ(VNectBarracudaRunnerクラス内で既存の関節の位置から算出)
    //------------------------------------
    hip,            // 24 尻
    head,           // 25 頭
    neck,           // 26 首
    spine,          // 27 脊椎(実際は背骨第一ボーン)
}


public static partial class PositionIndexEx
{
    // 要素のインデックスを返す
    public static int Int(this PositionIndex i) => (int)i;
    // 要素数
    public static int Count(this Type type)
    {
        if (type != typeof(PositionIndex)) throw new ArgumentException("PositionIndex 以外の型が渡された", nameof(type));
        return Enum.GetValues(type).Length;
    }
}


public class VNectModel : MonoBehaviour
{
    public class JointPoint
    {
        public Vector3 Pos3D = new();
        public Vector3 Now3D = new();
        public Vector3[] PrevPos3D = new Vector3[6];
        public float Score3D;
        // Animatorから関節ごとのTransformの参照を取っている
        public Transform Transform = null;
        // 3つのパラメータで感s熱を表現
        public Quaternion InitRotation;
        public Quaternion Inverse;
        public Quaternion InverseRotation;
        // 自分の親関節と子関節
        public JointPoint Parent = null;
        public JointPoint Child = null;
        // カルマンフィルタ用のパラメータ
        public Vector3 P = new();
        public Vector3 X = new();
        public Vector3 K = new();
    }


    class Skeleton
    {
        public GameObject LineObject;
        public LineRenderer Line;
        // ボーンの開始関節と末端関節
        public JointPoint start = null;
        public JointPoint end = null;
    }


    // 関節を表現するモデル
    public JointPoint[] JointPoints { get; private set; }
    
    // 骨を表現するモデル
    List<Skeleton> Skeletons = new();
    [SerializeField] Material SkeletonMT;
    [SerializeField] bool ShowSkeleton = true;
    [SerializeField] float SkeletonX = -1;
    [SerializeField] float SkeletonY = 1f;
    [SerializeField] float SkeletonZ = -0.5f;
    [SerializeField] float SkeletonScale = 0.005f;

    // アバターのセンターの初期位置
    Vector3 InitialPos;

    // z方向の移動に関するやつら
    [SerializeField] float ZScale = 0.8f;
    float centerTall = 224 * 0.75f;
    float tall = 224 * 0.75f;
    float prevTall = 224 * 0.75f;



    // 全関節初期化
    public JointPoint[] Init()
    {
        JointPoints = new JointPoint[typeof(PositionIndex).Count()];
        for (var i = 0; i < typeof(PositionIndex).Count(); i++) JointPoints[i] = new JointPoint();

        // Animatorコンポから各ボーンのTransformの参照を取得し各jointPointのTransformプロパティと繋ぐ
        Animator anim = GetComponent<Animator>();
        // 右腕
        JointPoints[PositionIndex.rShldrBend.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        JointPoints[PositionIndex.rForearmBend.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        JointPoints[PositionIndex.rHand.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightHand);
        JointPoints[PositionIndex.rThumb2.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightThumbIntermediate);
        JointPoints[PositionIndex.rMid1.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        // 左腕
        JointPoints[PositionIndex.lShldrBend.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        JointPoints[PositionIndex.lForearmBend.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        JointPoints[PositionIndex.lHand.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        JointPoints[PositionIndex.lThumb2.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate);
        JointPoints[PositionIndex.lMid1.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        // 顔
        JointPoints[PositionIndex.lEar.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.Head);
        JointPoints[PositionIndex.lEye.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftEye);
        JointPoints[PositionIndex.rEar.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.Head);
        JointPoints[PositionIndex.rEye.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightEye);
        // 鼻を自動取得
        JointPoints[PositionIndex.Nose.Int()].Transform = GetComponentInChildren<VNectNose>().transform;
        // 右脚
        JointPoints[PositionIndex.rThighBend.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        JointPoints[PositionIndex.rShin.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        JointPoints[PositionIndex.rFoot.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        JointPoints[PositionIndex.rToe.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.RightToes);
        // 左脚
        JointPoints[PositionIndex.lThighBend.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        JointPoints[PositionIndex.lShin.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        JointPoints[PositionIndex.lFoot.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        JointPoints[PositionIndex.lToe.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.LeftToes);
        // 他
        JointPoints[PositionIndex.abdomenUpper.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.Spine);
        JointPoints[PositionIndex.hip.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.Hips);
        JointPoints[PositionIndex.head.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.Head);
        JointPoints[PositionIndex.neck.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.Neck);
        JointPoints[PositionIndex.spine.Int()].Transform = anim.GetBoneTransform(HumanBodyBones.Spine);

        // 各関節の親と子を設定。必要な関節に対してだけ
        // 右腕
        JointPoints[PositionIndex.rShldrBend.Int()].Child = JointPoints[PositionIndex.rForearmBend.Int()];
        JointPoints[PositionIndex.rForearmBend.Int()].Child = JointPoints[PositionIndex.rHand.Int()];
        JointPoints[PositionIndex.rForearmBend.Int()].Parent = JointPoints[PositionIndex.rShldrBend.Int()];
        // 左腕
        JointPoints[PositionIndex.lShldrBend.Int()].Child = JointPoints[PositionIndex.lForearmBend.Int()];
        JointPoints[PositionIndex.lForearmBend.Int()].Child = JointPoints[PositionIndex.lHand.Int()];
        JointPoints[PositionIndex.lForearmBend.Int()].Parent = JointPoints[PositionIndex.lShldrBend.Int()];
        // 顔
        // 右脚
        JointPoints[PositionIndex.rThighBend.Int()].Child = JointPoints[PositionIndex.rShin.Int()];
        JointPoints[PositionIndex.rShin.Int()].Child = JointPoints[PositionIndex.rFoot.Int()];
        JointPoints[PositionIndex.rFoot.Int()].Child = JointPoints[PositionIndex.rToe.Int()];
        JointPoints[PositionIndex.rFoot.Int()].Parent = JointPoints[PositionIndex.rShin.Int()];
        // 左脚
        JointPoints[PositionIndex.lThighBend.Int()].Child = JointPoints[PositionIndex.lShin.Int()];
        JointPoints[PositionIndex.lShin.Int()].Child = JointPoints[PositionIndex.lFoot.Int()];
        JointPoints[PositionIndex.lFoot.Int()].Child = JointPoints[PositionIndex.lToe.Int()];
        JointPoints[PositionIndex.lFoot.Int()].Parent = JointPoints[PositionIndex.lShin.Int()];
        // 他
        JointPoints[PositionIndex.spine.Int()].Child = JointPoints[PositionIndex.neck.Int()];
        JointPoints[PositionIndex.neck.Int()].Child = JointPoints[PositionIndex.head.Int()];
        //jointPoints[PositionIndex.head.Int()].Child = jointPoints[PositionIndex.Nose.Int()];


        // スケルトン設定
        if (ShowSkeleton)
        {
            // 右腕
            AddSkeleton(PositionIndex.rShldrBend, PositionIndex.rForearmBend);
            AddSkeleton(PositionIndex.rForearmBend, PositionIndex.rHand);
            AddSkeleton(PositionIndex.rHand, PositionIndex.rThumb2);
            AddSkeleton(PositionIndex.rHand, PositionIndex.rMid1);
            // 左腕
            AddSkeleton(PositionIndex.lShldrBend, PositionIndex.lForearmBend);
            AddSkeleton(PositionIndex.lForearmBend, PositionIndex.lHand);
            AddSkeleton(PositionIndex.lHand, PositionIndex.lThumb2);
            AddSkeleton(PositionIndex.lHand, PositionIndex.lMid1);
            // 顔
            AddSkeleton(PositionIndex.lEar, PositionIndex.Nose);
            AddSkeleton(PositionIndex.rEar, PositionIndex.Nose);
            // 右脚
            AddSkeleton(PositionIndex.rThighBend, PositionIndex.rShin);
            AddSkeleton(PositionIndex.rShin, PositionIndex.rFoot);
            AddSkeleton(PositionIndex.rFoot, PositionIndex.rToe);
            // 左脚
            AddSkeleton(PositionIndex.lThighBend, PositionIndex.lShin);
            AddSkeleton(PositionIndex.lShin, PositionIndex.lFoot);
            AddSkeleton(PositionIndex.lFoot, PositionIndex.lToe);
            // 他
            AddSkeleton(PositionIndex.spine, PositionIndex.neck);
            AddSkeleton(PositionIndex.neck, PositionIndex.head);
            AddSkeleton(PositionIndex.head, PositionIndex.Nose);
            AddSkeleton(PositionIndex.neck, PositionIndex.rShldrBend);
            AddSkeleton(PositionIndex.neck, PositionIndex.lShldrBend);
            AddSkeleton(PositionIndex.rThighBend, PositionIndex.rShldrBend);
            AddSkeleton(PositionIndex.lThighBend, PositionIndex.lShldrBend);
            AddSkeleton(PositionIndex.rShldrBend, PositionIndex.abdomenUpper);
            AddSkeleton(PositionIndex.lShldrBend, PositionIndex.abdomenUpper);
            AddSkeleton(PositionIndex.rThighBend, PositionIndex.abdomenUpper);
            AddSkeleton(PositionIndex.lThighBend, PositionIndex.abdomenUpper);
            AddSkeleton(PositionIndex.lThighBend, PositionIndex.rThighBend);
        }

        //----------------------
        // Inverse を仕込む
        //----------------------
        // アバターの前方向を得る
        // [尻] [右もも] [左もも] のベクトル外積 (3点からなる平面の法線) を反時計回りに取る
        var forward = TriangleNormal(
            JointPoints[PositionIndex.hip.Int()].Transform.position, 
            JointPoints[PositionIndex.lThighBend.Int()].Transform.position, 
            JointPoints[PositionIndex.rThighBend.Int()].Transform.position);

        foreach (var jointPoint in JointPoints)
        {
            if (jointPoint.Transform != null)
            {
                // Tポーズ時点でのボーン回転を保存
                jointPoint.InitRotation = jointPoint.Transform.rotation;
            }
            if (jointPoint.Child != null)
            {
                // (親→子)ベクトルの逆回転を Inverse に格納
                jointPoint.Inverse = GetInverse(jointPoint, jointPoint.Child, forward);
                jointPoint.InverseRotation = jointPoint.Inverse * jointPoint.InitRotation;
            }
        }
        var hip = JointPoints[PositionIndex.hip.Int()];
        InitialPos = JointPoints[PositionIndex.hip.Int()].Transform.position;
        hip.Inverse = Quaternion.Inverse(Quaternion.LookRotation(forward));
        hip.InverseRotation = hip.Inverse * hip.InitRotation;

        // For Head Rotation
        var head = JointPoints[PositionIndex.head.Int()];
        head.InitRotation = JointPoints[PositionIndex.head.Int()].Transform.rotation;
        var gaze = JointPoints[PositionIndex.Nose.Int()].Transform.position - JointPoints[PositionIndex.head.Int()].Transform.position;
        head.Inverse = Quaternion.Inverse(Quaternion.LookRotation(gaze));
        head.InverseRotation = head.Inverse * head.InitRotation;
        
        var lHand = JointPoints[PositionIndex.lHand.Int()];
        var lf = TriangleNormal(lHand.Pos3D, JointPoints[PositionIndex.lMid1.Int()].Pos3D, JointPoints[PositionIndex.lThumb2.Int()].Pos3D);
        lHand.InitRotation = lHand.Transform.rotation;
        lHand.Inverse = Quaternion.Inverse(Quaternion.LookRotation(JointPoints[PositionIndex.lThumb2.Int()].Transform.position - JointPoints[PositionIndex.lMid1.Int()].Transform.position, lf));
        lHand.InverseRotation = lHand.Inverse * lHand.InitRotation;

        var rHand = JointPoints[PositionIndex.rHand.Int()];
        var rf = TriangleNormal(rHand.Pos3D, JointPoints[PositionIndex.rThumb2.Int()].Pos3D, JointPoints[PositionIndex.rMid1.Int()].Pos3D);
        rHand.InitRotation = JointPoints[PositionIndex.rHand.Int()].Transform.rotation;
        rHand.Inverse = Quaternion.Inverse(Quaternion.LookRotation(JointPoints[PositionIndex.rThumb2.Int()].Transform.position - JointPoints[PositionIndex.rMid1.Int()].Transform.position, rf));
        rHand.InverseRotation = rHand.Inverse * rHand.InitRotation;

        // 追加関節は推論スコアを出せないので1で固定
        JointPoints[PositionIndex.hip.Int()].Score3D = 1f;
        JointPoints[PositionIndex.neck.Int()].Score3D = 1f;
        JointPoints[PositionIndex.Nose.Int()].Score3D = 1f;
        JointPoints[PositionIndex.head.Int()].Score3D = 1f;
        JointPoints[PositionIndex.spine.Int()].Score3D = 1f;

        return JointPoints;
    }


    public void PoseUpdate()
    {
        if (JointPoints == null)
        {
            Debug.LogAssertion("クラスVnectModelの初期化がまだなのでUpdateを開始できない。先にInit()を呼べ");
            return;
        }

        // 各部位の高さからz座標の可動範囲を計算
        var t1 = Vector3.Distance(JointPoints[PositionIndex.head.Int()].Pos3D, JointPoints[PositionIndex.neck.Int()].Pos3D);
        var t2 = Vector3.Distance(JointPoints[PositionIndex.neck.Int()].Pos3D, JointPoints[PositionIndex.spine.Int()].Pos3D);
        var pm = (JointPoints[PositionIndex.rThighBend.Int()].Pos3D + JointPoints[PositionIndex.lThighBend.Int()].Pos3D) / 2f;
        var t3 = Vector3.Distance(JointPoints[PositionIndex.spine.Int()].Pos3D, pm);
        var t4r = Vector3.Distance(JointPoints[PositionIndex.rThighBend.Int()].Pos3D, JointPoints[PositionIndex.rShin.Int()].Pos3D);
        var t4l = Vector3.Distance(JointPoints[PositionIndex.lThighBend.Int()].Pos3D, JointPoints[PositionIndex.lShin.Int()].Pos3D);
        var t4 = (t4r + t4l) / 2f;
        var t5r = Vector3.Distance(JointPoints[PositionIndex.rShin.Int()].Pos3D, JointPoints[PositionIndex.rFoot.Int()].Pos3D);
        var t5l = Vector3.Distance(JointPoints[PositionIndex.lShin.Int()].Pos3D, JointPoints[PositionIndex.lFoot.Int()].Pos3D);
        var t5 = (t5r + t5l) / 2f;
        var t = t1 + t2 + t3 + t4 + t5;

        // z方向のローパスフィルタ
        tall = t * 0.7f + prevTall * 0.3f;
        prevTall = tall;
        if (tall == 0) tall = centerTall;
        var dz = (centerTall - tall) / centerTall * ZScale;

        // 中心の移動と回転
        var forward = TriangleNormal(JointPoints[PositionIndex.hip.Int()].Pos3D, JointPoints[PositionIndex.lThighBend.Int()].Pos3D, JointPoints[PositionIndex.rThighBend.Int()].Pos3D);
        JointPoints[PositionIndex.hip.Int()].Transform.position = JointPoints[PositionIndex.hip.Int()].Pos3D * 0.005f + new Vector3(InitialPos.x, InitialPos.y, InitialPos.z + dz);
        JointPoints[PositionIndex.hip.Int()].Transform.rotation = Quaternion.LookRotation(forward) * JointPoints[PositionIndex.hip.Int()].InverseRotation;

        // 全関節の回転
        foreach (var jointPoint in JointPoints)
        {
            if (jointPoint.Parent != null)
            {
                var fv = jointPoint.Parent.Pos3D - jointPoint.Pos3D;
                jointPoint.Transform.rotation = Quaternion.LookRotation(jointPoint.Pos3D - jointPoint.Child.Pos3D, fv) * jointPoint.InverseRotation;
            }
            else 
            if (jointPoint.Child != null)
            {
                jointPoint.Transform.rotation = Quaternion.LookRotation(jointPoint.Pos3D - jointPoint.Child.Pos3D, forward) * jointPoint.InverseRotation;
            }
        }

        // 頭の回転
        var gaze = JointPoints[PositionIndex.Nose.Int()].Pos3D - JointPoints[PositionIndex.head.Int()].Pos3D;
        var f = TriangleNormal(JointPoints[PositionIndex.Nose.Int()].Pos3D, JointPoints[PositionIndex.rEar.Int()].Pos3D, JointPoints[PositionIndex.lEar.Int()].Pos3D);
        var head = JointPoints[PositionIndex.head.Int()];
        head.Transform.rotation = Quaternion.LookRotation(gaze, f) * head.InverseRotation;
        
        // 手首の回転 (テストコードらしい)
        var lHand = JointPoints[PositionIndex.lHand.Int()];
        var lf = TriangleNormal(lHand.Pos3D, JointPoints[PositionIndex.lMid1.Int()].Pos3D, JointPoints[PositionIndex.lThumb2.Int()].Pos3D);
        lHand.Transform.rotation = Quaternion.LookRotation(JointPoints[PositionIndex.lThumb2.Int()].Pos3D - JointPoints[PositionIndex.lMid1.Int()].Pos3D, lf) * lHand.InverseRotation;

        var rHand = JointPoints[PositionIndex.rHand.Int()];
        var rf = TriangleNormal(rHand.Pos3D, JointPoints[PositionIndex.rThumb2.Int()].Pos3D, JointPoints[PositionIndex.rMid1.Int()].Pos3D);
        rHand.Transform.rotation = Quaternion.LookRotation(JointPoints[PositionIndex.rThumb2.Int()].Pos3D - JointPoints[PositionIndex.rMid1.Int()].Pos3D, rf) * rHand.InverseRotation;

        // スケルトンを動かす
        foreach (var sk in Skeletons)
        {
            var s = sk.start;
            var e = sk.end;

            sk.Line.SetPosition(0, new Vector3(s.Pos3D.x * SkeletonScale + SkeletonX, s.Pos3D.y * SkeletonScale + SkeletonY, s.Pos3D.z * SkeletonScale + SkeletonZ));
            sk.Line.SetPosition(1, new Vector3(e.Pos3D.x * SkeletonScale + SkeletonX, e.Pos3D.y * SkeletonScale + SkeletonY, e.Pos3D.z * SkeletonScale + SkeletonZ));
        }
    }


    Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 d1 = a - b;
        Vector3 d2 = a - c;
        Vector3 dd = Vector3.Cross(d1, d2);
        dd.Normalize();
        return dd;
    }



    // Tポーズ方向をワールド基準に合わせる補正 quaternion　を計算
    // 親‑子ベクトルを forward として LookRotation を作成し更にその逆回転を取得
    private Quaternion GetInverse(JointPoint parent, JointPoint child, Vector3 up)
    {
        // LookRotation(forward, up) → 「親→子 が Z+ を向き，上向きが upDir」の回転
        var look = Quaternion.LookRotation(parent.Transform.position - child.Transform.position, up);
        // 逆回転を掛ければ「Tポーズの姿勢」をローカル空間の原点に戻せる
        return Quaternion.Inverse(look);
    }


    //　渡された(骨の)開始関節インデックスと末端関節インデックスからその部位のスケルトン描画を作成
    private void AddSkeleton(PositionIndex s, PositionIndex e)
    {
        var sk = new Skeleton()
        {
            LineObject = new GameObject("Line"),
            start = JointPoints[s.Int()],
            end = JointPoints[e.Int()],
        };

        sk.Line = sk.LineObject.AddComponent<LineRenderer>();
        sk.Line.startWidth = 0.04f;
        sk.Line.endWidth = 0.01f;
        
        // define the number of vertex
        sk.Line.positionCount = 2;
        sk.Line.material = SkeletonMT;

        Skeletons.Add(sk);
    }
}
