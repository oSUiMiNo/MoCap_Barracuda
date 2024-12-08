using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using TMPro;


// �֐ߏ��𐄒肷��A���S���Y��
public class VNectBarracudaRunner : MonoBehaviour
{
    // ONNX���f�����C���|�[�g���ďo����Unity�A�Z�b�g��NN���f��
    public NNModel NNModel;
    // ���[�h����NN���f��������
    private Model _model;
    // Barracuda �̃��[�J�[
    private IWorker _worker;
    public WorkerFactory.Type WorkerType = WorkerFactory.Type.Auto;
    // Verbose �� true �ɂ���ƃG���[�����f�o�b�O�����₷���炵��
    public bool Verbose = false;
    // �֐ߏ��̎��샂�f��
    public VNectModel VNectModel;

    // ���旬���R���|
    public VideoCapture videoCapture;
    // �L�����u���[�V�����p�摜
    public Texture2D InitImg;
    // �L�����u���[�V�����p�̓��͉摜�e���\��
    Tensor InitialInput => new Tensor(InitImg);

    // ���悩���������͉摜�e���\��
    Tensor VideoInput => new Tensor(videoCapture.MainTexture);
    // �e�L�X�g
    public TextMeshProUGUI Msg => GameObject.Find("Msg").GetComponent<TextMeshProUGUI>();

    // �L�����u���[�V���������t���O
    private bool Calibrated = false;

    // �H�킹����͉摜�e���\����3�t���[�����B���O�͂�������Ȃ��Ƃ����Ȃ����ۂ��B���f���̓s���H
    const string inputName_1 = "input.1";
    const string inputName_2 = "input.4";
    const string inputName_3 = "input.7";
    Dictionary<string, Tensor> inputs = new Dictionary<string, Tensor>() {
        { inputName_1, null },
        { inputName_2, null },
        { inputName_3, null },
    };

    // �֐߂̂����ȏ�񂪓������f�[�^�N���X�̑S�֐ߕ�
    private VNectModel.JointPoint[] jointPoints;
    // �֐ߐ�
    private const int JointNum = 24;
    private int JointNum_Squared = JointNum * 2;
    private int JointNum_Cube = JointNum * 3;

    // ����e�N�X�`����؂蔲���ă��[�L���v�Ɏg���T�C�Y
    // �傫�����Ă����������Ă����x�������A
    // ����������̐l�ԂƓ������炢�ɂ���Ƃ����������ۂ� VideoCapture ���ŏ���������炵���B
    public int InputImageSize;
    private float InputImageSizeHalf => InputImageSize / 2f;
    // �q�[�g�}�b�v�J�������ɑ΂���C���v�b�g�C���[�W�T�C�Y�̊���
    // 1�J�����ӂ�Ȃ�s�N�Z����S�����Ƃ��Ȃ̂��H
    private float ImageScale => InputImageSize / (float)HeatMapCol;// 224f / (float)InputImageSize;

    // �q�[�g�}�b�v�J����
    public int HeatMapCol;     //�@���l�ς�����A�o�^�[�̋�������΂��Ȃ���
    // �q�[�g�}�b�v�J����2��
    private int HeatMapCol_Squared => HeatMapCol * HeatMapCol;     //�摜�A����̃q�[�g�}�b�v�J����
    // �q�[�g�}�b�v�J����3��
    private int HeatMapCol_Cube => HeatMapCol * HeatMapCol * HeatMapCol;  // �A�o�^�[�̃q�[�g�}�b�v�J����

    // �o�b�t�@ ( �֐߂̌� * �q�[�g�}�b�v�J������3�� ) �̗v�f������float�z��
    private float[] heatMap3D; // VNect�Ɏg��

    // �o�b�t�@ ( �֐߂̌� * �q�[�g�}�b�v�J������3�� * 3 ) �̗v�f������float�z��
    private float[] offset3D;
    /*�y�I�t�Z�b�g�z
    �v���O���~���O�J���̒��ŃI�t�Z�b�g�Ƃ́A�ʂ̏ꏊ�Ɣ�r�f�[�^�̂Q�_�Ԃ̋����ł��B
    �I�t�Z�b�g�͂Q�̃������̈ʒu�̊Ԃ̋�����\�����߂ɂ����܂��B */


    // �q�[�g�}�b�v�J���� * �֐߂̌�
    private int HeatMapCol_JointNum => HeatMapCol * JointNum;
    // �q�[�g�}�b�v�J���� * ( �֐߂̌�*2 )
    private int CubeOffsetLinear => HeatMapCol * JointNum_Cube;
    // �q�[�g�}�b�v�J������2�� * ( �֐߂̌�*2 )
    private int CubeOffsetSquared => HeatMapCol_Squared * JointNum_Cube;


    // �t�B���^�p�p�����[�^
    public float KalmanParamQ;
    public float KalmanParamR;
    public float LowPassParam;
    // ���[�p�X�t�B���^���g�����ǂ���
    public bool UseLowPassFilter;



    IEnumerator Start()
    {
        heatMap3D = new float[JointNum * HeatMapCol_Cube];
        offset3D = new float[JointNum * HeatMapCol_Cube * 3];

        // �[���̃X���[�v�֎~
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // NN���f�����[�h
        _model = ModelLoader.Load(NNModel, Verbose);
        // ���[�h����NN���f�����烏�[�J�[�쐬
        _worker = WorkerFactory.CreateWorker(WorkerType, _model, Verbose);

        // �L�����u���[�V����
        yield return StartCoroutine(Calibrate());

        // UI�̕������N���A
        Msg.text = "";


    }


    void Update()
    {
        // �X�V
        if (Calibrated) StartCoroutine(UpdateVNectModel());
    }


    IEnumerator Calibrate()
    {
        // �X�V
        yield return StartCoroutine(UpdateVNectModel());

        // VideoCapture ������
        videoCapture.Init(InputImageSize, InputImageSize);

        // �L�����u���[�V���������t���O
        Calibrated = true;
    }


    IEnumerator ExecuteModelAsync(Dictionary<string, Tensor> inputs)
    {
        // ���_
        yield return _worker.StartManualSchedule(inputs);

        // ���_���ʂ�ۑ�����o�b�t�@
        Tensor[] b_outputs = new Tensor[4];

        // ���_���ʎ擾
        for (var i = 2; i < _model.outputs.Count; i++) b_outputs[i] = _worker.PeekOutput(_model.outputs[i]);

        // �g���鐄�_���ʂ𔲂����
        offset3D = b_outputs[2].data.Download(b_outputs[2].shape);
        heatMap3D = b_outputs[3].data.Download(b_outputs[3].shape);

        // �������J��
        for (var i = 2; i < b_outputs.Length; i++) b_outputs[i].Dispose();
    }


    IEnumerator UpdateVNectModel()
    {
        // �C���v�b�g�f�[�^���X�V
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

        // ���O�F���̓e���\���̌`��
        //foreach (var input in inputs) Debug.Log($"Input {input.Key}: Shape = {input.Value.shape}");

        // ���_���s
        yield return StartCoroutine(ExecuteModelAsync(inputs));

        // VNectModel ������
        if (!Calibrated) jointPoints = VNectModel.Init();

        // �p������
        PredictPose();
    }



    //�p������
    void PredictPose()
    {
        // ======== �e�Ԑڂɑ΂��ĉ������Ă� ========
        for (var j = 0; j < JointNum; j++)
        {
            // ����4�p�����[�^�����߂邽�߂̃��[�v���ۂ�
            jointPoints[j].score3D = 0.0f;
            var maxXIndex = 0;
            var maxYIndex = 0;
            var maxZIndex = 0;

            var jj = j * HeatMapCol; // ��
            for (var z = 0; z < HeatMapCol; z++)
            {
                var zz = jj + z;�@// ��
                for (var y = 0; y < HeatMapCol; y++)
                {
                    var yy = y * HeatMapCol_Squared * JointNum + zz;�@// ��
                    for (var x = 0; x < HeatMapCol; x++)
                    {
                        float v = heatMap3D[yy + x * HeatMapCol_JointNum]; // ��
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

        // ========== �ǉ��̊֐߈ʒu���v�Z ==========
        // �K�ʒu = ���� [ ���E�������� , ���㕔 ]
        jointPoints[PositionIndex.hip.Int()].Now3D = ((
                jointPoints[PositionIndex.rThighBend.Int()].Now3D + // �E����
                jointPoints[PositionIndex.lThighBend.Int()].Now3D   // ������
                ) / 2f
                + jointPoints[PositionIndex.abdomenUpper.Int()].Now3D // ���㕔
            ) / 2f;

        // ��ʒu = ���E������
        jointPoints[PositionIndex.neck.Int()].Now3D = (
            jointPoints[PositionIndex.rShldrBend.Int()].Now3D + // ����
            jointPoints[PositionIndex.lShldrBend.Int()].Now3D   // �E��
            ) / 2f;

        // �Ғňʒu = ���㕔
        jointPoints[PositionIndex.spine.Int()].Now3D = jointPoints[PositionIndex.abdomenUpper.Int()].Now3D;

        // (�񂩂�̑���)������ = �m�[�}���C�Y [ ���E������ - �� ]
        var headDir = Vector3.Normalize((
                jointPoints[PositionIndex.rEar.Int()].Now3D + // �E��
                jointPoints[PositionIndex.lEar.Int()].Now3D   // ����
                ) / 2f
                - jointPoints[PositionIndex.neck.Int()].Now3D // ��
            );
        // (�񂩂�̑���)�@�x�N�g�� = [ �@ - �� ]
        var noseVec = 
            jointPoints[PositionIndex.Nose.Int()].Now3D - // �@
            jointPoints[PositionIndex.neck.Int()].Now3D;  // ��
        // (�񂩂�̑���)���ʒu = ������ * ���� [ �@�x�N�g�� , ������ ]
        var localHeadPos = headDir * Vector3.Dot(headDir, noseVec);
        // ���ʒu = �� + (�񂩂�̑���)���ʒu
        jointPoints[PositionIndex.head.Int()].Now3D = jointPoints[PositionIndex.neck.Int()].Now3D + localHeadPos;


        // ================ �t�B���^ ================
        // �J���}���t�B���^
        foreach (var jp in jointPoints)
        {
            KalmanUpdate(jp);
        }
        // ���[�p�X�t�B���^
        if (UseLowPassFilter)
        {
            foreach (var jp in jointPoints)
            {
                LowPassFilter(jp);
            }
        }
    }




    // �J���}���t�B���^
    void KalmanUpdate(VNectModel.JointPoint jp)
    {
        // MeasurementUpdate
        jp.K.x = (jp.P.x + KalmanParamQ) / (jp.P.x + KalmanParamQ + KalmanParamR);
        jp.K.y = (jp.P.y + KalmanParamQ) / (jp.P.y + KalmanParamQ + KalmanParamR);
        jp.K.z = (jp.P.z + KalmanParamQ) / (jp.P.z + KalmanParamQ + KalmanParamR);
        jp.P.x = KalmanParamR * (jp.P.x + KalmanParamQ) / (KalmanParamR + jp.P.x + KalmanParamQ);
        jp.P.y = KalmanParamR * (jp.P.y + KalmanParamQ) / (KalmanParamR + jp.P.y + KalmanParamQ);
        jp.P.z = KalmanParamR * (jp.P.z + KalmanParamQ) / (KalmanParamR + jp.P.z + KalmanParamQ);
        // ���C������
        jp.Pos3D.x = jp.X.x + (jp.Now3D.x - jp.X.x) * jp.K.x;
        jp.Pos3D.y = jp.X.y + (jp.Now3D.y - jp.X.y) * jp.K.y;
        jp.Pos3D.z = jp.X.z + (jp.Now3D.z - jp.X.z) * jp.K.z;
        jp.X = jp.Pos3D;
    }

    // ���[�p�X�t�B���^
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
