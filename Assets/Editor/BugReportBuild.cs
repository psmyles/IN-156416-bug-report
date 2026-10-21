using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

// Command-line build used for the IN-156416 measurements (see README.md for the exact command).
// Builds the scenes in Build Settings for Windows 64-bit and logs lines prefixed with [IN-156416] to the Editor log.
public static class BugReportBuild
{
    private const string LogPrefix = "[IN-156416] ";
    private const string OutputPath = "Builds/Windows/IN-156416.exe";

    public static void BuildWindows()
    {
        ReimportVisualEffects();

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes),
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        UnityEngine.Debug.Log(LogPrefix + "Player build started");
        Stopwatch stopwatch = Stopwatch.StartNew();
        BuildReport report = BuildPipeline.BuildPlayer(options);
        UnityEngine.Debug.Log(LogPrefix + "Player build finished: " + report.summary.result + " in " + stopwatch.Elapsed.TotalSeconds.ToString("F1") + " s");
        LogTopLevelSteps(report);

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception(LogPrefix + "Player build failed");
        }
    }

    // Branches may be switched in place on the same Library, so regenerate the VFX shaders with whichever
    // VFX Graph package is installed, then report how many generated shaders carry the RenderPipeline tag.
    private static void ReimportVisualEffects()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset");
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guids[i]), ImportAssetOptions.ForceUpdate);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        int shaderCount = 0;
        int taggedCount = 0;
        ShaderTagId renderPipelineTag = new ShaderTagId("RenderPipeline");
        for (int i = 0; i < guids.Length; i++)
        {
            UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guids[i]));
            for (int o = 0; o < objects.Length; o++)
            {
                Shader shader = objects[o] as Shader;
                if (shader == null)
                {
                    continue;
                }

                shaderCount++;
                if (shader.FindSubshaderTagValue(0, renderPipelineTag).name == "UniversalPipeline")
                {
                    taggedCount++;
                }
            }
        }

        UnityEngine.Debug.Log(LogPrefix + "Reimported " + guids.Length + " VFX assets in " + stopwatch.Elapsed.TotalSeconds.ToString("F1") + " s; "
            + taggedCount + " of " + shaderCount + " generated VFX shaders have \"RenderPipeline\"=\"UniversalPipeline\"");
    }

    private static void LogTopLevelSteps(BuildReport report)
    {
        BuildStep[] steps = report.steps;
        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i].depth <= 1)
            {
                UnityEngine.Debug.Log(LogPrefix + "Step \"" + steps[i].name + "\": " + steps[i].duration.TotalSeconds.ToString("F1") + " s");
            }
        }
    }
}
