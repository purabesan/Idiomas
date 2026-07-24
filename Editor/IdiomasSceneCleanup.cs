using System.Collections.Generic;
using System.Text;
using BenderDios.Idiomas;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

/// <summary>
/// Removes scene components added by Idiomas without modifying translation assets.
/// Add future automatically-added component types to this cleanup entry point.
/// </summary>
public static class IdiomasSceneCleanup
{
    private const string MenuPath = "Tools/Idiomas/Cleanup Scene Components";
    private const int MaxListedTargets = 20;
    private static string S(string key) => IdiomasEditorStrings.Get(key);

    [MenuItem(MenuPath, false, 200)]
    public static void CleanupSceneComponents()
    {
        List<CanvasLocalizer> localizers = FindSceneCanvasLocalizers();
        if (localizers.Count == 0)
        {
            EditorUtility.DisplayDialog(
                S("cleanup_title"),
                S("cleanup_no_components"),
                S("ok"));
            return;
        }

        if (!EditorUtility.DisplayDialog(
                S("cleanup_title"),
                BuildConfirmationMessage(localizers),
                S("cleanup_remove"),
                S("cancel")))
        {
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Cleanup Idiomas Scene Components");

        RemoveCanvasLocalizerReferences(localizers);

        int removedBackingBehaviours = 0;
        for (int i = 0; i < localizers.Count; i++)
        {
            CanvasLocalizer localizer = localizers[i];
            if (localizer == null) continue;

            UdonBehaviour backingBehaviour = IdiomasEditorUtils.FindUdonBehaviourFor(localizer);
            if (backingBehaviour != null)
            {
                Undo.DestroyObjectImmediate(backingBehaviour);
                removedBackingBehaviours++;
            }

            Undo.DestroyObjectImmediate(localizer);
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log(
            $"[Idiomas] Scene cleanup removed {localizers.Count} CanvasLocalizer component(s) " +
            $"and {removedBackingBehaviours} backing UdonBehaviour component(s). " +
            "Translation files were not modified.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateCleanupSceneComponents()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static List<CanvasLocalizer> FindSceneCanvasLocalizers()
    {
        CanvasLocalizer[] found = Object.FindObjectsByType<CanvasLocalizer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        List<CanvasLocalizer> result = new List<CanvasLocalizer>();
        for (int i = 0; i < found.Length; i++)
        {
            CanvasLocalizer localizer = found[i];
            if (localizer == null ||
                EditorUtility.IsPersistent(localizer) ||
                !localizer.gameObject.scene.IsValid())
            {
                continue;
            }

            result.Add(localizer);
        }

        result.Sort((a, b) =>
            string.CompareOrdinal(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform)));
        return result;
    }

    private static string BuildConfirmationMessage(List<CanvasLocalizer> localizers)
    {
        StringBuilder message = new StringBuilder();
        message.AppendLine(string.Format(S("cleanup_confirm_header"), localizers.Count));
        message.AppendLine();

        int listedCount = Mathf.Min(localizers.Count, MaxListedTargets);
        for (int i = 0; i < listedCount; i++)
        {
            CanvasLocalizer localizer = localizers[i];
            message.Append("• ");
            message.Append(localizer.gameObject.scene.name);
            message.Append('/');
            message.AppendLine(GetHierarchyPath(localizer.transform));
        }

        if (localizers.Count > listedCount)
        {
            message.Append("• ");
            message.AppendLine(string.Format(
                S("cleanup_more_targets"),
                localizers.Count - listedCount));
        }

        message.AppendLine();
        message.AppendLine(S("cleanup_remove_details"));
        message.AppendLine(S("cleanup_preserve_details"));
        message.AppendLine();
        message.Append(S("cleanup_undo_hint"));
        return message.ToString();
    }

    private static void RemoveCanvasLocalizerReferences(List<CanvasLocalizer> localizers)
    {
        HashSet<CanvasLocalizer> targets = new HashSet<CanvasLocalizer>(localizers);
        LocalizationManager[] managers = Object.FindObjectsByType<LocalizationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int managerIndex = 0; managerIndex < managers.Length; managerIndex++)
        {
            LocalizationManager manager = managers[managerIndex];
            if (manager == null ||
                EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid())
            {
                continue;
            }

            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty references = serializedManager.FindProperty("canvasLocalizers");
            if (references == null || !references.isArray) continue;

            bool changed = false;
            for (int i = references.arraySize - 1; i >= 0; i--)
            {
                CanvasLocalizer reference =
                    references.GetArrayElementAtIndex(i).objectReferenceValue as CanvasLocalizer;
                if (reference != null && !targets.Contains(reference)) continue;

                if (!changed)
                {
                    Undo.RecordObject(manager, "Cleanup Idiomas Manager References");
                    changed = true;
                }

                DeleteArrayElement(references, i);
            }

            if (changed)
            {
                serializedManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }
        }
    }

    private static void DeleteArrayElement(SerializedProperty array, int index)
    {
        int previousSize = array.arraySize;
        array.DeleteArrayElementAtIndex(index);

        // Unity clears object references on the first call and removes the slot
        // on the second call.
        if (array.arraySize == previousSize)
            array.DeleteArrayElementAtIndex(index);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}
