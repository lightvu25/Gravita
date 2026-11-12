#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Solo.MOST_IN_ONE.Editor
{
    /// <summary>
    /// Adds the native iOS frameworks required by MOST_HapticFeedback.mm to the generated Xcode project.
    /// This is not a custom inspector/editor window; it only runs after an iOS build is generated.
    /// </summary>
    public static class MOST_HapticFeedback_iOSPostProcessBuild
    {
        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS)
                return;

            string projectPath = PBXProject.GetPBXProjectPath(buildPath);

            PBXProject project = new PBXProject();
            project.ReadFromFile(projectPath);

#if UNITY_2019_3_OR_NEWER
            string unityFrameworkTarget = project.GetUnityFrameworkTargetGuid();
            string mainTarget = project.GetUnityMainTargetGuid();
#else
            string unityFrameworkTarget = project.TargetGuidByName("Unity-iPhone");
            string mainTarget = unityFrameworkTarget;
#endif

            AddFrameworks(project, unityFrameworkTarget);

            if (!string.IsNullOrEmpty(mainTarget) && mainTarget != unityFrameworkTarget)
                AddFrameworks(project, mainTarget);

            project.WriteToFile(projectPath);
        }

        private static void AddFrameworks(PBXProject project, string targetGuid)
        {
            if (string.IsNullOrEmpty(targetGuid))
                return;

            // CoreHaptics was introduced in iOS 13, so weak-link it for projects with lower deployment targets.
            project.AddFrameworkToProject(targetGuid, "CoreHaptics.framework", true);

            // These are usually already present in Unity iOS projects, but adding them is harmless and avoids
            // linker issues in stripped-down/custom Xcode exports.
            project.AddFrameworkToProject(targetGuid, "UIKit.framework", false);
            project.AddFrameworkToProject(targetGuid, "Foundation.framework", false);
        }
    }
}
#endif
