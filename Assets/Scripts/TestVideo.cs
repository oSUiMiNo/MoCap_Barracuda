using UnityEngine;

public class TestVideo : MonoBehaviour
{
    public VideoCapture videoCapture;
    public int InputImageSize = 448;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // VideoCapture èâä˙âª
        videoCapture.Init(InputImageSize, InputImageSize);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
