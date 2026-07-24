using System.Collections.Generic;
using System.Text;
using BenderDios.Idiomas;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

/// <summary>
/// Removes scene components added by Idiomas without modifying
/// translation files.
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
        List<InteractionLocalizer> interactionLocalizers =
            FindConfiguredInteractionLocalizers();
        int interactionTextCount =
            CountInteractionEntries(interactionLocalizers);
        if (localizers.Count == 0 && interactionLocalizers.Count == 0)
        {
            EditorUtility.DisplayDialog(
                S("cleanup_title"),
                S("cleanup_no_components"),
                S("ok"));
            return;
        }

        if (!EditorUtility.DisplayDialog(
                S("cleanup_title"),
                BuildConfirmationMessage(
                    localizers,
                    interactionLocalizers,
                    interactionTextCount),
                S("cleanup_remove"),
                S("cancel")))
        {
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Cleanup Idiomas Scene Components");

        RemoveCanvasLocalizerReferences(localizers);
        ClearInteractionLocalizers(interactionLocalizers);

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
            $"Cleared {interactionTextCount} Interaction Text entries from " +
            $"{interactionLocalizers.Count} InteractionLocalizer component(s). " +
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

    private static List<InteractionLocalizer> FindConfiguredInteractionLocalizers()
    {
        InteractionLocalizer[] found =
            Object.FindObjectsByType<InteractionLocalizer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        List<InteractionLocalizer> result =
            new List<InteractionLocalizer>();
        for (int i = 0; i < found.Length; i++)
        {
            InteractionLocalizer localizer = found[i];
            if (localizer == null ||
                EditorUtility.IsPersistent(localizer) ||
                !localizer.gameObject.scene.IsValid() ||
                !HasInteractionEntries(localizer))
            {
                continue;
            }
            result.Add(localizer);
        }
        result.Sort((a, b) => string.CompareOrdinal(
            GetHierarchyPath(a.transform),
            GetHierarchyPath(b.transform)));
        return result;
    }

    private static bool HasInteractionEntries(InteractionLocalizer localizer)
    {
        SerializedObject serializedLocalizer =
            new SerializedObject(localizer);
        string[] arrays =
        {
            "interactTargets", "interactKeys", "interactEnabled",
            "pickupInteractionTargets", "pickupInteractionKeys",
            "pickupInteractionEnabled", "pickupUseTargets",
            "pickupUseKeys", "pickupUseEnabled"
        };
        for (int i = 0; i < arrays.Length; i++)
        {
            SerializedProperty property =
                serializedLocalizer.FindProperty(arrays[i]);
            if (property != null && property.arraySize > 0) return true;
        }
        return false;
    }

    private static int CountInteractionEntries(
        List<InteractionLocalizer> localizers)
    {
        int total = 0;
        for (int i = 0; i < localizers.Count; i++)
        {
            SerializedObject serializedLocalizer =
                new SerializedObject(localizers[i]);
            total += CountPairedEntries(
                serializedLocalizer.FindProperty("interactTargets"),
                serializedLocalizer.FindProperty("interactKeys"));
            total += CountPairedEntries(
                serializedLocalizer.FindProperty(
                    "pickupInteractionTargets"),
                serializedLocalizer.FindProperty(
                    "pickupInteractionKeys"));
            total += CountPairedEntries(
                serializedLocalizer.FindProperty("pickupUseTargets"),
                serializedLocalizer.FindProperty("pickupUseKeys"));
        }
        return total;
    }

    private static int CountPairedEntries(
        SerializedProperty targets, SerializedProperty keys)
    {
        if (targets == null || keys == null) return 0;
        return Mathf.Min(targets.arraySize, keys.arraySize);
    }

    private static string BuildConfirmationMessage(
        List<CanvasLocalizer> localizers,
        List<InteractionLocalizer> interactionLocalizers,
        int interactionTextCount)
    {
        StringBuilder message = new StringBuilder();
        message.AppendLine(string.Format(
            S("cleanup_confirm_header"),
            localizers.Count,
            interactionTextCount));
        message.AppendLine();

        int listedCount = 0;
        for (int i = 0;
            i < localizers.Count && listedCount < MaxListedTargets;
            i++, listedCount++)
        {
            CanvasLocalizer localizer = localizers[i];
            message.Append("• ");
            message.Append(localizer.gameObject.scene.name);
            message.Append('/');
            message.AppendLine(GetHierarchyPath(localizer.transform));
        }

        for (int i = 0;
            i < interactionLocalizers.Count &&
                listedCount < MaxListedTargets;
            i++, listedCount++)
        {
            InteractionLocalizer localizer = interactionLocalizers[i];
            message.Append("• ");
            message.Append(localizer.gameObject.scene.name);
            message.Append('/');
            message.Append(GetHierarchyPath(localizer.transform));
            message.AppendLine(" (InteractionLocalizer)");
        }

        int totalCount = localizers.Count + interactionLocalizers.Count;
        if (totalCount > listedCount)
        {
            message.Append("• ");
            message.AppendLine(string.Format(
                S("cleanup_more_targets"),
                totalCount - listedCount));
        }

        message.AppendLine();
        message.AppendLine(S("cleanup_remove_details"));
        message.AppendLine(S("cleanup_preserve_details"));
        message.AppendLine();
        message.Append(S("cleanup_undo_hint"));
        return message.ToString();
    }

    private static void ClearInteractionLocalizers(
        List<InteractionLocalizer> localizers)
    {
        string[] arrays =
        {
            "interactTargets", "interactKeys", "interactEnabled",
            "pickupInteractionTargets", "pickupInteractionKeys",
            "pickupInteractionEnabled", "pickupUseTargets",
            "pickupUseKeys", "pickupUseEnabled"
        };
        for (int i = 0; i < localizers.Count; i++)
        {
            InteractionLocalizer localizer = localizers[i];
            if (localizer == null) continue;

            Undo.RecordObject(
                localizer, "Clear Idiomas Interaction Text Entries");
            SerializedObject serializedLocalizer =
                new SerializedObject(localizer);
            for (int j = 0; j < arrays.Length; j++)
            {
                SerializedProperty property =
                    serializedLocalizer.FindProperty(arrays[j]);
                if (property != null) property.ClearArray();
            }
            serializedLocalizer.ApplyModifiedProperties();
            EditorUtility.SetDirty(localizer);
        }
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
