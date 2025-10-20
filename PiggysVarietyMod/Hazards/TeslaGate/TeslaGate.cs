using Unity.Netcode;
using UnityEngine;

namespace PiggysVarietyMod.Hazards.TeslaGate;

public class TeslaGate : NetworkBehaviour {
    private static readonly int _POWERED_ANIMATOR_HASH = Animator.StringToHash("Powered");
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public Animator animator;
    public AudioSource shutDownSound;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private void LateUpdate() {
        if (!animator.GetBool(_POWERED_ANIMATOR_HASH) || !RoundManager.Instance.powerOffPermanently) return;

        animator.SetBool(_POWERED_ANIMATOR_HASH, false);
        shutDownSound.Play();
    }
}