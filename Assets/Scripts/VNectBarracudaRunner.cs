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
public class VNectBarracudaRunner : MonoBehaviour
{
    // �֐ߏ��̎��샂�f��
    [SerializeField] VNectModel VNect;
    // ONNX���f�����C���|�[�g���ďo����Unity�A�Z�b�g��NN���f��
    [SerializeField] NNModel NN;
    // ���[�h����NN���f��������
    Model model;
    // Barracuda �̃��[�J�[
    IWorker worker;
    [SerializeField] WorkerFactory.Type WorkerType = WorkerFactory.Type.Auto;


    // ���旬���R���|�[�l���g
    [SerializeField] VideoCapture Video;
    // ���悩���������͉摜�e���\��
    Tensor VideoInput => new Tensor(Video.InputTexture);

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
    

    // �H�킹����͉摜�e���\����3�t���[�����B���O�͂�������Ȃ��Ƃ����Ȃ����ۂ��BNN���f���̓s���H
    const string inputName1 = "input.1";
    const string inputName2 = "input.4";
    const string inputName3 = "input.7";
    Dictionary<string, Tensor> Inputs = new Dictionary<string, Tensor>() {
        { inputName1, null },
        { inputName2, null },
        { inputName3, null },
    };


    // �֐߂̂����ȏ�񂪓������f�[�^�N���X�̑S�֐ߕ�
    VNectModel.JointPoint[] JointPoints;
    // �֐ߐ�
    const int JointNum = 24;


    // �q�[�g�}�b�v�̂P�ӂ̖ڐ���
    // ���f���̓s����28�� �����炭�֐߂̌��ƈ�v
    const int HeatMapCol = 28;
    // �e�{�N�Z���Ɋ֐߂̑��݊m����������3�����̃q�[�g�}�b�v
    // [Joint��] x 28 x 28 x 28 �ɑ�������4�����e���\��������i���ۂɂ�1�����z��Ɋi�[���Ă��邪�Y���v�Z��4���������ɂȂ��Ă���)
    // �e�{�N�Z���Ɂu�֐� i �������ɑ��݂���m�� (�X�R�A)�v���i�[����Ă���C���[�W
    // ������ (x, y, z) ��S�T�����A��ԃX�R�A�������{�N�Z�����u�֐� i �̐���ʒu�v�Ƃ��Č��o
    // if (v > jointPoints[i].Score3D) {...} �̂悤�ɍő�l��T���Ă���
    float[] HeatMap = new float[JointNum * HeatMapCol * HeatMapCol * HeatMapCol];
    // �q�[�g�}�b�v�̃{�N�Z���̖ڂ��r���̂ł��������ׂ����֐߈ʒu�����߂邽�߂̃I�t�Z�b�g
    float[] Offset = new float[JointNum * HeatMapCol * HeatMapCol * HeatMapCol * 3];


    // ����e�N�X�`����؂蔲���ă��[�L���v�Ɏg���T�C�Y
    // �l�b�g���[�N���󂯎��U�C�Y��448*448�ō���Ă�̂�448
    [SerializeField] int InputImgSize = 448;
    float InputImgSizeHalf => InputImgSize / 2f;
    // �q�[�g�}�b�v�J�������ɑ΂���C���v�b�g�C���[�W�T�C�Y�̊���
    // 1�J�����ӂ�Ȃ�s�N�Z����S�����Ƃ��Ȃ̂��H
    float ImgScale => InputImgSize / (float)HeatMapCol;


    // �t�B���^�p�p�����[�^
    [SerializeField] float KalmanParamQ = 0.001f;
    [SerializeField] float KalmanParamR = 0.0015f;
    [SerializeField] float LowPassParam = 0.1f;
    // ���[�p�X�t�B���^���g�����ǂ���
    [SerializeField] bool UseLowPassFilter = true;


    // �����������t���O
    bool Initialized = false;
    // ���b�Z�[�W�\���p�e�L�X�g
    TextMeshProUGUI Msg => GameObject.Find("Msg").GetComponent<TextMeshProUGUI>();




    IEnumerator Start()
    {
        // VNectModel ������
        JointPoints = VNect.Init();
        // NN���f�����[�h
        model = ModelLoader.Load(NN);
        // ���[�h����NN���f�����烏�[�J�[�쐬
        worker = WorkerFactory.CreateWorker(WorkerType, model);
        // VideoCapture ������
        Video.Play(InputImgSize, InputImgSize);
        
        
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
        if (Initialized) StartCoroutine(Exe());
    }


    //==============================================================
    // �e�L���v�`���t���[���ł̏���
    //==============================================================
    IEnumerator Exe()
    {
        // NN�ւ̓��̓f�[�^���X�V
        if (!Initialized) // ����
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
        
        // ���_
        yield return StartCoroutine(Predict(Inputs));
        
        // ���_���ʂ����f���ɓK�p
        ApplyDefaultJoint();
        ApplyAdditionalJoint();
        
        // �J���}���t�B���^
        foreach (var jp in JointPoints) KalmanFilter(jp);
        
        // ���[�p�X�t�B���^
        if (UseLowPassFilter)
        foreach (var jp in JointPoints) LowPassFilter(jp);

        // �X�V���ꂽ���f�����A�o�^�[�ɓK�p
        VNect.PoseUpdate();
    }


    //==============================================================
    // ���_
    //==============================================================
    IEnumerator Predict(Dictionary<string, Tensor> inputs)
    {
        // ���O�F���̓e���\���̌`��
        //foreach (var input in inputs) Debug.Log($"Input {input.Key}: Shape = {input.Value.shape}")
        // ���_
        yield return worker.StartManualSchedule(inputs);
        // ���_���ʎ擾
        Tensor[] b_outputs = new Tensor[4];
        for (var i = 2; i < model.outputs.Count; i++) b_outputs[i] = worker.PeekOutput(model.outputs[i]);
        // �g����f�[�^�𒊏o
        Offset = b_outputs[2].data.Download(b_outputs[2].shape);
        HeatMap = b_outputs[3].data.Download(b_outputs[3].shape);
    }


    //==============================================================
    // NN�̐��_���ʂ��瓾���X�R�A�Ɗ֐߈ʒu�����f���ɓK�p
    //==============================================================
    void ApplyDefaultJoint()
    {
        // �X�R�A�q�[�g�}�b�v�̓Y���v�Z
        int HeatMapIndex(int i, int z, int y, int x)
        {
            // (i,z,y,x) �� heatMap3D[...] ��1����index
            return i * HeatMapCol
                 + x * (HeatMapCol * JointNum)
                 + y * (HeatMapCol * HeatMapCol * JointNum)
                 + z;
        }
        // �I�t�Z�b�g�Y���v�Z:
        int OffsetIndex(int i, int x, int y, int z, string component)
        {
            // offset3D[ x * CubeOffsetLinear + y * CubeOffsetSquared + z + HeatMapCol * (i + ???)]
            int iOffset = i;
            if (component == "x") iOffset = i;                // X�I�t�Z�b�g
            else
            if (component == "y") iOffset = i + JointNum;     // Y�I�t�Z�b�g
            else
            if (component == "z") iOffset = i + JointNum * 2; // Z�I�t�Z�b�g

            // (i, x, y, z, c = x/y/z) �� offset3D[...] ��1����index
            return iOffset * HeatMapCol
                 + x * HeatMapCol * JointNum * 3
                 + y * HeatMapCol * HeatMapCol * JointNum * 3
                 + z;
        }

        // �e�֐߂ɂ��� �ő�X�R�A�{�N�Z�����I�t�Z�b�g�擾
        for (int i = 0; i < JointNum; i++)
        {
            //----------------------------------
            // �e�֐߂̑��݊m������ԍ����{�N�Z����
            // ���̊m�������
            //----------------------------------
            // �e�֐߂̐��_�X�R�A(�֐߂̑��݊m������ԍ����{�N�Z���̊m��)
            float maxScore = 0f;
            // �ō��X�R�A���o���{�N�Z���̃C���f�b�N�X
            int indexX = 0, indexY = 0, indexZ = 0;
            // �q�[�g�}�b�v��S�T��
            for (int z = 0; z < HeatMapCol; z++)
            for (int y = 0; y < HeatMapCol; y++)
            for (int x = 0; x < HeatMapCol; x++)
            {
                // 1������index ���Z�o
                int index = HeatMapIndex(i, z, y, x);
                // �q�[�g�}�b�v���Ŋ֐߈ʒu�̉ӏ��̃X�R�A���擾
                float score = HeatMap[index];
                // �X�R�A��臒l(����͏����l��0.0)���������ꍇ�͍̗p (�֐߃f�[�^�ɓK�p)
                // �S�T�����čő�X�R�A�̃C���f�b�N�X��T�����߂ɁAScore3D�����傫����΍X�V
                if (score > maxScore)
                {
                    maxScore = score;
                    indexX = x; 
                    indexY = y;
                    indexZ = z;
                }
            }
            // �������� maxX,maxY,maxZ �ŃI�t�Z�b�g�v�Z
            float offsetX = Offset[OffsetIndex(i, indexX, indexY, indexZ, "x")];
            float offsetY = Offset[OffsetIndex(i, indexX, indexY, indexZ, "y")];
            float offsetZ = Offset[OffsetIndex(i, indexX, indexY, indexZ, "z")];

            //----------------------------------
            // �֐�[i]�̑��݊m������ԍ����{�N�Z����
            // �֐�[i]�̑��݊m�����ꉞ�ۑ�
            //----------------------------------
            JointPoints[i].Score3D = maxScore;

            //----------------------------------
            // �֐�[i]�̈ʒu�m��
            //----------------------------------
            //  �֐�[i]��X���W
            JointPoints[i].Now3D.x =
            (
                offsetX 
                + 0.5f
                + indexX
            ) * ImgScale
            - InputImgSizeHalf;

            //  �֐�[i]��Y���W
            JointPoints[i].Now3D.y =
            InputImgSizeHalf -
            (
                offsetY
                + 0.5f
                + indexY
            ) * ImgScale;

            //  �֐�[i]��Z���W
            JointPoints[i].Now3D.z =
            (
                offsetZ
                + 0.5f
                + (indexZ - 14)
            ) * ImgScale;
        }
    }


    //==============================================================
    // �ǉ��|�W���v�Z�����f���ɓK�p
    //==============================================================
    void ApplyAdditionalJoint()
    {
        //----------------------------------
        // �ǉ��|�W�v�Z
        //----------------------------------
        // �K�ʒu = ���� [ ���E�������� , ���㕔 ]
        JointPoints[PositionIndex.hip.Int()].Now3D = ((
                JointPoints[PositionIndex.rThighBend.Int()].Now3D +   // �E����
                JointPoints[PositionIndex.lThighBend.Int()].Now3D     // ������
                ) / 2f
                + JointPoints[PositionIndex.abdomenUpper.Int()].Now3D // ���㕔
            ) / 2f;

        // ��ʒu = ���E������
        JointPoints[PositionIndex.neck.Int()].Now3D = (
            JointPoints[PositionIndex.rShldrBend.Int()].Now3D + // ����
            JointPoints[PositionIndex.lShldrBend.Int()].Now3D   // �E��
            ) / 2f;

        // �Ғňʒu = ���㕔
        JointPoints[PositionIndex.spine.Int()].Now3D = JointPoints[PositionIndex.abdomenUpper.Int()].Now3D;

        // (�񂩂�̑���)������ = �m�[�}���C�Y [ ���E������ - �� ]
        var headDir = Vector3.Normalize((
                JointPoints[PositionIndex.rEar.Int()].Now3D + // �E��
                JointPoints[PositionIndex.lEar.Int()].Now3D   // ����
                ) / 2f
                - JointPoints[PositionIndex.neck.Int()].Now3D // ��
            );

        // (�񂩂�̑���)�@�x�N�g�� = [ �@ - �� ]
        var noseVec =
            JointPoints[PositionIndex.Nose.Int()].Now3D - // �@
            JointPoints[PositionIndex.neck.Int()].Now3D;  // ��
        // (�񂩂�̑���)���ʒu = ������ * ���� [ �@�x�N�g�� , ������ ]
        var localHeadPos = headDir * Vector3.Dot(headDir, noseVec);
        // ���ʒu = �� + (�񂩂�̑���)���ʒu
        JointPoints[PositionIndex.head.Int()].Now3D = JointPoints[PositionIndex.neck.Int()].Now3D + localHeadPos;
    }


    //==============================================================
    // �J���}���t�B���^
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
        // ���C������
        jp.Pos3D.x = jp.X.x + (jp.Now3D.x - jp.X.x) * jp.K.x;
        jp.Pos3D.y = jp.X.y + (jp.Now3D.y - jp.X.y) * jp.K.y;
        jp.Pos3D.z = jp.X.z + (jp.Now3D.z - jp.X.z) * jp.K.z;
        jp.X = jp.Pos3D;
    }


    //==============================================================
    // ���[�p�X�t�B���^
    //==============================================================
    void LowPassFilter(VNectModel.JointPoint jp)
    {
        jp.PrevPos3D[0] = jp.Pos3D;
        for (var i = 1; i < jp.PrevPos3D.Length; i++)
            jp.PrevPos3D[i] = jp.PrevPos3D[i] * LowPassParam + jp.PrevPos3D[i - 1] * (1f - LowPassParam);
        jp.Pos3D = jp.PrevPos3D[jp.PrevPos3D.Length - 1];
    }
}
