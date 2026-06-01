using UnityEngine;
using XNClient.Logger;

public static class GameAudio
{
    private const string AudioRootName = "GameAudio";
    private const string MainMenuBgmPath = "Audio/BGM/Active/MainMenu_Asianoriental1";
    private const string DuelBgmPath = "Audio/BGM/Active/Duel_KotoBooth";
    private const float MainMenuBgmVolume = 0.42f;
    private const float DuelBgmVolume = 0.26f;
    private const float StonePlaceVolume = 0.72f;

    private static readonly string[] StonePlaceClipPaths = {
        "Audio/SFX/StonePlace/StonePlace_01",
        "Audio/SFX/StonePlace/StonePlace_02",
        "Audio/SFX/StonePlace/StonePlace_03",
        "Audio/SFX/StonePlace/StonePlace_04",
    };

    private static GameObject audioRoot;
    private static AudioSource bgmSource;
    private static AudioSource sfxSource;
    private static string currentBgmPath;
    private static AudioClip[] stonePlaceClips;

    public static void PlayMainMenuBgm()
    {
        PlayBgm(MainMenuBgmPath, MainMenuBgmVolume);
    }

    public static void PlayDuelBgm()
    {
        PlayBgm(DuelBgmPath, DuelBgmVolume);
    }

    public static void PlayStonePlace()
    {
        EnsureAudioRoot();
        AudioClip[] clips = LoadStonePlaceClips();
        if (clips.Length <= 0) {
            return;
        }

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) {
            return;
        }

        sfxSource.pitch = Random.Range(0.97f, 1.03f);
        sfxSource.PlayOneShot(clip, StonePlaceVolume);
    }

    private static void PlayBgm(string clipPath, float volume)
    {
        EnsureAudioRoot();
        if (bgmSource.clip != null && currentBgmPath == clipPath) {
            bgmSource.volume = volume;
            if (!bgmSource.isPlaying) {
                bgmSource.Play();
            }
            return;
        }

        AudioClip clip = LoadAudioClip(clipPath);
        if (clip == null) {
            return;
        }

        currentBgmPath = clipPath;
        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.volume = volume;
        bgmSource.pitch = 1f;
        bgmSource.Play();
    }

    private static AudioClip[] LoadStonePlaceClips()
    {
        if (stonePlaceClips != null) {
            return stonePlaceClips;
        }

        var clips = new System.Collections.Generic.List<AudioClip>();
        foreach (string clipPath in StonePlaceClipPaths) {
            AudioClip clip = LoadAudioClip(clipPath);
            if (clip != null) {
                clips.Add(clip);
            }
        }

        stonePlaceClips = clips.ToArray();
        return stonePlaceClips;
    }

    private static AudioClip LoadAudioClip(string clipPath)
    {
        if (Global.Instance.resourceManager == null) {
            XNLogger.LogError("Audio clip load failed, resource manager is null.", ("clipPath", clipPath));
            return null;
        }

        AudioClip clip = Global.Instance.resourceManager.LoadAsset<AudioClip>(clipPath);
        if (clip == null) {
            XNLogger.LogError("Audio clip load failed.", ("clipPath", clipPath));
        }
        return clip;
    }

    private static void EnsureAudioRoot()
    {
        if (audioRoot != null && bgmSource != null && sfxSource != null) {
            return;
        }

        audioRoot = new GameObject(AudioRootName);
        Object.DontDestroyOnLoad(audioRoot);

        bgmSource = audioRoot.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.spatialBlend = 0f;

        sfxSource = audioRoot.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
    }
}
