using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Assets.SizeboxSDK.Editor
{
    // Headless front end for "Sizebox SDK > Export Model" so an object can be exported from the
    // command line:
    //   Unity.exe -batchmode -projectPath <SizeboxSdk> -executeMethod Assets.SizeboxSDK.Editor.BatchExport.Run
    //             -exportAsset "Assets/Import/Object/HeightRuler/Height Ruler.fbx" -logFile <log> -quit
    // It opens the preview scene (ModelExporter needs Camera.main for the thumbnail), selects the
    // model asset and calls the SDK's own exporter, so the result is exactly what the menu makes.
    public static class BatchExport
    {
        public static void Run()
        {
            string assetPath = Arg("-exportAsset");
            if (string.IsNullOrEmpty(assetPath))
                Fail("missing -exportAsset <path inside the project>");

            try
            {
                if (File.Exists("Assets/Preview Scene.unity"))
                    EditorSceneManager.OpenScene("Assets/Preview Scene.unity");
                if (Camera.main == null)
                {
                    var go = new GameObject("Main Camera") { tag = "MainCamera" };
                    go.AddComponent<Camera>();
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (model == null)
                    Fail("could not load " + assetPath);
                Selection.activeObject = model;

                MethodInfo export = typeof(ModelExporter).GetMethod("ExportModel", BindingFlags.NonPublic | BindingFlags.Static);
                if (export == null)
                    Fail("ModelExporter.ExportModel not found");
                Debug.Log("BatchExport: exporting " + assetPath);
                export.Invoke(null, null);
                AssetDatabase.SaveAssets();
                Debug.Log("BatchExport: done");
            }
            catch (Exception e)
            {
                Fail(e.ToString());
            }
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        private static void Fail(string message)
        {
            Debug.LogError("BatchExport: " + message);
            EditorApplication.Exit(1);
        }
    }
}
