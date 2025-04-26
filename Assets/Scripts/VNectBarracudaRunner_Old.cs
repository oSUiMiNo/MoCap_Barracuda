using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using TMPro;

/*
���� �p���` ����
�����f���FVNectModel
���A�o�^�[�F�l�^�RD���f��
��NN�F�j���[�����l�b�g���[�N, ONNX
*/


// �֐ߏ��𐄒肷��A���S���Y��
public class VNectBarracudaRunner_Old : MonoBehaviour
{
    // ONNX���f�����C���|�[�g���ďo����Unity�A�Z�b�g��NN���f��
    [SerializeField] NNModel NNModel;
    // ���[�h����NN���f��������
    Model model;
    // Barracuda �̃��[�J�[
    IWorker worker;
    [SerializeField] WorkerFactory.Type WorkerType = WorkerFactory.Type.Auto;
    // Verbose �� true �ɂ���ƃG���[�����f�o�b�O�����₷���炵��
    [SerializeField] bool Verbose = false;
    // �֐ߏ��̎��샂�f��
    [SerializeField] VNectModel vNectModel;
    // ���旬���R���|�[�l���g
    [SerializeField] VideoCapture videoCapture;
    // �L�����u���[�V�����p�摜 �S�g���f����������₷���f�����]�܂���
    [SerializeField] Texture2D InitImg;
    // �L�����u���[�V�����p�̓��͉摜�e���\��
    Tensor InitialInput {
        get {
            // �摜���ݒ肳��Ă����ꍇ�͂��̃e�N�X�`��������L���v�`���Ɏg����
            if (InitImg) return new Tensor(InitImg);
            // �摜�������ꍇ�͂����Ȃ���͓���̃e�N�X�`�����g��
            else return VideoInput;
        }
    }
    // ���悩���������͉摜�e���\��
    Tensor VideoInput => new Tensor(videoCapture.InputTexture);
    // ���b�Z�[�W�\���p�e�L�X�g
    TextMeshProUGUI Msg => GameObject.Find("Msg").GetComponent<TextMeshProUGUI>();


    // �H�킹����͉摜�e���\����3�t���[�����B���O�͂�������Ȃ��Ƃ����Ȃ����ۂ��BNN���f���̓s���H
    const string inputName_1 = "input.1";
    const string inputName_2 = "input.4";
    const string inputName_3 = "input.7";
    Dictionary<string, Tensor> inputs = new Dictionary<string, Tensor>() {
        { inputName_1, null },
        { inputName_2, null },
        { inputName_3, null },
    };


    // �֐߂̂����ȏ�񂪓������f�[�^�N���X�̑S�֐ߕ�
    VNectModel.JointPoint[] jointPoints;
    // �֐ߐ�
    const int JointNum = 24;
    int JointNum_Squared => JointNum * 2;
    int JointNum_Cube => JointNum * 3;


    // ����e�N�X�`����؂蔲���ă��[�L���v�Ɏg���T�C�Y
    // �l�b�g���[�N���󂯎��U�C�Y��448*448�ō���Ă�̂�448
    [SerializeField] int InputImgSize = 448;
    float InputImgSizeHalf => InputImgSize / 2f;
    // �q�[�g�}�b�v�J�������ɑ΂���C���v�b�g�C���[�W�T�C�Y�̊���
    // 1�J�����ӂ�Ȃ�s�N�Z����S�����Ƃ��Ȃ̂��H
    float ImgScale => InputImgSize / (float)HeatMapCol;            // 224f / (float)InputImageSize;
    
    
    // �q�[�g�}�b�v�J���� �����炭�֐߂̌��ƈ�v
    const int HeatMapCol = 28;                          // ���l�ς�����A�o�^�[�̋�������΂��Ȃ���
    // �q�[�g�}�b�v�J����2��
    int HeatMapCol_Squared => HeatMapCol * HeatMapCol;             //�摜�A����̃q�[�g�}�b�v�J����
    // �q�[�g�}�b�v�J����3��
    int HeatMapCol_Cube => HeatMapCol * HeatMapCol * HeatMapCol;   // �A�o�^�[�̃q�[�g�}�b�v�J����
    // �q�[�g�}�b�v�J���� * �֐߂̌�
    int HeatMapCol_JointNum => HeatMapCol * JointNum;
    // �q�[�g�}�b�v�J���� * ( �֐߂̌� * 2 )
    int CubeOffsetLinear => HeatMapCol * JointNum_Cube;
    // �q�[�g�}�b�v�J������2�� * ( �֐߂̌� * 2 )
    int CubeOffsetSquared => HeatMapCol_Squared * JointNum_Cube;

    //// �o�b�t�@ ( �֐߂̌� * �q�[�g�}�b�v�J������3�� ) �̗v�f������float�z��
    //// [Joint��] x 28 x 28 x 28 �ɑ�������4�����e���\��������i���ۂɂ�1�����z��Ɋi�[���Ă��邪�Y���v�Z��4���������ɂȂ��Ă���)
    //// �e�{�N�Z���Ɂu�֐� i �������ɑ��݂���m�� (�X�R�A)�v���i�[����Ă���C���[�W
    //// ������ (x, y, z) ��S�T�����A��ԃX�R�A�������{�N�Z�����u�֐� i �̐���ʒu�v�Ƃ��Č��o
    //// if (v > jointPoints[i].Score3D) {...} �̂悤�ɍő�l��T���Ă���
    //float[] heatMap3D; // VNect�Ɏg��
    //// �o�b�t�@ ( �֐߂̌� * �q�[�g�}�b�v�J������3�� * 3 ) �̗v�f������float�z��
    //float[] offset3D;
    ///*�y�I�t�Z�b�g�z
    //�v���O���~���O�J���̒��ŃI�t�Z�b�g�Ƃ́A�ʂ̏ꏊ�Ɣ�r�f�[�^�̂Q�_�Ԃ̋����ł��B
    //�I�t�Z�b�g�͂Q�̃������̈ʒu�̊Ԃ̋�����\�����߂ɂ����܂��B*/

    
    // �t�B���^�p�p�����[�^
    [SerializeField] float KalmanParamQ = 0.001f;
    [SerializeField] float KalmanParamR = 0.0015f;
    [SerializeField] float LowPassParam = 0.1f;
    // ���[�p�X�t�B���^���g�����ǂ���
    [SerializeField] bool UseLowPassFilter = true;


    // �����������t���O
    bool Initialized = false;


    IEnumerator Start()
    {
        // VNectModel ������
        jointPoints = vNectModel.Init();
        // ��
        heatMap3D = new float[JointNum * HeatMapCol_Cube];
        offset3D = new float[JointNum * HeatMapCol_Cube * 3];
        //heatMap4D = new float[JointNum, HeatMapCol, HeatMapCol, HeatMapCol];
        //offset5D = new float[JointNum, HeatMapCol, HeatMapCol, HeatMapCol, 3];
        // NN���f�����[�h
        model = ModelLoader.Load(NNModel, Verbose);
        // ���[�h����NN���f�����烏�[�J�[�쐬
        worker = WorkerFactory.CreateWorker(WorkerType, model, Verbose);
        // VideoCapture ������
        videoCapture.Play(InputImgSize, InputImgSize);
        // ����L���v�`��(�L�����u���[�V����)
        yield return StartCoroutine(Exe());
        // UI�̕������N���A
        Msg.text = "";
        // �����������t���O
        Initialized = true;
        // �[���̃X���[�v�֎~
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }


    void Update()
    {
        // �X�V
        if (Initialized) StartCoroutine(Exe());
    }


    // ���s
    IEnumerator Exe()
    {
        // NN�ւ̓��̓f�[�^���X�V
        if (!Initialized) // ����
        {
            inputs[inputName_1] = InitialInput;
            inputs[inputName_2] = InitialInput;
            inputs[inputName_3] = InitialInput;
        }
        else             // ����ȍ~
        {
            inputs[inputName_3].Dispose();
            inputs[inputName_3] = inputs[inputName_2];
            inputs[inputName_2] = inputs[inputName_1];
            inputs[inputName_1] = VideoInput;
        }
        // ���_
        yield return StartCoroutine(Predict(inputs));
        // ���_���ʂ����f���ɓK�p
        Apply();

        // �X�V���ꂽ���f�����A�o�^�[�ɓK�p
        vNectModel.PoseUpdate();
    }



   
    float[] heatMap3D;
    float[] offset3D;
    // ���_
    IEnumerator Predict(Dictionary<string, Tensor> inputs)
    {
        // ���O�F���̓e���\���̌`��
        //foreach (var input in inputs) Debug.Log($"Input {input.Key}: Shape = {input.Value.shape}")
        // ���_
        yield return worker.StartManualSchedule(inputs);
        // ���_���ʂ�ۑ�����o�b�t�@
        Tensor[] b_outputs = new Tensor[4];
        // ���_���ʎ擾
        for (var i = 2; i < model.outputs.Count; i++) b_outputs[i] = worker.PeekOutput(model.outputs[i]);
        // �g���鐄�_���ʂ𔲂����
        offset3D = b_outputs[2].data.Download(b_outputs[2].shape);
        heatMap3D = b_outputs[3].data.Download(b_outputs[3].shape);
        // �������J��
        for (var i = 2; i < b_outputs.Length; i++) b_outputs[i].Dispose();
    }


    //NN�̐��_���ʂ��瓾���X�R�A�Ɗ֐߈ʒu���e�֐߂ɓK�ps
    void Apply()
    {
        // �e�f�t�H���g�֐߂ɂ���
        for (var i = 0; i < JointNum; i++)
        {
            //----------------------------------
            // �e�f�t�H���g�֐߂̑��݊m����
            // ��ԍ����{�N�Z���Ƃ��̊m�������
            //----------------------------------
            // �e�֐߂̐��_�X�R�A(�֐߂̑��݊m������ԍ����{�N�Z���̊m��)
            jointPoints[i].Score3D = 0.0f;
            // �ō��X�R�A���o���{�N�Z���̃C���f�b�N�X
            var maxXIndex = 0;
            var maxYIndex = 0;
            var maxZIndex = 0;

            // heatMap3D�͊T�O�I�ɂ͎��̂悤�� 4 �����\���������Ă��� heatMap3D[joint i][z][y][x] ������1�����z�� �ɕ��ׂĂ���
            // i �� 1 �����邲�Ƃ� �gHeatMapCol ���h �z�񂪐�ɐi��
            // �֐� i ���Ƃ̐擪�C���f�b�N�X�����߂�C���[�W
            var jj = i * HeatMapCol;
            for (var z = 0; z < HeatMapCol; z++)
            {
                // ��قǂ� i *HeatMapCol �ɑ΂��� z �����Z
                // �܂� �u�֐� i �̒��ŁAz �� 1 ���₷�Ɣz��1�i�ށv 
                // �����܂ł� �g(i, z) �̑g�ݍ��킹�h �� 1�����փ}�b�s���O
                var zz = jj + z;
                for (var y = 0; y < HeatMapCol; y++)
                {
                    var yy = y * HeatMapCol_Squared * JointNum + zz;�@// ��
                    for (var x = 0; x < HeatMapCol; x++)
                    {
                        // �q�[�g�}�b�v���Ŋ֐߈ʒu�̉ӏ��̃X�R�A���擾
                        float v = heatMap3D[yy + x * HeatMapCol_JointNum];
                        // �X�R�A��臒l(����͏����l��0.0)���������ꍇ�͍̗p (�֐߃f�[�^�ɓK�p)
                        // �S�T�����čő�X�R�A�̃C���f�b�N�X��T�����߂ɁAScore3D�����傫����΍X�V
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
            // �e�f�t�H���g�֐߂̈ʒu�v�Z���ă��f���ɓK�p
            //----------------------------------
            //  �֐�[i]��X���W
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

            // �֐�[i]��Y���W
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

            //  �֐�[i]��Z���W
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
        // �ǉ��̊֐߈ʒu�v�Z
        //----------------------------------
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

        //----------------------------------
        // �t�B���^
        //----------------------------------
        // �J���}���t�B���^
        foreach (var jp in jointPoints) KalmanUpdate(jp);
        // ���[�p�X�t�B���^
        if (UseLowPassFilter)
            foreach (var jp in jointPoints) LowPassFilter(jp);
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
            jp.PrevPos3D[i] = jp.PrevPos3D[i] * LowPassParam + jp.PrevPos3D[i - 1] * (1f - LowPassParam);
        jp.Pos3D = jp.PrevPos3D[jp.PrevPos3D.Length - 1];
    }
}
