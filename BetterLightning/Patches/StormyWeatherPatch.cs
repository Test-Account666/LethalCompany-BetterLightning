using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterLightning.Patches;

[HarmonyPatch(typeof(StormyWeather))]
public static class StormyWeatherPatch {
    public static Vector3 strikePosition;
    public static ParticleSystem staticElectricityParticle = null!;
    public static AudioSource? electricityAudioSource;

    [HarmonyPatch(nameof(StormyWeather.DetermineNextStrikeInterval))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void DetermineNextPosition(StormyWeather __instance) {
        if (staticElectricityParticle == null || !staticElectricityParticle) {
            staticElectricityParticle = Object.Instantiate(__instance.staticElectricityParticle);

            var shape = staticElectricityParticle.shape;
            shape.meshRenderer = null;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .1F;
        }

        DetermineNextStrikePosition(__instance);
    }

    [HarmonyPatch(nameof(StormyWeather.LightningStrike))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static void StopElectricity(StormyWeather __instance) {
        electricityAudioSource?.Stop();

        if (staticElectricityParticle == null || !staticElectricityParticle) return;

        staticElectricityParticle.Stop();
    }

    [HarmonyPatch(nameof(StormyWeather.LightningStrikeRandom))]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static bool ReplaceRandomLightningStrike(StormyWeather __instance) {
        __instance.lastRandomStrikePosition = strikePosition;
        __instance.LightningStrike(strikePosition, false);
        return false;
    }

    private static void DetermineNextStrikePosition(StormyWeather stormyWeather) {
        if (stormyWeather.seed.Next(0, 100) < 60
         && (stormyWeather.randomThunderTime - (double) stormyWeather.timeAtLastStrike) * TimeOfDay.Instance.currentWeatherVariable < 3.0) {
            strikePosition = stormyWeather.lastRandomStrikePosition;
            return;
        }

        var outsideNodes = stormyWeather.outsideNodes;

        outsideNodes ??= [
        ];

        var index = stormyWeather.seed.Next(0, outsideNodes.Length);

        if (outsideNodes.Length <= index || outsideNodes[index] == null) {
            stormyWeather.outsideNodes = GameObject.FindGameObjectsWithTag("OutsideAINode")
                                                   .OrderBy<GameObject, float>(x => x.transform.position.x + x.transform.position.z)
                                                   .ToArray();

            outsideNodes = stormyWeather.outsideNodes;
        }

        strikePosition = RoundManager.Instance.GetRandomNavMeshPositionInBoxPredictable(outsideNodes[index].transform.position, 15f,
                                                                                        stormyWeather.navHit, stormyWeather.seed);

        BetterLightning.Logger.LogDebug($"Next Lightning Strike at: {strikePosition}");

        var localPlayer = StartOfRound.Instance.localPlayerController;

        localPlayer.TeleportPlayer(strikePosition);

        var timeDifference = stormyWeather.randomThunderTime - TimeOfDay.Instance.globalTime;

        BetterLightning.Logger.LogDebug($"TimeDifference? {timeDifference}");

        var particleTime = timeDifference * .7F * TimeOfDay.Instance.globalTimeSpeedMultiplier;

        var length = stormyWeather.staticElectricityAudio.length - 2F;

        var waitTime = 0F;

        if (particleTime > length) {
            waitTime = particleTime - length;

            waitTime *= .7F;

            particleTime = length;
        }

        particleTime = length - particleTime;

        particleTime += 2F;

        BetterLightning.Logger.LogDebug($"ParticleTime? {particleTime}");

        BetterLightning.Logger.LogDebug($"Length? {length}");

        BetterLightning.Logger.LogDebug($"Wait? {waitTime}");

        stormyWeather.StartCoroutine(WaitToPlayParticles(particleTime, stormyWeather, waitTime));
    }

    private static IEnumerator WaitToPlayParticles(float particleTime, StormyWeather stormyWeather, float waitTime) {
        yield return new WaitForSeconds(waitTime);

        PlayLightningParticles(particleTime, stormyWeather.staticElectricityAudio);
    }

    private static void PlayLightningParticles(float particleTime, AudioClip staticElectricityAudio) {
        var particleGameObject = staticElectricityParticle.gameObject;

        particleGameObject.transform.SetPositionAndRotation(strikePosition, Quaternion.identity);

        staticElectricityParticle.time = particleTime;
        staticElectricityParticle.Play();
        staticElectricityParticle.time = particleTime;

        if (electricityAudioSource == null || !electricityAudioSource) electricityAudioSource = particleGameObject.GetComponent<AudioSource>();

        electricityAudioSource.loop = false;
        electricityAudioSource.clip = staticElectricityAudio;
        electricityAudioSource.Play();
        electricityAudioSource.time = particleTime;
    }
}