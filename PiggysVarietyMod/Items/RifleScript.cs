using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PiggysVarietyMod.Items;

internal class RifleScript : GrabbableObject {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    // ---- CONFIG ----
    public int gunCompatibleAmmoID = 485;
    public int clipSize = 30;

    public float fireRate = 0.085f;
    public bool automatic = true;

    private const string _BASE_AMMO_TIP = "Reload : [R]";
    private const string _BASE_MODE_TIP = "Switch Fire Mode [C]";

    // ---- BLOOM ----
    public float baseSpread = 0.01f;
    public float bloomIncrease = 0.005f;
    public float maxBloom = 0.04f;
    private float _currentBloom;

    // ---- RECOIL SYSTEM ----
    public float recoilVertical = 2.0f;
    public float recoilHorizontal = 0.6f;
    public float recoilReturnSpeed = 6f;

    private float _currentRecoilX;
    private float _currentRecoilY;

    // ---- STATE ----
    private float _fireCooldown;
    public int ammosLoaded;
    public int ammoSlotToUse;
    public bool isReloading;
    public bool isInspecting;
    public bool isSwitchingFireMode;
    public bool cantFire;

    // ---- PLAYER ANIMATIONS ----
    public Animator gunAnimator;
    public RuntimeAnimatorController rifleLocalAnimator;
    public RuntimeAnimatorController rifleRemoteAnimator;

    private static readonly Dictionary<ulong, RuntimeAnimatorController> _SAVED_ANIMATORS = new();

    private AnimatorStateInfo _savedState;
    private float _savedNormalizedTime;
    private bool _savedCrouching;
    private bool _savedWalking;
    private bool _savedJumping;
    private bool _savedSprinting;
    private bool _animatorReplaced = false;

    // ---- VFX ----
    public ParticleSystem shootParticle;

    // ---- SFX ----
    public AudioSource audioSource;

    // ReSharper disable InconsistentNaming
    public AudioClip SFX_FireM4;
    public AudioClip SFX_ReloadM4;
    public AudioClip SFX_TriggerM4;
    public AudioClip SFX_SwitchFireModeM4;
    // ReSharper restore InconsistentNaming
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    // ---------------------------------------------------------------------------------------
    private static void DebugLog(string msg) {
#if DEBUG
        Debug.Log($"[PiggyVarietyMod:RifleScript] {msg}");
#endif
    }

    public override void Start() {
        ammosLoaded = clipSize;
        _currentBloom = baseSpread;

        base.Start();
    }

    // ---------------------------------------------------------------------------------------
    public override void Update() {
        base.Update();
        if (playerHeldBy == null) return;
        if (playerHeldBy != GameNetworkManager.Instance.localPlayerController || isPocketed) return;

        _fireCooldown -= Time.deltaTime;

        HandleInput();
        UpdateRecoilCamera();
        DisplayRifleTooltip();
    }

    // ---------------------------------------------------------------------------------------
    public override void GrabItem() {
        EnableRifleAnimator();
        base.GrabItem();
    }

    public override void EquipItem() {
        EnableRifleAnimator();
        base.EquipItem();
    }

    public override void PocketItem() {
        DisableRifleAnimator();
        base.PocketItem();
    }

    public override void DiscardItem() {
        DisableRifleAnimator();
        base.DiscardItem();
    }

    // ---------------------------------------------------------------------------------------
    private void HandleInput() {
        if (ShouldBlockInput()) return;

        var input = PiggysVarietyMod.INPUT_ACTIONS_INSTANCE;
        var shootPressed = input.FireKey.IsPressed();
        var shootDown = input.FireKey.WasPressedThisFrame();

        if (shootDown && ammosLoaded <= 0) {
            audioSource.PlayOneShot(SFX_TriggerM4);
        }

        if (automatic) {
            if (shootPressed) {
                TryShoot();
            }
        } else {
            if (shootDown) {
                TryShoot();
            }
        }

        if (input.RifleReloadKey.WasPressedThisFrame()) {
            TryReload();
        }

        if (input.SwitchFireModeKey.WasPressedThisFrame()) {
            TrySwitchFireMode();
        }

        if (input.InspectKey.WasPressedThisFrame()) {
            TryInspect();
        }
    }

    private bool ShouldBlockInput() {
        if (playerHeldBy == null) return true;
        if (playerHeldBy.isGrabbingObjectAnimation) return true;
        if (playerHeldBy.isTypingChat) return true;
        if (playerHeldBy.inTerminalMenu) return true;
        if (playerHeldBy.inSpecialInteractAnimation) return true;
        if (playerHeldBy.hoveringOverTrigger != null) return true;
        if (playerHeldBy.quickMenuManager.isMenuOpen) return true;
        if (playerHeldBy.inSpecialMenu) return true;

        return false;
    }

    // ---------------------------------------------------------------------------------------
    private void TryShoot() {
        if (playerHeldBy == null) return;
        if (cantFire) return;
        if (isReloading || isInspecting || isSwitchingFireMode) return;
        if (_fireCooldown > 0) return;

        if (ammosLoaded <= 0) return;

        _fireCooldown = fireRate;
        cantFire = true;

        StartCoroutine(ResetCantFireCoroutine(0.1f));

        var gunPosition = playerHeldBy.gameplayCamera.transform.position;
        var gunForward = GetShootDirection();

        ShootServerRpc(gunPosition, gunForward);
        ApplyCameraRecoil();
        IncreaseBloom();
    }

    private IEnumerator ResetCantFireCoroutine(float t) {
        yield return new WaitForSeconds(t);
        cantFire = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ShootServerRpc(Vector3 gunPosition, Vector3 gunForward) {
        ShootClientRpc(gunPosition, gunForward);
    }

    [ClientRpc]
    public void ShootClientRpc(Vector3 gunPosition, Vector3 gunForward) {
        Shoot(gunPosition, gunForward);
    }

    public void Shoot(Vector3 gunPosition, Vector3 gunForward) {
        if (playerHeldBy == null) return;

        var maxDist = 80f;
        shootParticle.Play(withChildren: true);

        playerHeldBy.playerBodyAnimator.Play("ShootM4", 4, 0f);
        audioSource.PlayOneShot(SFX_FireM4);

        ammosLoaded--;

        if (playerHeldBy != GameNetworkManager.Instance.localPlayerController) return;

        var hits = Physics.RaycastAll(
            gunPosition,
            gunForward,
            maxDist,
            StartOfRound.Instance.collidersRoomMaskDefaultAndPlayers | 524288,
            QueryTriggerInteraction.Collide);

        if (hits.Length == 0) return;

        Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));

        foreach (var hit in hits) {
            var obj = hit.collider.gameObject;

            if (obj.TryGetComponent(out PlayerControllerB player) && player == playerHeldBy) continue;

            if (obj.TryGetComponent<IHittable>(out var hittable)) {
                if (hittable is EnemyAICollisionDetect detectEnemy) {
                    DebugLog("Hitting Enemy = " + detectEnemy);
                    detectEnemy.mainScript.HitEnemyOnLocalClient(1, gunForward, playerHeldBy, true);
                } else if (hittable is PlayerControllerB detectPlayer) {
                    DebugLog("Hitting Player = " + detectPlayer);
                    hittable.Hit(1, gunForward, playerHeldBy, true);
                } else {
                    hittable.Hit(1, gunForward, playerHeldBy, true);
                }

                return;
            } else {
                var enemy = hit.collider.GetComponentInParent<EnemyAI>();
                if (enemy != null) {
                    enemy.HitEnemyOnLocalClient(1, gunForward, playerHeldBy, true, 1);
                    return;
                }
            }

            return;
        }
    }

    // ---------------------------------------------------------------------------------------
    private void TryReload() {
        if (playerHeldBy == null) return;
        if (isReloading || isInspecting || isSwitchingFireMode) return;
        if (ammosLoaded >= clipSize) return;

        var slot = FindAmmoInInventory();
        if (slot == -1) return;

        ammoSlotToUse = slot;
        isReloading = true;
        cantFire = true;

        ReloadServerRpc(ammoSlotToUse);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReloadServerRpc(int ammoSlotIndex) {
        ReloadClientRpc(ammoSlotIndex);
    }

    [ClientRpc]
    public void ReloadClientRpc(int ammoSlotIndex) {
        StartReload(ammoSlotIndex);
    }

    public void StartReload(int ammoSlotIndex) {
        ammoSlotToUse = ammoSlotIndex;
        isReloading = true;
        cantFire = true;

        playerHeldBy.playerBodyAnimator.Play("ReloadM4", 4, 0f);
        gunAnimator.Play("Reload", 0, 0f);

        audioSource.PlayOneShot(SFX_ReloadM4);

        playerHeldBy.DestroyItemInSlotAndSync(ammoSlotToUse);

        ammoSlotToUse = -1;

        var clipLen = GetClipLength("ReloadM4");
        StartCoroutine(FinishReloadCoroutine(clipLen));
    }

    private IEnumerator FinishReloadCoroutine(float t) {
        yield return new WaitForSeconds(t);

        ammosLoaded = clipSize;
        isReloading = false;
        cantFire = false;

        ResetBloom();
    }

    private int FindAmmoInInventory() {
        for (var i = 0; i < playerHeldBy.ItemSlots.Length; i++) {
            if (playerHeldBy.ItemSlots[i] != null) {
                var gunAmmo = playerHeldBy.ItemSlots[i].GetComponent<GunAmmo>();
                if (gunAmmo != null && gunAmmo.ammoType == gunCompatibleAmmoID) {
                    return i;
                }
            }
        }

        return -1;
    }

    // ---------------------------------------------------------------------------------------
    private void TrySwitchFireMode() {
        if (playerHeldBy == null) return;
        if (isReloading || isInspecting || isSwitchingFireMode) return;

        isSwitchingFireMode = true;
        cantFire = true;

        SwitchFireModeServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SwitchFireModeServerRpc() {
        SwitchFireModeClientRpc();
    }

    [ClientRpc]
    private void SwitchFireModeClientRpc() {
        StartSwitchFireMode();
    }

    private void StartSwitchFireMode() {
        isSwitchingFireMode = true;
        cantFire = true;

        playerHeldBy.playerBodyAnimator.Play("SwitchFireModeM4", 4, 0f);

        audioSource.PlayOneShot(SFX_SwitchFireModeM4);

        var clipLen = GetClipLength("SwitchFireModeM4") / 2;
        StartCoroutine(FinishSwitchFireModeCoroutine(clipLen));
    }

    private IEnumerator FinishSwitchFireModeCoroutine(float t) {
        yield return new WaitForSeconds(t);

        automatic = !automatic;

        cantFire = false;
        isSwitchingFireMode = false;
    }

    // ---------------------------------------------------------------------------------------
    private void TryInspect() {
        if (playerHeldBy == null) return;
        if (isReloading || isInspecting || isSwitchingFireMode) return;

        isInspecting = true;
        cantFire = true;

        InspectServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void InspectServerRpc() {
        InspectClientRpc();
    }

    [ClientRpc]
    private void InspectClientRpc() {
        StartInspect();
    }

    private void StartInspect() {
        isInspecting = true;
        cantFire = true;

        playerHeldBy.playerBodyAnimator.Play("InspectM4", 4, 0f);

        var clipLen = GetClipLength("InspectM4");
        StartCoroutine(EndInspectCoroutine(clipLen));
    }

    private IEnumerator EndInspectCoroutine(float t) {
        yield return new WaitForSeconds(t);

        isInspecting = false;
        cantFire = false;
    }

    // ---------------------------------------------------------------------------------------
    private Vector3 GetShootDirection() {
        var cam = playerHeldBy.gameplayCamera;

        var dir = cam.transform.forward;

        dir.x += UnityEngine.Random.Range(-_currentBloom, _currentBloom);
        dir.y += UnityEngine.Random.Range(-_currentBloom, _currentBloom);

        return dir.normalized;
    }

    private void IncreaseBloom() {
        _currentBloom = Mathf.Clamp(_currentBloom + bloomIncrease, baseSpread, maxBloom);
    }

    private void ResetBloom() {
        _currentBloom = baseSpread;
    }

    private void ApplyCameraRecoil() {
        if (playerHeldBy == null) return;

        _currentRecoilY += recoilVertical;
        _currentRecoilX += UnityEngine.Random.Range(-recoilHorizontal, recoilHorizontal);

        HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
    }

    private void UpdateRecoilCamera() {
        if (playerHeldBy == null) return;

        playerHeldBy.cameraUp -= _currentRecoilY * Time.deltaTime * 8f;

        playerHeldBy.cameraUp = Mathf.Clamp(playerHeldBy.cameraUp, -80f, 80f);

        playerHeldBy.thisPlayerBody.Rotate(Vector3.up * (_currentRecoilX * Time.deltaTime * 8f));

        _currentRecoilX = Mathf.Lerp(_currentRecoilX, 0, Time.deltaTime * recoilReturnSpeed);
        _currentRecoilY = Mathf.Lerp(_currentRecoilY, 0, Time.deltaTime * recoilReturnSpeed);
    }

    // ---------------------------------------------------------------------------------------
    private void DisplayRifleTooltip() {
        var currentMode = automatic? "Auto" : "Semi";

        itemProperties.toolTips[2] = $"{_BASE_AMMO_TIP} [{ammosLoaded}]";
        itemProperties.toolTips[3] = $"{_BASE_MODE_TIP} [{currentMode}]";

        HUDManager.Instance.ChangeControlTipMultiple(itemProperties.toolTips, holdingItem: false, itemProperties);
    }

    // ---------------------------------------------------------------------------------------
    // ANIMATOR CONTROLLER OVERRIDE, (DO NOT TOUCH!)
    // ---------------------------------------------------------------------------------------
    private void SaveAnimatorState(Animator anim) {
        _savedState = anim.GetCurrentAnimatorStateInfo(0);
        _savedNormalizedTime = _savedState.normalizedTime;

        _savedCrouching = anim.GetBool("crouching");
        _savedWalking = anim.GetBool("Walking");
        _savedJumping = anim.GetBool("Jumping");
        _savedSprinting = anim.GetBool("Sprinting");
    }

    private void RestoreAnimatorState(Animator anim) {
        anim.Play(_savedState.fullPathHash, 0, _savedNormalizedTime);

        anim.SetBool("crouching", _savedCrouching);
        anim.SetBool("Walking", _savedWalking);
        anim.SetBool("Jumping", _savedJumping);
        anim.SetBool("Sprinting", _savedSprinting);
        playerHeldBy.playerBodyAnimator.Play("HoldM4", 4, 0f);
    }

    private void EnableRifleAnimator() {
        if (_animatorReplaced) return;
        if (playerHeldBy == null) return;

        if (!_SAVED_ANIMATORS.ContainsKey(playerHeldBy.playerClientId)) {
            _SAVED_ANIMATORS[playerHeldBy.playerClientId] = playerHeldBy.playerBodyAnimator.runtimeAnimatorController;
        }

        SaveAnimatorState(playerHeldBy.playerBodyAnimator);

        if (playerHeldBy == GameNetworkManager.Instance.localPlayerController) {
            playerHeldBy.playerBodyAnimator.runtimeAnimatorController = rifleLocalAnimator;
        } else {
            playerHeldBy.playerBodyAnimator.runtimeAnimatorController = rifleRemoteAnimator;
        }

        playerHeldBy.playerBodyAnimator.Rebind();
        playerHeldBy.playerBodyAnimator.Update(0f);

        RestoreAnimatorState(playerHeldBy.playerBodyAnimator);
        _animatorReplaced = true;
    }

    private void DisableRifleAnimator() {
        if (!_animatorReplaced) return;
        if (playerHeldBy == null) return;

        SaveAnimatorState(playerHeldBy.playerBodyAnimator);

        if (_SAVED_ANIMATORS.TryGetValue(playerHeldBy.playerClientId, out var original)) {
            playerHeldBy.playerBodyAnimator.runtimeAnimatorController = original;
            _SAVED_ANIMATORS.Remove(playerHeldBy.playerClientId);
        }

        playerHeldBy.playerBodyAnimator.Rebind();
        playerHeldBy.playerBodyAnimator.Update(0f);

        RestoreAnimatorState(playerHeldBy.playerBodyAnimator);

        _animatorReplaced = false;
    }

    private float GetClipLength(string clipName) {
        if (playerHeldBy == null || playerHeldBy.playerBodyAnimator == null ||
            playerHeldBy.playerBodyAnimator.runtimeAnimatorController == null) {
            return -1;
        }

        foreach (var clip in playerHeldBy.playerBodyAnimator.runtimeAnimatorController.animationClips) {
            if (clip.name == clipName) {
                return clip.length;
            }
        }

        return -1;
    }
}