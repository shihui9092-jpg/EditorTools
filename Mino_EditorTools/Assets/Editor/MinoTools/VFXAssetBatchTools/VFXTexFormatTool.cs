#if UNITY_2017_1_OR_NEWER
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

/*
**  Author:     yangyun
**  DateTime:   2018/05/30 14:05
**	Module:     修改图片
**/

namespace ETools
{
    /*
    正方贴图:
    IOS下：
    a.普通不透明：RGB PVRTC 4BITS
    b.普通透明：RGBA PVRTC 4BITS
    Android下：
    a.普通不透明：RGB ETC 4BITS
    b.普通透明:
    因为没有通用最兼容的格式，所以一般情况是用RGBA 16BIT或有针对性的选择DXT5/ATC8 BITS/ETC2 8BITS。如果有技术支持，可以采用RGB ETC 4BITS加一张ALPHA 8的贴图来实现透明效果。
    非正方贴图:
    一般采用16位压缩，16位会带来颜色损失，但如果本来美术就是按16BITS画的话，就不会损失，日本好些手游都是按16BITS来画的。这样的游戏一般少渐变艳度高比较容易看出来。
    a.不透明贴图: RGB 16BITS
    d.透明贴图：RGBA 16BITS
    高清不压缩贴图:
    RGBA 32BIT
    */
    public class VFXTexFormatTool
    {
        [MenuItem("Tools/MinoTools/特效资源工具/贴图格式化/512")]
        private static void Format512Menu()
        {
            OnFormatVFXTexture(CompressedType.All_512);
        }

        [MenuItem("Tools/MinoTools/特效资源工具/贴图格式化/256")]
        private static void Format256Menu()
        {
            OnFormatVFXTexture(CompressedType.All_256);
        }

        [MenuItem("Tools/MinoTools/特效资源工具/贴图格式化/128")]
        private static void Format128Menu()
        {
            OnFormatVFXTexture(CompressedType.All_128);
        }

        public enum CompressedType
        {
            All_512,
            All_256,
            All_128,
            Size64_512,
        }


        public static void ShowProgress(float val, int total, int cur)
        {
            EditorUtility.DisplayProgressBar("设置图片中...", string.Format("请稍等({0}/{1}) ", cur, total), val);
        }


        static Object[] GetSelectedTextures()
        {
            return Selection.GetFiltered(typeof(Texture2D), SelectionMode.DeepAssets);
        }


        #region atlas deal

        private static CompressedType curType = CompressedType.All_512;

        public static int CompressQuality = 50;

        public static float HalveRate = 1.0f;

        public static bool iPhoneRGB32 = false;


        //[MenuItem("ArtTools/10. Format TextureImporter([特效人员专用] 选中 Project 视图中 图片文件或者文件夹设置图片格式)", false, ArtToolsMenuConst.Menu_110)]
        public static void OnFormatVFXTexture(CompressedType type)
        {
            curType = type;

            Object[] objects = GetSelectedTextures();
            if (objects == null || objects.Length == 0)
            {
                EditorUtility.DisplayDialog("警告", "没有选中文件夹或者文件夹下面没有图片文件", "确定", "取消");
                return;
            }
            //UnityEngine.Object[] objects = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets); //获取选择文件夹
            for (int i = 0; i < objects.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(objects[i]).Replace("\\", "/");
                //EditorUtility.DisplayProgressBar("处理中>>>", path, (float)i / (float)objects.Length);
                FormatVFXTexture(path);
            }
            //EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Debug.Log("图片批量格式化完成");
        }

        private static void OnFilterDirTexture(string dirPath)
        {
            string[] files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);

            for (int i = 0; i < files.Length; i++)
            {
                //筛选出png和jpg图片
                //EditorUtility.DisplayProgressBar("处理中>>>", files[i], (float)i / (float)files.Length);
                FormatVFXTexture(files[i]);
            }
            AssetDatabase.Refresh();
            //EditorUtility.ClearProgressBar();
            //EditorUtility.DisplayDialog("成功", "处理完成！", "好的");
        }

        public static void FormatTextureSuffix(string filePath)
        {
            if (filePath.EndsWith(".PNG"))
            {
                File.Move(filePath, filePath.Replace(".PNG", ".png"));
            }
            if (filePath.EndsWith(".TGA"))
            {
                File.Move(filePath, filePath.Replace(".TGA", ".tga"));
            }
            if (filePath.EndsWith(".JPG"))
            {
                File.Move(filePath, filePath.Replace(".JPG", ".jpg"));
            }
            if (filePath.EndsWith(".psd"))
            {
                Debug.LogError("PSD图片，请检查: " + filePath, AssetDatabase.LoadAssetAtPath(filePath, typeof(Texture2D)));
            }
            if (!filePath.EndsWith(".png") && !filePath.EndsWith(".jpg") && !filePath.EndsWith(".tga"))
            {
                //Debug.Log("not match " + filePath);
                return;
            }


        }
        /// <summary>
        /// 格式化图片
        /// </summary>
        /// <param name="filePath"></param>
        public static void FormatVFXTexture(string filePath)
        {
            filePath = filePath.Replace("\\", "/");
            string name = filePath.Substring(filePath.LastIndexOf("/") + 1);

            FormatTextureSuffix(filePath);

            TextureImporter textureImporter = AssetImporter.GetAtPath(filePath) as TextureImporter;
            if (textureImporter == null)
            {
                Debug.Log("textureImporter == null filePath: " + filePath);
                return;
            }

            textureImporter.npotScale = TextureImporterNPOTScale.ToNearest;
            int twidth, theight;
            Texture2D texture = AssetDatabase.LoadAssetAtPath(filePath, typeof(Texture2D)) as Texture2D;
            GetImageSize(texture, out twidth, out theight);
            int textureSize = Mathf.Max(theight, twidth);
            if (textureSize <= 64)
            {
                textureImporter.npotScale = TextureImporterNPOTScale.None;
            }
            //设置图片最大尺寸
            switch (curType)
            {
                case CompressedType.All_512:
                    if (textureSize > 512)
                    {
                        textureSize = 512;
                    }
                    break;
                case CompressedType.All_256:
                    if (textureSize > 256)
                    {
                        textureSize = 256;
                    }
                    break;
                case CompressedType.All_128:
                    if (textureSize > 128)
                    {
                        textureSize = 128;
                    }
                    break;
                case CompressedType.Size64_512:
                    if (textureSize > 512)
                    {
                        textureSize = 512;
                    }
                    break;
            }

            textureImporter.mipmapEnabled = false;

            TextureImporterSettings settings = new TextureImporterSettings();
            textureImporter.ReadTextureSettings(settings);
            textureImporter.textureType = TextureImporterType.Default;

            //settings.wrapMode = TextureWrapMode.Repeat;
            //是否透明
            settings.alphaIsTransparency = textureImporter.DoesSourceTextureHaveAlpha();
            textureImporter.SetTextureSettings(settings);

            FormatTexAndroid(textureSize, ref textureImporter);
            FormatTexiPhone(textureSize, ref textureImporter);

            DoAssetReimport(filePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            //textureImporter.SaveAndReimport();
        }

        /// <summary>
        /// 格式化安卓平台
        /// </summary>
        private static void FormatTexAndroid(int textureSize, ref TextureImporter textureImporter)
        {
            string platform = "Android";

            int androidMaxTextureSize = 0;
            TextureImporterFormat androidTextureFormat = UnityEditor.TextureImporterFormat.ETC_RGB4;

            bool isAndroidOverWrite = textureImporter.GetPlatformTextureSettings(platform, out androidMaxTextureSize, out androidTextureFormat);
            if (textureImporter.DoesSourceTextureHaveAlpha())
            {
                if (curType == CompressedType.Size64_512 && textureSize <= 64)
                {
                    androidTextureFormat = TextureImporterFormat.RGBA32;
                }
                else
                {
                    androidTextureFormat = TextureImporterFormat.ETC2_RGBA8;
                }
            }
            else
            {
                if (curType == CompressedType.Size64_512 && textureSize <= 64)
                {
                    androidTextureFormat = TextureImporterFormat.ETC_RGB4;
                }
                else
                {
                    androidTextureFormat = TextureImporterFormat.ETC_RGB4;
                }

            }

            TextureImporterPlatformSettings textureSetting = textureImporter.GetPlatformTextureSettings(platform);
            textureSetting.allowsAlphaSplitting = false;
            textureSetting.format = androidTextureFormat;
            textureSetting.maxTextureSize = (int)textureSize;
            textureSetting.textureCompression = TextureImporterCompression.Compressed;

            textureImporter.SetPlatformTextureSettings(textureSetting);

            textureImporter.SaveAndReimport();
        }

        /// <summary>
        /// 格式化 iOS 平台
        /// </summary>
        private static void FormatTexiPhone(int textureSize, ref TextureImporter textureImporter)
        {
            string platform = "iPhone";
            int iphoneMaxTextureSize = 0;
            TextureImporterFormat iphoneTextureFormat = UnityEditor.TextureImporterFormat.PVRTC_RGBA4;

            bool isIphoneOverWrite = textureImporter.GetPlatformTextureSettings(platform, out iphoneMaxTextureSize, out iphoneTextureFormat);
            if (textureImporter.DoesSourceTextureHaveAlpha())
            {
                if (textureSize <= 64 || iPhoneRGB32)
                {
                    iphoneTextureFormat = TextureImporterFormat.RGBA32;
                }
                else
                {
                    iphoneTextureFormat = TextureImporterFormat.PVRTC_RGBA4;
                }

            }
            else
            {
                if (textureSize <= 64 || iPhoneRGB32)
                {
                    iphoneTextureFormat = TextureImporterFormat.RGB24;
                }
                else
                {
                    iphoneTextureFormat = TextureImporterFormat.PVRTC_RGB4;
                }
            }

            TextureImporterPlatformSettings textureSetting = textureImporter.GetPlatformTextureSettings(platform);
            textureSetting.allowsAlphaSplitting = false;
            textureSetting.format = iphoneTextureFormat;
            textureSetting.maxTextureSize = (int)textureSize;
            textureSetting.textureCompression = TextureImporterCompression.Compressed;

            textureImporter.SetPlatformTextureSettings(textureSetting);

            textureImporter.SaveAndReimport();
        }

        private static int GetValidSize(int size)
        {
            int result = 0;
            if (size <= 18)
            {
                result = 16;
            }
            else
            if (size <= 48)
            {
                result = 32;
            }
            else if (size <= 96)
            {
                result = 64;
            }
            else if (size <= 192)
            {
                result = 128;
            }
            else if (size <= 384)
            {
                result = 256;
            }
            else if (size <= 768)
            {
                result = 512;
            }
            else if (size <= 1536)
            {
                result = 1024;
            }
            else if (size <= 3072)
            {
                result = 2048;
            }

            return result;
        }

        private static bool GetImageSize(Texture2D texture, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (texture == null)
            {
                return false;
            }

            width = texture.width;
            height = texture.height;
            return true;
        }

        private static void DoAssetReimport(string path, ImportAssetOptions options)
        {
            AssetDatabase.ImportAsset(path, options);
        }

        #endregion

    }
}
#endif