using UnityEngine;
using System.Collections;
using System.IO;

namespace taecg.tools
{
    public class XuLieTuExporterCtrl : MonoBehaviour
    {
        [HideInInspector] public Camera cam;
        [HideInInspector] public string imageFormat;
        [HideInInspector] public bool isEnabledAlpha;
        [HideInInspector] public Vector2 resolution;
        [HideInInspector] public int frameCount;
        [HideInInspector] public string fileName;
        [HideInInspector] public string filePath;
        [HideInInspector] public int rangeStart;
        [HideInInspector] public int rangeEnd;

        private void Awake()
        {
            // 未显式指定相机时，回退到主相机
            if (cam == null)
                cam = Camera.main;
        }

        private void Start()
        {
            Time.captureFramerate = frameCount;
        }

        /// <summary>
        /// 生成序列图
        /// </summary>
        public void TakeSequenceScreenShot()
        {
            StartCoroutine(WaitTakeSequenceScreenShot());
        }

        private IEnumerator WaitTakeSequenceScreenShot()
        {
            yield return new WaitForEndOfFrame();

            int resWidthN = (int)resolution.x;
            int resHeightN = (int)resolution.y;

            RenderTexture rt = new RenderTexture(resWidthN, resHeightN,24);
            cam.targetTexture = rt;

            TextureFormat _texFormat;
            if (isEnabledAlpha)
                _texFormat = TextureFormat.ARGB32;
            else
                _texFormat = TextureFormat.RGB24;

            Texture2D tex = new Texture2D(resWidthN, resHeightN, _texFormat, false);


            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, resWidthN, resHeightN), 0, 0);
            tex.Apply();

            //清空rendertexture
            cam.targetTexture = null;
            RenderTexture.active = null; 
            if (!isEnabledAlpha)
                GameObject.Destroy(rt);

            byte[] bytes;
            switch(imageFormat)
            {
                case ".png":
                    bytes = tex.EncodeToPNG();
                    break;
                case ".jpg":
                    bytes = tex.EncodeToJPG();
                    break;
                default:
                    bytes = tex.EncodeToPNG();
                    break;
            }
            string outputPath = Path.Combine(filePath, fileName + "_" + Time.frameCount + imageFormat);
            File.WriteAllBytes(outputPath, bytes);
        }
    }
}