using Unity.Netcode;
using UnityEngine;

namespace PiggysVarietyMod.Hazards.TeslaGate;

public class TeslaGate : NetworkBehaviour {
    private static readonly int _POWERED_ANIMATOR_HASH = Animator.StringToHash("Powered");
    private static readonly int _SPEED_ANIMATOR_HASH = Animator.StringToHash("Speed");
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public Animator animator;
    public AudioSource shutDownSound;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    private float _randomSpeed = -1F;
    private bool _initialized;

    #region Overrides of NetworkBehaviour

    public override void OnNetworkSpawn() {
        if (!IsHost && !IsServer) return;
        if (!_initialized) _randomSpeed = Random.Range(.96F, 1.04F);
        UseRandomOffsetClientRpc(_randomSpeed);
    }

    #endregion

    private void LateUpdate() {
        if (!animator.GetBool(_POWERED_ANIMATOR_HASH) || !RoundManager.Instance.powerOffPermanently) return;

        animator.SetBool(_POWERED_ANIMATOR_HASH, false);
        shutDownSound.Play();
    }

    [ClientRpc]
    public void UseRandomOffsetClientRpc(float randomSpeed) {
        if (_initialized) return;

        _randomSpeed = randomSpeed;
        animator.SetFloat(_SPEED_ANIMATOR_HASH, _randomSpeed);
        _initialized = true;
    }
}