﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class VideoCapture : MonoBehaviour
{
    public GameObject TextureDisp_Input;
    public GameObject TextureDisp_Soutce;
    public RawImage VideoScreen;
    public float SoutceTextureScale = 1;
    public LayerMask _layer;
    public bool UseWebCam = true;
    public int WebCamIndex = 0;
    public VideoPlayer VideoPlayer;

    private WebCamTexture webCamTexture;
    private RenderTexture videoTexture;
    public RenderTexture InputTexture { get; private set; }

    private int videoScreenWidth = 2560;
    private int bgWidth, bgHeight;



    // エントリーポイント
    public void Init(int bgWidth, int bgHeight)
    {
        this.bgWidth = bgWidth;
        this.bgHeight = bgHeight;
        if (UseWebCam) CameraPlayStart();
        else VideoPlayStart();
    }

    // Webカメラ再生
    private void CameraPlayStart()
    {
        Debug.Log("カメラ");
        WebCamDevice[] devices = WebCamTexture.devices;
        if(devices.Length <= WebCamIndex)
        {
            WebCamIndex = 0;
        }
        
        webCamTexture = new WebCamTexture(devices[WebCamIndex].name);

        var sd = VideoScreen.GetComponent<RectTransform>();
        VideoScreen.texture = webCamTexture;

        webCamTexture.Play();

        sd.sizeDelta = new Vector2(videoScreenWidth, videoScreenWidth * webCamTexture.height / webCamTexture.width);
        var aspect = (float)webCamTexture.width / webCamTexture.height;
        TextureDisp_Soutce.transform.localScale = new Vector3(aspect, 1, 1) * SoutceTextureScale;
        TextureDisp_Soutce.GetComponent<Renderer>().material.mainTexture = webCamTexture;

        InitMainTexture();
    }

    // 動画再生
    private void VideoPlayStart()
    {
        Debug.Log("動画");
        videoTexture = new RenderTexture((int)VideoPlayer.clip.width, (int)VideoPlayer.clip.height, 24);

        VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
        VideoPlayer.targetTexture = videoTexture;
        var sd = VideoScreen.GetComponent<RectTransform>();
        sd.sizeDelta = new Vector2(videoScreenWidth, (int)(videoScreenWidth * VideoPlayer.clip.height / VideoPlayer.clip.width));
        VideoScreen.texture = videoTexture;

        VideoPlayer.Play();

        var aspect = (float)videoTexture.width / videoTexture.height;

        TextureDisp_Soutce.transform.localScale = new Vector3(aspect, 1, 1) * SoutceTextureScale;
        TextureDisp_Soutce.GetComponent<Renderer>().material.mainTexture = videoTexture;

        InitMainTexture();
    }

    Camera camera;
    // MainTexture 作成
    private void InitMainTexture()
    {
        GameObject go = new GameObject("Cam_MainTexture", typeof(Camera));

        go.transform.parent = TextureDisp_Soutce.transform;
        go.transform.localScale = new Vector3(-1.0f, -1.0f, 1.0f);
        go.transform.localPosition = new Vector3(0.0f, 0.0f, -2.0f);
        go.transform.localEulerAngles = Vector3.zero;
        go.layer = _layer;

        camera = go.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 0.5f ;
        camera.depth = -5;
        camera.depthTextureMode = 0;
        camera.clearFlags = CameraClearFlags.Color;
        camera.backgroundColor = Color.black;
        camera.cullingMask = _layer.value;
        camera.useOcclusionCulling = false;
        camera.nearClipPlane = 1.0f;
        camera.farClipPlane = 5.0f;
        camera.allowMSAA = false;
        camera.allowHDR = false;

        InputTexture = new RenderTexture(bgWidth, bgHeight, 24, RenderTextureFormat.RGB565, RenderTextureReadWrite.sRGB)
        {
            useMipMap = false,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        camera.targetTexture = InputTexture;
        if (TextureDisp_Input.activeSelf) TextureDisp_Input.GetComponent<Renderer>().material.mainTexture = InputTexture;
    }
}
