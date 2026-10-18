using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Arena.Editor.Setup
{
    /// <summary>
    /// Installs/removes project packages via Package Manager (D-023). Batchmode entry point:
    /// -executeMethod Arena.Editor.Setup.PackageInstaller.Install (without -quit; exits itself).
    /// </summary>
    public static class PackageInstaller
    {
        private static readonly string[] PackagesToAdd =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.test-framework",
            "com.unity.ugui", // includes TextMeshPro in Unity 6
            "com.unity.cinemachine",
        };

        private static readonly string[] PackagesToRemoveIfPresent =
        {
            "com.unity.collab-proxy",
            "com.unity.visualscripting",
            "com.unity.multiplayer.center",
            "com.unity.timeline",
        };

        private const double TimeoutSeconds = 900.0;

        private static ListRequest _listRequest;
        private static AddAndRemoveRequest _addRemoveRequest;
        private static double _startTime;

        public static void Install()
        {
            Debug.Log("ARENA_PACKAGES_START");
            _startTime = EditorApplication.timeSinceStartup;
            _listRequest = Client.List(true, false);
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startTime > TimeoutSeconds)
                {
                    Finish(false, "timeout");
                    return;
                }

                if (_listRequest != null)
                {
                    if (!_listRequest.IsCompleted) return;
                    if (_listRequest.Status != StatusCode.Success)
                    {
                        Finish(false, "list failed: " + _listRequest.Error?.message);
                        return;
                    }

                    var installed = new HashSet<string>();
                    foreach (var info in _listRequest.Result) installed.Add(info.name);
                    _listRequest = null;

                    var remove = new List<string>();
                    foreach (var name in PackagesToRemoveIfPresent)
                        if (installed.Contains(name)) remove.Add(name);

                    Debug.Log("ARENA_PACKAGES add=" + string.Join(",", PackagesToAdd) +
                              " remove=" + string.Join(",", remove));
                    _addRemoveRequest = Client.AddAndRemove(PackagesToAdd, remove.ToArray());
                    return;
                }

                if (_addRemoveRequest != null)
                {
                    if (!_addRemoveRequest.IsCompleted) return;
                    if (_addRemoveRequest.Status != StatusCode.Success)
                    {
                        Finish(false, "add/remove failed: " + _addRemoveRequest.Error?.message);
                        return;
                    }

                    foreach (var info in _addRemoveRequest.Result)
                        Debug.Log("ARENA_PACKAGE " + info.name + "@" + info.version);
                    Finish(true, "ok");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(false, "exception");
            }
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Poll;
            _listRequest = null;
            _addRemoveRequest = null;
            Debug.Log("ARENA_PACKAGES_RESULT " + (success ? "Succeeded" : "Failed") + " " + message);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
