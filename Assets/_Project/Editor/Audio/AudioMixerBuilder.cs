using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Arena.Editor.Audio
{
    /// <summary>
    /// Creates the game's audio mixer (brief A-TheoSfx): Master with the child groups <see cref="SfxGroupName"/> and
    /// <see cref="VoiceGroupName"/>. Unity has no public API for building a mixer, so the internal
    /// <c>UnityEditor.Audio.AudioMixerController</c> is called by reflection (CreateMixerControllerAtPath, CreateNewGroup,
    /// AddChildToParent). Idempotent: an existing mixer is kept, missing groups are added. Volumes are applied by the game's
    /// <c>SfxPlayer</c> (channel × setting), the groups are the routing point for later effects (ducking, limiter).
    /// If the internal API is missing in a future Unity, it logs a warning and returns null (the game then plays without a mixer).
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string SfxGroupName = "Sfx";
        public const string VoiceGroupName = "Voice";

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>The mixer at <paramref name="path"/> with both groups, or null if it cannot be built.</summary>
        public static AudioMixer EnsureMixer(string path)
        {
            try
            {
                AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
                Type controllerType = typeof(EditorWindow).Assembly.GetType("UnityEditor.Audio.AudioMixerController")
                                      ?? FindType("UnityEditor.Audio.AudioMixerController");
                if (controllerType == null) throw new MissingMemberException("UnityEditor.Audio.AudioMixerController");
                if (mixer == null)
                {
                    MethodInfo create = controllerType.GetMethod("CreateMixerControllerAtPath", Any);
                    if (create == null) throw new MissingMethodException("AudioMixerController.CreateMixerControllerAtPath");
                    mixer = (AudioMixer)create.Invoke(null, new object[] { path });
                }
                EnsureGroup(mixer, controllerType, SfxGroupName);
                EnsureGroup(mixer, controllerType, VoiceGroupName);
                EditorUtility.SetDirty(mixer);
                AssetDatabase.SaveAssets();
                return AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("AudioMixerBuilder: cannot build the mixer (" + e.GetBaseException().Message + ") — sounds play without it");
                return AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
            }
        }

        /// <summary>Group of the mixer with exactly this name, or null.</summary>
        public static AudioMixerGroup FindGroup(AudioMixer mixer, string name)
        {
            if (mixer == null) return null;
            AudioMixerGroup[] groups = mixer.FindMatchingGroups(name);
            for (int i = 0; i < groups.Length; i++)
                if (groups[i] != null && groups[i].name == name) return groups[i];
            return null;
        }

        private static void EnsureGroup(AudioMixer mixer, Type controllerType, string name)
        {
            if (FindGroup(mixer, name) != null) return;
            MethodInfo createGroup = controllerType.GetMethod("CreateNewGroup", Any);
            MethodInfo addChild = controllerType.GetMethod("AddChildToParent", Any);
            PropertyInfo master = controllerType.GetProperty("masterGroup", Any);
            if (createGroup == null || addChild == null || master == null)
                throw new MissingMethodException("AudioMixerController group API");
            object group = createGroup.Invoke(mixer, new object[] { name, false });
            addChild.Invoke(mixer, new[] { group, master.GetValue(mixer) });
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
