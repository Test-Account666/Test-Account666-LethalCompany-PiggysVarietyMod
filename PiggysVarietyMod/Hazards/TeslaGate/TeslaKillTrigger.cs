using GameNetcodeStuff;
using UnityEngine;
using static PiggysVarietyMod.Utils.ReferenceResolver;

namespace PiggysVarietyMod.Hazards.TeslaGate;

public class TeslaKillTrigger : MonoBehaviour {
    private void OnTriggerStay(Collider other) {
        var isEnemy = TryGetEnemy(other, out var enemyAI);
        if (isEnemy) {
            OnEnemyTrigger(enemyAI);
            return;
        }

        var player = other.GetComponentInChildren<PlayerControllerB>();
        if (!player) return;

        var localPlayer = StartOfRound.Instance.localPlayerController;
        if (player != localPlayer) return;

        player.DamagePlayer(player.health, causeOfDeath: CauseOfDeath.Electrocution);
    }

    private static void OnEnemyTrigger(EnemyAI enemyAI) {
        if (StartOfRound.Instance is {
                IsHost: false,
                IsServer: false,
            }) return;

        if (!enemyAI) return;
        if (enemyAI is not {
                isEnemyDead: false,
                enemyType.canDie: true,
            }) return;

        enemyAI.KillEnemyClientRpc(false);
    }
}