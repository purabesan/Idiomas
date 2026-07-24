using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace BenderDios.Idiomas
{
/// <summary>
/// Localizes configured interaction texts on UdonSharpBehaviour and VRCPickup.
/// This component is placed on a dedicated child object of LocalizationManager.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class InteractionLocalizer : UdonSharpBehaviour
{
    [SerializeField] private LocalizationManager manager;
    [SerializeField] private string baseLanguage = "en";

    [SerializeField] private UdonSharpBehaviour[] interactTargets = new UdonSharpBehaviour[0];
    [SerializeField] private string[] interactKeys = new string[0];
    [SerializeField] private bool[] interactEnabled = new bool[0];

    [SerializeField] private VRCPickup[] pickupInteractionTargets = new VRCPickup[0];
    [SerializeField] private string[] pickupInteractionKeys = new string[0];
    [SerializeField] private bool[] pickupInteractionEnabled = new bool[0];

    [SerializeField] private VRCPickup[] pickupUseTargets = new VRCPickup[0];
    [SerializeField] private string[] pickupUseKeys = new string[0];
    [SerializeField] private bool[] pickupUseEnabled = new bool[0];

    public string GetBaseLanguage()
    {
        return baseLanguage;
    }

    private void Start()
    {
        if (!Utilities.IsValid(manager))
        {
            Debug.LogWarning("[InteractionLocalizer] No se asigno LocalizationManager.");
            return;
        }

        manager.RegisterInteractionLocalizer(this);
    }

    /// <summary>
    /// Applies the active language to all registered interaction texts.
    /// </summary>
    public void UpdateAllTexts()
    {
        if (!Utilities.IsValid(manager)) return;

        int interactCount = Mathf.Min(interactTargets.Length, interactKeys.Length);
        for (int i = 0; i < interactCount; i++)
        {
            if (i < interactEnabled.Length && !interactEnabled[i]) continue;
            if (Utilities.IsValid(interactTargets[i]) && !string.IsNullOrEmpty(interactKeys[i]))
                interactTargets[i].InteractionText = manager.GetValue(interactKeys[i]);
        }

        int pickupInteractionCount = Mathf.Min(
            pickupInteractionTargets.Length, pickupInteractionKeys.Length);
        for (int i = 0; i < pickupInteractionCount; i++)
        {
            if (i < pickupInteractionEnabled.Length &&
                !pickupInteractionEnabled[i]) continue;
            if (Utilities.IsValid(pickupInteractionTargets[i]) &&
                !string.IsNullOrEmpty(pickupInteractionKeys[i]))
            {
                pickupInteractionTargets[i].InteractionText =
                    manager.GetValue(pickupInteractionKeys[i]);
            }
        }

        int pickupUseCount = Mathf.Min(pickupUseTargets.Length, pickupUseKeys.Length);
        for (int i = 0; i < pickupUseCount; i++)
        {
            if (i < pickupUseEnabled.Length && !pickupUseEnabled[i]) continue;
            if (Utilities.IsValid(pickupUseTargets[i]) && !string.IsNullOrEmpty(pickupUseKeys[i]))
                pickupUseTargets[i].UseText = manager.GetValue(pickupUseKeys[i]);
        }
    }
}
}
