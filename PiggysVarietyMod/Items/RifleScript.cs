using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PiggysVarietyMod.Items
{
    internal class RifleScript : GrabbableObject
    {
        // ---- CONFIG ----
        public int gunCompatibleAmmoID = 485;
        public int clipSize = 30;

        public float fireRate = 0.085f;
        public bool automatic = true;

        private string baseAmmoTip = "Reload : [R]";
        private string baseModeTip = "Switch Fire Mode [C]";

        // ---- BLOOM ----
        public float baseSpread = 0.01f;
        public float bloomIncrease = 0.005f;
        public float maxBloom = 0.04f;
        private float currentBloom;

        // ---- RECOIL SYSTEM ----
        public float recoilVertical = 2.0f;
        public float recoilHorizontal = 0.6f;
        public float recoilReturnSpeed = 6f;

        private float currentRecoilX;
        private float currentRecoilY;

        // ---- STATE ----
        private float fireCooldown;
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

        private static Dictionary<ulong, RuntimeAnimatorController> savedAnimators = new();

        private AnimatorStateInfo savedState;
        private float savedNormalizedTime;
        private bool savedCrouching;
        private bool savedWalking;
        private bool savedJumping;
        private bool savedSprinting;
        private bool animatorReplaced = false;

        // ---- VFX ----
        public ParticleSystem shootParticle;

        // ---- SFX ----
        private AudioSource audio;

        public AudioClip SFX_FireM4;
        public AudioClip SFX_ReloadM4;
        public AudioClip SFX_TriggerM4;
        public AudioClip SFX_SwitchFireModeM4;

        // ---------------------------------------------------------------------------------------
        private void DebugLog(string msg)
        {
            Debug.Log($"[RifleScript] {msg}");
        }

        public override void Start()
        {
            grabbable = true;
            grabbableToEnemies = false;
            itemProperties.canBeInspected = false;
            isInFactory = true;

            mainObjectRenderer = GetComponent<MeshRenderer>();
            audio = GetComponent<AudioSource>();
            gunAnimator = GetComponent<Animator>();
            shootParticle = transform.Find("GunShootRayPoint/BulletParticle").GetComponent<ParticleSystem>();

            ammosLoaded = clipSize;
            currentBloom = baseSpread;

            base.Start();
        }

        // ---------------------------------------------------------------------------------------
        public override void Update()
        {
            base.Update();
            if (playerHeldBy == null) return;
            if (playerHeldBy != GameNetworkManager.Instance.localPlayerController || isPocketed) return;

            fireCooldown -= Time.deltaTime;

            HandleInput();
            UpdateRecoilCamera();
            DisplayRifleTooltip();
        }

        // ---------------------------------------------------------------------------------------
        public override void GrabItem()
        {
            EnableRifleAnimator();
            base.GrabItem();
        }

        public override void EquipItem()
        {
            EnableRifleAnimator();
            base.EquipItem();
        }

        public override void PocketItem()
        {
            DisableRifleAnimator();
            base.PocketItem();
        }

        public override void DiscardItem()
        {
            DisableRifleAnimator();
            base.DiscardItem();
        }

        // ---------------------------------------------------------------------------------------
        private void HandleInput()
        {
            if (ShouldBlockInput()) return;

            var input = PiggysVarietyMod.InputActionsInstance;
            bool shootPressed = input.FireKey.IsPressed();
            bool shootDown = input.FireKey.WasPressedThisFrame();

            if (shootDown && ammosLoaded <= 0)
            {
                audio.PlayOneShot(SFX_TriggerM4);
            }

            if (automatic)
            {
                if (shootPressed)
                {
                    TryShoot();
                }
            }
            else
            {
                if (shootDown)
                {
                    TryShoot();
                }
            }

            if (input.RifleReloadKey.WasPressedThisFrame())
            {
                TryReload();
            }

            if (input.SwitchFireModeKey.WasPressedThisFrame())
            {
                TrySwitchFireMode();
            }

            if (input.InspectKey.WasPressedThisFrame())
            {
                TryInspect();
            }
        }

        private bool ShouldBlockInput()
        {
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
        private void TryShoot()
        {
            if (playerHeldBy == null) return;
            if (cantFire) return;
            if (isReloading || isInspecting || isSwitchingFireMode) return;
            if (fireCooldown > 0) return;

            if (ammosLoaded <= 0) return;

            fireCooldown = fireRate;
            cantFire = true;

            StartCoroutine(ResetCantFireCoroutine(0.1f));

            Vector3 gunPosition = playerHeldBy.gameplayCamera.transform.position;
            Vector3 gunForward = GetShootDirection();

            ShootServerRpc(gunPosition, gunForward);
            ApplyCameraRecoil();
            IncreaseBloom();
        }

        private IEnumerator ResetCantFireCoroutine(float t)
        {
            yield return new WaitForSeconds(t);
            cantFire = false;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ShootServerRpc(Vector3 gunPosition, Vector3 gunForward)
        {
            ShootClientRpc(gunPosition, gunForward);
        }

        [ClientRpc]
        public void ShootClientRpc(Vector3 gunPosition, Vector3 gunForward)
        {
            Shoot(gunPosition, gunForward);
        }

        public void Shoot(Vector3 gunPosition, Vector3 gunForward)
        {
            if (playerHeldBy == null) return;

            float maxDist = 80f;
            shootParticle.Play(withChildren: true);

            playerHeldBy.playerBodyAnimator.Play("ShootM4", 4, 0f);
            audio.PlayOneShot(SFX_FireM4);

            ammosLoaded--;

            if (playerHeldBy != GameNetworkManager.Instance.localPlayerController) return;
  
            RaycastHit[] hits = Physics.RaycastAll(
                gunPosition,
                gunForward,
                maxDist,
                StartOfRound.Instance.collidersRoomMaskDefaultAndPlayers | 524288,
                QueryTriggerInteraction.Collide);

            if (hits.Length == 0) return;

            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));

            foreach (var hit in hits)
            {
                GameObject obj = hit.collider.gameObject;

                if (obj.TryGetComponent(out PlayerControllerB player) && player == playerHeldBy) continue;

                if (obj.TryGetComponent<IHittable>(out var hittable))
                {
                    if (hittable is EnemyAICollisionDetect detectEnemy)
                    {
                        DebugLog("Hitting Enemy = " + detectEnemy);
                        detectEnemy.mainScript.HitEnemyOnLocalClient(1, gunForward, playerHeldBy, true);
                    }
                    else if (hittable is PlayerControllerB detectPlayer)
                    {
                        DebugLog("Hitting Player = " + detectPlayer);
                        hittable.Hit(1, gunForward, playerHeldBy, true);
                    }
                    else
                    {
                        hittable.Hit(1, gunForward, playerHeldBy, true);
                    }
                    return;
                }
                else 
                {
                    EnemyAI enemy = hit.collider.GetComponentInParent<EnemyAI>();
                    if (enemy != null)
                    {
                        enemy.HitEnemyOnLocalClient(1, gunForward, playerHeldBy, true, 1);
                        return;
                    }
                }
                return;
            }
        }

        // ---------------------------------------------------------------------------------------
        private void TryReload()
        {
            if (playerHeldBy == null) return;
            if (isReloading || isInspecting || isSwitchingFireMode) return;
            if (ammosLoaded >= clipSize) return;

            int slot = FindAmmoInInventory();
            if (slot == -1) return;

            ammoSlotToUse = slot;
            isReloading = true;
            cantFire = true;

            ReloadServerRpc(ammoSlotToUse);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReloadServerRpc(int ammoSlotIndex)
        {
            ReloadClientRpc(ammoSlotIndex);
        }

        [ClientRpc]
        public void ReloadClientRpc(int ammoSlotIndex)
        {
            StartReload(ammoSlotIndex);
        }

        public void StartReload(int ammoSlotIndex)
        {
            ammoSlotToUse = ammoSlotIndex;
            isReloading = true;
            cantFire = true;

            playerHeldBy.playerBodyAnimator.Play("ReloadM4", 4, 0f);
            gunAnimator.Play("Reload", 0 , 0f);

            audio.PlayOneShot(SFX_ReloadM4);

            playerHeldBy.DestroyItemInSlotAndSync(ammoSlotToUse);

            ammoSlotToUse = -1;

            float clipLen = GetClipLength("ReloadM4");
            StartCoroutine(FinishReloadCoroutine(clipLen));
        }

        private IEnumerator FinishReloadCoroutine(float t)
        {
            yield return new WaitForSeconds(t);

            ammosLoaded = clipSize;
            isReloading = false;
            cantFire = false;

            ResetBloom();
        }

        private int FindAmmoInInventory()
        {
            for (int i = 0; i < playerHeldBy.ItemSlots.Length; i++)
            {
                if (playerHeldBy.ItemSlots[i] != null)
                {
                    GunAmmo gunAmmo = playerHeldBy.ItemSlots[i].GetComponent<GunAmmo>();
                    if (gunAmmo != null && gunAmmo.ammoType == gunCompatibleAmmoID)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        // ---------------------------------------------------------------------------------------
        private void TrySwitchFireMode()
        {
            if (playerHeldBy == null) return;
            if (isReloading || isInspecting || isSwitchingFireMode) return;

            isSwitchingFireMode = true;
            cantFire = true;

            SwitchFireModeServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SwitchFireModeServerRpc()
        {
            SwitchFireModeClientRpc();
        }

        [ClientRpc]
        private void SwitchFireModeClientRpc()
        {
            StartSwitchFireMode();
        }

        private void StartSwitchFireMode()
        {
            isSwitchingFireMode = true;
            cantFire = true;

            playerHeldBy.playerBodyAnimator.Play("SwitchFireModeM4", 4, 0f);

            audio.PlayOneShot(SFX_SwitchFireModeM4);

            float clipLen = GetClipLength("SwitchFireModeM4") / 2;
            StartCoroutine(FinishSwitchFireModeCoroutine(clipLen));
        }

        private IEnumerator FinishSwitchFireModeCoroutine(float t)
        {
            yield return new WaitForSeconds(t);

            automatic = !automatic;

            cantFire = false;
            isSwitchingFireMode = false;
        }

        // ---------------------------------------------------------------------------------------
        private void TryInspect()
        {
            if (playerHeldBy == null) return;
            if (isReloading || isInspecting || isSwitchingFireMode) return;

            isInspecting = true;
            cantFire = true;

            InspectServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void InspectServerRpc()
        {
            InspectClientRpc();
        }

        [ClientRpc]
        private void InspectClientRpc()
        {
            StartInspect();
        }

        private void StartInspect()
        {
            isInspecting = true;
            cantFire = true;

            playerHeldBy.playerBodyAnimator.Play("InspectM4", 4, 0f);

            float clipLen = GetClipLength("InspectM4");
            StartCoroutine(EndInspectCoroutine(clipLen));
        }

        private IEnumerator EndInspectCoroutine(float t)
        {
            yield return new WaitForSeconds(t);

            isInspecting = false;
            cantFire = false;
        }

        // ---------------------------------------------------------------------------------------
        private Vector3 GetShootDirection()
        {
            var cam = playerHeldBy.gameplayCamera;

            Vector3 dir = cam.transform.forward;

            dir.x += UnityEngine.Random.Range(-currentBloom, currentBloom);
            dir.y += UnityEngine.Random.Range(-currentBloom, currentBloom);

            return dir.normalized;
        }

        private void IncreaseBloom()
        {
            currentBloom = Mathf.Clamp(currentBloom + bloomIncrease, baseSpread, maxBloom);
        }

        private void ResetBloom()
        {
            currentBloom = baseSpread;
        }

        private void ApplyCameraRecoil()
        {
            if (playerHeldBy == null) return;

            currentRecoilY += recoilVertical;
            currentRecoilX += UnityEngine.Random.Range(-recoilHorizontal, recoilHorizontal);

            HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
        }

        private void UpdateRecoilCamera()
        {
            if (playerHeldBy == null) return;

            playerHeldBy.cameraUp -= currentRecoilY * Time.deltaTime * 8f;

            playerHeldBy.cameraUp = Mathf.Clamp(playerHeldBy.cameraUp, -80f, 80f);

            playerHeldBy.thisPlayerBody.Rotate(Vector3.up * (currentRecoilX * Time.deltaTime * 8f));

            currentRecoilX = Mathf.Lerp(currentRecoilX, 0, Time.deltaTime * recoilReturnSpeed);
            currentRecoilY = Mathf.Lerp(currentRecoilY, 0, Time.deltaTime * recoilReturnSpeed);
        }

        // ---------------------------------------------------------------------------------------
        private void DisplayRifleTooltip()
        {
            string currentMode = automatic ? "Auto" : "Semi";

            itemProperties.toolTips[2] = $"{baseAmmoTip} [{ammosLoaded}]";
            itemProperties.toolTips[3] = $"{baseModeTip} [{currentMode}]";

            HUDManager.Instance.ChangeControlTipMultiple(itemProperties.toolTips, holdingItem: false, itemProperties);
        }

        // ---------------------------------------------------------------------------------------
        // ANIMATOR CONTROLLER OVERRIDE, (DO NOT TOUCH!)
        // ---------------------------------------------------------------------------------------
        private void SaveAnimatorState(Animator anim)
        {
            savedState = anim.GetCurrentAnimatorStateInfo(0);
            savedNormalizedTime = savedState.normalizedTime;

            savedCrouching = anim.GetBool("crouching");
            savedWalking = anim.GetBool("Walking");
            savedJumping = anim.GetBool("Jumping");
            savedSprinting = anim.GetBool("Sprinting");
        }

        private void RestoreAnimatorState(Animator anim)
        {
            anim.Play(savedState.fullPathHash, 0, savedNormalizedTime);

            anim.SetBool("crouching", savedCrouching);
            anim.SetBool("Walking", savedWalking);
            anim.SetBool("Jumping", savedJumping);
            anim.SetBool("Sprinting", savedSprinting);
            playerHeldBy.playerBodyAnimator.Play("HoldM4", 4, 0f);
        }

        private void EnableRifleAnimator()
        {
            if (animatorReplaced) return;
            if (playerHeldBy == null) return;

            if (!savedAnimators.ContainsKey(playerHeldBy.playerClientId))
            {
                savedAnimators[playerHeldBy.playerClientId] = playerHeldBy.playerBodyAnimator.runtimeAnimatorController;
            }

            SaveAnimatorState(playerHeldBy.playerBodyAnimator);

            if (playerHeldBy == GameNetworkManager.Instance.localPlayerController)
            {
                playerHeldBy.playerBodyAnimator.runtimeAnimatorController = rifleLocalAnimator;
            }
            else
            {
                playerHeldBy.playerBodyAnimator.runtimeAnimatorController = rifleRemoteAnimator;
            }

            playerHeldBy.playerBodyAnimator.Rebind();
            playerHeldBy.playerBodyAnimator.Update(0f);

            RestoreAnimatorState(playerHeldBy.playerBodyAnimator);
            animatorReplaced = true;
        }

        private void DisableRifleAnimator()
        {
            if (!animatorReplaced) return;
            if (playerHeldBy == null) return;

            SaveAnimatorState(playerHeldBy.playerBodyAnimator);

            if (savedAnimators.TryGetValue(playerHeldBy.playerClientId, out var original))
            {
                playerHeldBy.playerBodyAnimator.runtimeAnimatorController = original;
                savedAnimators.Remove(playerHeldBy.playerClientId);
            }

            playerHeldBy.playerBodyAnimator.Rebind();
            playerHeldBy.playerBodyAnimator.Update(0f);

            RestoreAnimatorState(playerHeldBy.playerBodyAnimator);

            animatorReplaced = false;
        }

        private float GetClipLength(string clipName)
        {
            if (playerHeldBy == null || playerHeldBy.playerBodyAnimator == null || playerHeldBy.playerBodyAnimator.runtimeAnimatorController == null)
            {
                return -1;
            }

            foreach (var clip in playerHeldBy.playerBodyAnimator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == clipName)
                {
                    return clip.length;
                }
            }
            return -1;
        }
    }
}
