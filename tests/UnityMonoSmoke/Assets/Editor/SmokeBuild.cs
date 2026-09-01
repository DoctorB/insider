using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Insider.UnityMonoSmoke.Editor
{
    public static class SmokeBuild
    {
        private const string OutputArgument = "-insiderSmokeOutput";
        private const string TemporaryScenePath = "Assets/__InsiderUnityMonoSmoke.unity";

        public static void BuildWindows64()
        {
            var outputPath = ReadRequiredArgument(OutputArgument);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("The smoke player output path has no parent directory.");
            }

            Directory.CreateDirectory(outputDirectory);
            PlayerSettings.companyName = "Insider";
            PlayerSettings.productName = "Insider Unity Mono Smoke";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, TemporaryScenePath))
            {
                throw new InvalidOperationException("Could not create the temporary smoke scene.");
            }

            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { TemporaryScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Unity smoke player build failed: {report.summary.result} with {report.summary.totalErrors} errors.");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(TemporaryScenePath);
            }
        }

        private static string ReadRequiredArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(arguments[index + 1]);
                }
            }

            throw new InvalidOperationException($"Missing required command-line argument '{name}'.");
        }
    }
}
