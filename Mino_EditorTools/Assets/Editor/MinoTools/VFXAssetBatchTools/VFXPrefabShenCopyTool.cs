using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using System.IO;
using UnityEditorInternal;

/*
**  Author:     yangyun
**  DateTime:   2018/06/07 22:06
**	Module:     深度复制 prefab
**/


namespace ETools
{
    public class VFXPrefabShenCopyTool
    {
        [MenuItem("Tools/MinoTools/特效资源工具/深度复制Prefab")]
        private static void DeepCopyPrefabMenu()
        {
            OnBatchResource(DeepType.Material_Texture_Animation_FBX);
        }

        public enum DeepType
        {
            None,
            Material,                        //材质球
            Material_Animation,              //材质球、动画文件
            Material_Texture_Animation,      //材质球、贴图、动画文件
            Material_Texture_Animation_FBX,  //材质球、贴图、动画文件、模型
        }
        #region  variable

        static string[] CommonPath = new string[] { "Assets/Art/Optimization/Common/", "Assets/Art/GuangHuan/Common/", "Assets/Art/Obsolete/Common/" };

        static string[] ChildDirs = new string[] { "Ani", "Mat", "Tex", "Mod" };

        static Dictionary<string, string> refrenceDic = new Dictionary<string, string>();

        static string black1_Extensions = "*.FBX*.fbx*.cs*.shader";

        static string black2_Extensions = "*.cs*.shader";

        static string Suffix_Mat = ".mat";
        /// <summary>
        /// 
        /// </summary>
        static string matDeepExtensions = "*.mat*.PNG*.png*.TGA*.tga*.JPEG*.jepg*.JPG*.jpg";
        static List<string> materialList = new List<string>();
        /// <summary>
        /// 材质球链表信息
        /// </summary>
        static Dictionary<string, string> texDic = new Dictionary<string, string>();

        static string Suffix_Animator = ".controller";

        /// <summary>
        /// 
        /// </summary>
        static string aniDeepExtensions = "*.controller*.anim";
        static List<string> controllerList = new List<string>();
        /// <summary>
        /// 动画容器链表信息
        /// </summary>
        static Dictionary<string, string> animDic = new Dictionary<string, string>();
        /// <summary>
        /// 
        /// </summary>
        static string fbxDeepExtensions = "*.FBX*.fbx";
        static List<string> fbxList = new List<string>();

        /// <summary>
        /// 深度复制类型
        /// </summary>
        static DeepType coptyType = DeepType.Material;

        #endregion

        #region  private function

        public static void OnBatchResource(DeepType type)
        {
            if (EditorApplication.isPlaying == true)
            {
                EditorUtility.DisplayDialog("警告", "请先取消场景运行状态!", "知道了");
                return;
            }
            coptyType = type;
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
            {
                var curModel = System.Enum.GetName(typeof(SerializationMode), EditorSettings.serializationMode);
                var content = string.Format("当前EditorSetting为：{0}，确认切换为ForceText模式吗？", curModel);
                bool bEnsure = EditorUtility.DisplayDialog("切换EditorSetting模式", content, "OK", "Cancel");
                if (!bEnsure)
                {
                    return;
                }
            }
            EditorSettings.serializationMode = SerializationMode.ForceText;

            GameObject[] objs = Selection.gameObjects;
            if (objs == null)
            {
                EditorUtility.DisplayDialog("警告", "请在 preject 视图中选中 prefab 资源", "确定", "取消");
                return;
            }
            if (objs.Length > 1)
            {
                EditorUtility.DisplayDialog("警告", "禁止选择多个，请在 preject 视图中选中单个 prefab 资源", "确定", "取消");
                return;
            }
            GameObject obj = objs[0];

            if (!PrefabUtility.IsPartOfPrefabAsset(obj))
            {
                EditorUtility.DisplayDialog("警告", "请选择 preject 视图中prefab 资源", "确定", "取消");
                return;
            }

            OnBatchInit();

            string prefabPath = AssetDatabase.GetAssetPath(obj);
            string newName = obj.name + "_new";
            string newprefabPath = prefabPath.Substring(0, prefabPath.LastIndexOf("/") + 1) + newName + ".prefab";// prefabPath.Replace(obj.name, newName);
            if (File.Exists(newprefabPath))
            {
                File.Delete(newprefabPath);
                AssetDatabase.Refresh();
            }
            AssetDatabase.CopyAsset(prefabPath, newprefabPath);
            AssetDatabase.Refresh();

            if (!File.Exists(newprefabPath))
            {
                EditorUtility.DisplayDialog("警告", "prefab 复制失败", "确定", "取消");
                return;
            }

            string newRefrenceDir = prefabPath.Substring(0, prefabPath.LastIndexOf("/") + 1) + newName + "_dependencies";
            //newRefrenceDir = newRefrenceDir.Substring(0, newRefrenceDir.LastIndexOf("."));
            if (Directory.Exists(newRefrenceDir))
            {
                Directory.Delete(newRefrenceDir, true);
            }
            Directory.CreateDirectory(newRefrenceDir);
            AssetDatabase.Refresh();

            string[] depends = AssetDatabase.GetDependencies(new string[] { newprefabPath });
            FilterDependencies(depends, newRefrenceDir, newprefabPath);

            RefreshPrefabGuid(refrenceDic, newprefabPath);
            RefreshDeepGuid(texDic, materialList);
            RefreshDeepGuid(animDic, controllerList);

            if (coptyType != DeepType.Material)
            {
                CheckChildsDir(newRefrenceDir);
            }

            Debug.Log("完成 prefab 深度 复制");
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("通知", "完成 " + obj.name + " 深度复制", "知道了");
        }
        static void OnBatchInit()
        {
            refrenceDic.Clear();

            materialList.Clear();
            texDic.Clear();
            controllerList.Clear();
            animDic.Clear();
            fbxList.Clear();

        }

        static void FilterDependencies(string[] depends, string newRefrenceDir, string newprefabPath)
        {
            for (int i = 0; i < depends.Length; i++)
            {
                string filepath = depends[i];
                if (filepath == newprefabPath)
                {
                    continue;
                }
                //Debug.Log("depends : " + depends[i]);
                string extension = filepath.Substring(filepath.LastIndexOf("."));
                if (coptyType == DeepType.Material_Texture_Animation_FBX)
                {
                    if (black2_Extensions.Contains(extension))
                    {
                        continue;
                    }
                }
                else
                {
                    if (black1_Extensions.Contains(extension))
                    {
                        continue;
                    }
                }
                string fileFullName = "";
                if (filepath.Contains("/"))
                {
                    fileFullName = filepath.Substring(filepath.LastIndexOf("/") + 1);
                }
                else
                {
                    fileFullName = filepath;
                }
                string newfilepath = newRefrenceDir + "/" + fileFullName;
                //Debug.Log("extension : " + extension + " | fileFullName: " + fileFullName);
                OnHandleDepends(extension, filepath, newfilepath);
            }
        }
        /// <summary>
        /// 格式化 数字
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        static string FormatSuffixNum(int num)
        {
            return num.ToString("00");
        }
        static public string GenNewRepetionName(string newfileParentPath, string newNamePrefix, string fileExtension, int index)
        {
            string id = FormatSuffixNum(index);
            string newName = newNamePrefix;
            string path = newfileParentPath + newName + fileExtension;
            //Debug.Log("@@@  newPath: " + newPath + " extension: " + extension + " childName: " + childName + " pathFolder: " + pathFolder);
            while (File.Exists(path))
            {
                index += 1;
                id = FormatSuffixNum(index);
                newName = newNamePrefix + "_" + id;
                path = newfileParentPath + newName + fileExtension;
            }
            //Debug.Log("GenNewRepetionPath: " + path);
            return path;
        }
        /// <summary>
        /// 检查 复制资源
        /// </summary>
        static void CheckCopyAssets(string extension, string filepath, ref string newfilepath)
        {
            string newfileParentPath = newfilepath.Substring(0, newfilepath.LastIndexOf("/") + 1);
            string newflieName = newfilepath.Substring(newfilepath.LastIndexOf("/") + 1).Replace(extension, "");
            newfilepath = GenNewRepetionName(newfileParentPath, newflieName, extension, 0);
            AssetDatabase.CopyAsset(filepath, newfilepath);
            //AssetDatabase.Refresh();
        }

        static void OnHandleDepends(string extension, string filepath, string newfilepath)
        {
            switch (coptyType)
            {
                case DeepType.Material:
                    if (extension == Suffix_Mat)
                    {
                        Object sourceobj = AssetDatabase.LoadAssetAtPath(filepath, typeof(Object));
                        CheckCopyAssets(extension, filepath, ref newfilepath);
                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                    }
                    break;
                case DeepType.Material_Texture_Animation:
                    if (matDeepExtensions.Contains(extension))
                    {
                        Object sourceobj = AssetDatabase.LoadAssetAtPath(filepath, typeof(Object));

                        if (InCommon(filepath))
                        {
                            break;
                        }
                        CheckCopyAssets(extension, filepath, ref newfilepath);
                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        if (extension == Suffix_Mat)
                        {
                            refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                            materialList.Add(newfilepath);
                        }
                        else
                        {
                            texDic.Add("guid: " + oldguid, "guid: " + newguid);
                        }
                    }
                    if (aniDeepExtensions.Contains(extension))
                    {
                        Object sourceobj = AssetDatabase.LoadAssetAtPath(filepath, typeof(Object));
                        CheckCopyAssets(extension, filepath, ref newfilepath);
                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        if (extension == Suffix_Animator)
                        {
                            refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                            controllerList.Add(newfilepath);
                        }
                        else
                        {
                            animDic.Add("guid: " + oldguid, "guid: " + newguid);
                        }
                    }
                    break;
                case DeepType.Material_Texture_Animation_FBX:
                    if (matDeepExtensions.Contains(extension))
                    {
                        //Object sourceobj = AssetDatabase.LoadAssetAtPath(filepath, typeof(Object));
                        if (InCommon(filepath))
                        {
                            break;
                        }
                        CheckCopyAssets(extension, filepath, ref newfilepath);
                        //Debug.Log("---->>> filePath: " + filepath + " newfilePath: " + newfilepath);
                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        if (extension == Suffix_Mat)
                        {
                            refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                            materialList.Add(newfilepath);
                        }
                        else
                        {
                            texDic.Add("guid: " + oldguid, "guid: " + newguid);
                        }
                    }
                    if (aniDeepExtensions.Contains(extension))
                    {
                        CheckCopyAssets(extension, filepath, ref newfilepath);

                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        if (extension == Suffix_Animator)
                        {
                            refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                            controllerList.Add(newfilepath);
                        }
                        else
                        {
                            animDic.Add("guid: " + oldguid, "guid: " + newguid);
                        }
                    }
                    if (fbxDeepExtensions.Contains(extension))
                    {
                        if (InCommon(filepath))
                        {
                            break;
                        }
                        CheckCopyAssets(extension, filepath, ref newfilepath);

                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                        fbxList.Add(newfilepath);
                    }

                    break;
                case DeepType.Material_Animation:
                    if (extension == Suffix_Mat)
                    {
                        CheckCopyAssets(extension, filepath, ref newfilepath);

                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                        materialList.Add(newfilepath);
                    }
                    if (aniDeepExtensions.Contains(extension))
                    {
                        CheckCopyAssets(extension, filepath, ref newfilepath);

                        string oldguid = AssetDatabase.AssetPathToGUID(filepath);
                        string newguid = AssetDatabase.AssetPathToGUID(newfilepath);
                        if (extension == Suffix_Animator)
                        {
                            refrenceDic.Add("guid: " + oldguid, "guid: " + newguid);
                            controllerList.Add(newfilepath);
                        }
                        else
                        {
                            animDic.Add("guid: " + oldguid, "guid: " + newguid);
                        }
                    }
                    break;
            }
        }

        static void RefreshPrefabGuid(Dictionary<string, string> refrenceDic, string newprefabpath)
        {

            string oldguid = "";
            string newguid = "";
            string prefabText = File.ReadAllText(newprefabpath, System.Text.Encoding.UTF8);
            if (string.IsNullOrEmpty(prefabText))
            {
                return;
            }
            if (refrenceDic.Count == 0)
            {
                return;
            }
            List<string> oldguidlist = new List<string>();
            oldguidlist.AddRange(refrenceDic.Keys);

            for (int i = 0; i < oldguidlist.Count; i++)
            {
                oldguid = oldguidlist[i];
                newguid = refrenceDic[oldguid];
                if (prefabText.Contains(oldguid))
                {
                    prefabText = prefabText.Replace(oldguid, newguid);
                    //Debug.Log("oldguid : " + oldguid + " newguid:" + newguid);
                }
            }
            File.WriteAllText(newprefabpath, prefabText);

            //AssetDatabase.Refresh();
        }

        static void RefreshDeepGuid(Dictionary<string, string> refrenceDic, List<string> objList)
        {
            if (refrenceDic.Count == 0 || objList.Count == 0)
            {
                return;
            }
            string oldguid = "";
            string newguid = "";
            for (int k = 0; k < objList.Count; k++)
            {
                string objectText = File.ReadAllText(objList[k], System.Text.Encoding.UTF8);
                if (objectText == null)
                {
                    continue;
                }
                List<string> oldguidlist = new List<string>();
                oldguidlist.AddRange(refrenceDic.Keys);

                //Debug.Log("@@@@@ object path: " + objList[k]);

                for (int i = 0; i < oldguidlist.Count; i++)
                {
                    oldguid = oldguidlist[i];
                    newguid = refrenceDic[oldguid];

                    if (objectText.Contains(oldguid))
                    {
                        objectText = objectText.Replace(oldguid, newguid);
                        //Debug.Log("oldguid : " + oldguid + " newguid:" + newguid);
                        //Debug.Log("oldguid Path: " + AssetDatabase.GUIDToAssetPath(oldguid.Replace("guid: ", "")) + " newguid Path: " + AssetDatabase.GUIDToAssetPath(newguid.Replace("guid: ", "")));
                    }
                }
                File.WriteAllText(objList[k], objectText);
            }

            //AssetDatabase.Refresh();
        }

        static void CheckChildsDir(string filepath)
        {
            if (coptyType == DeepType.Material)
            {
                return;
            }
            //static string[] ChildDirs = new string[] { "Ani", "Mat", "Tex", "Mod" };
            //materialList.Clear();
            //texDic.Clear();
            //controllerList.Clear();
            //animDic.Clear();
            //fbxList.Clear();
            if (materialList.Count > 0)
            {
                string matFolder = filepath + "/" + "Mat";
                if (!Directory.Exists(matFolder))
                {
                    Directory.CreateDirectory(matFolder);
                    AssetDatabase.Refresh();
                }
                for (int i = 0; i < materialList.Count; i++)
                {
                    string itempath = matFolder + "/" + materialList[i].Substring(materialList[i].LastIndexOf("/") + 1);
                    AssetDatabase.MoveAsset(materialList[i], itempath);
                }
            }

            if (texDic.Count > 0)
            {
                string texFolder = filepath + "/" + "Tex";
                if (!Directory.Exists(texFolder))
                {
                    Directory.CreateDirectory(texFolder);
                    AssetDatabase.Refresh();
                }
                List<string> texList = new List<string>();
                texList.AddRange(texDic.Values);
                for (int i = 0; i < texList.Count; i++)
                {
                    string assetspath = AssetDatabase.GUIDToAssetPath(texList[i].Replace("guid: ", ""));
                    string itempath = texFolder + "/" + assetspath.Substring(assetspath.LastIndexOf("/") + 1);
                    AssetDatabase.MoveAsset(assetspath, itempath);
                }
            }

            if (controllerList.Count > 0)
            {
                string controllerFolder = filepath + "/" + "Ani";
                if (!Directory.Exists(controllerFolder))
                {
                    Directory.CreateDirectory(controllerFolder);
                    AssetDatabase.Refresh();
                }
                for (int i = 0; i < controllerList.Count; i++)
                {
                    string itempath = controllerFolder + "/" + controllerList[i].Substring(controllerList[i].LastIndexOf("/") + 1);
                    AssetDatabase.MoveAsset(controllerList[i], itempath);
                }
            }
            if (animDic.Count > 0)
            {
                string aniFolder = filepath + "/" + "Ani";
                if (!Directory.Exists(aniFolder))
                {
                    Directory.CreateDirectory(aniFolder);
                    AssetDatabase.Refresh();
                }

                List<string> aniclipList = new List<string>();
                aniclipList.AddRange(animDic.Values);
                for (int i = 0; i < aniclipList.Count; i++)
                {
                    string assetspath = AssetDatabase.GUIDToAssetPath(aniclipList[i].Replace("guid: ", ""));
                    string itempath = aniFolder + "/" + assetspath.Substring(assetspath.LastIndexOf("/") + 1);
                    AssetDatabase.MoveAsset(assetspath, itempath);
                }
            }

            if (fbxList.Count > 0)
            {
                string fbxFolder = filepath + "/" + "Mod";
                if (!Directory.Exists(fbxFolder))
                {
                    Directory.CreateDirectory(fbxFolder);
                    AssetDatabase.Refresh();
                }
                for (int i = 0; i < fbxList.Count; i++)
                {
                    string itempath = fbxFolder + "/" + fbxList[i].Substring(fbxList[i].LastIndexOf("/") + 1);
                    AssetDatabase.MoveAsset(fbxList[i], itempath);
                }
            }
        }

        static bool InCommon(string path)
        {
            bool isInCommon = false;
            if (!string.IsNullOrEmpty(path))
            {
                for (int i = 0; i < CommonPath.Length; i++)
                {
                    if (path.StartsWith(CommonPath[i]))
                    {
                        isInCommon = true;
                        continue;
                    }
                }
            }

            return isInCommon;
        }

        #endregion

    }
}

