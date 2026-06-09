using UnityEngine;
using XNClient.Logger;

public static class GameAudio
{
    private const string AudioRootName = "GameAudio";
    private const string MainMenuBgmPath = "Audio/BGM/Active/HappyMomentsPianoFull.ogg";
    private const string DuelBgmPath = "Audio/BGM/Active/HappyMomentsPianoFull.ogg";
    private const float MainMenuBgmVolume = 0.34f;
    private const float DuelBgmVolume = 0.24f;
    private const float StonePlaceVolume = 0.58f;
    private const float StoneCaptureVolume = 0.64f;
    private const float StonePlaceMinInterval = 0.055f;
    private const float StoneCaptureMinInterval = 0.12f;
    private const float CaptureSuppressPlaceDuration = 0.10f;
    private const float DuelVoiceVolume = 0.78f;

    private static readonly string[] StonePlaceClipPaths = {
        "Audio/SFX/StonePlace/StonePlace_Sabaki_00.mp3",
        "Audio/SFX/StonePlace/StonePlace_Sabaki_01.mp3",
        "Audio/SFX/StonePlace/StonePlace_Sabaki_02.mp3",
        "Audio/SFX/StonePlace/StonePlace_Sabaki_03.mp3",
        "Audio/SFX/StonePlace/StonePlace_Sabaki_04.mp3",
    };

    private const string StoneSingleCaptureClipPath = "Audio/SFX/Capture/Capture_Single.mp3";
    private const string StoneMultiCaptureClipPath = "Audio/SFX/Capture/Capture_Multi.mp3";

    private static readonly string[] DuelVoiceClipPaths = {
        "Audio/Voice/OgsPreview/GameStarted.wav",
        "Audio/Voice/OgsPreview/StartCounting.wav",
        "Audio/Voice/OgsPreview/Byoyomi.wav",
        "Audio/Voice/OgsPreview/Overtime.wav",
        "Audio/Voice/OgsPreview/PeriodsLeft5.wav",
        "Audio/Voice/OgsPreview/PeriodsLeft4.wav",
        "Audio/Voice/OgsPreview/PeriodsLeft3.wav",
        "Audio/Voice/OgsPreview/PeriodsLeft2.wav",
        "Audio/Voice/OgsPreview/LastPeriod.wav",
        "Audio/Voice/OgsPreview/Countdown10.wav",
        "Audio/Voice/OgsPreview/Countdown09.wav",
        "Audio/Voice/OgsPreview/Countdown08.wav",
        "Audio/Voice/OgsPreview/Countdown07.wav",
        "Audio/Voice/OgsPreview/Countdown06.wav",
        "Audio/Voice/OgsPreview/Countdown05.wav",
        "Audio/Voice/OgsPreview/Countdown04.wav",
        "Audio/Voice/OgsPreview/Countdown03.wav",
        "Audio/Voice/OgsPreview/Countdown02.wav",
        "Audio/Voice/OgsPreview/Countdown01.wav",
        "Audio/Voice/OgsPreview/RemoveDeadStones.wav",
        "Audio/Voice/OgsPreview/Pass.wav",
        "Audio/Voice/OgsPreview/BlackWins.wav",
        "Audio/Voice/OgsPreview/WhiteWins.wav",
        "Audio/Voice/OgsPreview/Tie.wav",
        "Audio/Voice/OgsPreview/YouHaveWon.wav",
    };

    private static GameObject audioRoot;
    private static AudioSource bgmSource;
    private static AudioSource sfxSource;
    private static AudioSource voiceSource;
    private static string currentBgmPath;
    private static AudioClip[] stonePlaceClips;
    private static AudioClip stoneSingleCaptureClip;
    private static AudioClip stoneMultiCaptureClip;
    private static readonly System.Collections.Generic.Dictionary<DuelVoiceCue, AudioClip> duelVoiceClips =
        new System.Collections.Generic.Dictionary<DuelVoiceCue, AudioClip>();
    private static float lastStonePlaceTime = -999f;
    private static float lastStoneCaptureTime = -999f;
    private static int lastStonePlaceClipIndex = -1;

    public static void PlayMainMenuBgm()
    {
        PlayBgm(MainMenuBgmPath, MainMenuBgmVolume);
    }

    public static void PlayDuelBgm()
    {
        PlayBgm(DuelBgmPath, DuelBgmVolume);
    }

    public static void PlayDuelVoice(DuelVoiceCue cue)
    {
        EnsureAudioRoot();
        AudioClip clip = LoadDuelVoiceClip(cue);
        if (clip == null) {
            return;
        }

        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.volume = DuelVoiceVolume;
        voiceSource.pitch = 1f;
        voiceSource.Play();
    }

    public static bool IsDuelVoicePlaying()
    {
        return voiceSource != null && voiceSource.isPlaying;
    }

    public static void PlayStonePlace()
    {
        EnsureAudioRoot();
        float now = Time.unscaledTime;
        if (now - lastStoneCaptureTime <= CaptureSuppressPlaceDuration || now - lastStonePlaceTime < StonePlaceMinInterval) {
            return;
        }

        AudioClip[] clips = LoadStonePlaceClips();
        if (clips.Length <= 0) {
            return;
        }

        AudioClip clip = PickClip(clips, ref lastStonePlaceClipIndex);
        if (clip == null) {
            return;
        }

        lastStonePlaceTime = now;
        PlayExclusiveSfx(clip, StonePlaceVolume, Random.Range(0.97f, 1.03f));
    }

    public static void PlayStoneCapture(int captureCount)
    {
        if (captureCount <= 0) {
            return;
        }

        EnsureAudioRoot();
        float now = Time.unscaledTime;
        if (now - lastStoneCaptureTime < StoneCaptureMinInterval) {
            return;
        }

        AudioClip clip = LoadStoneCaptureClip(captureCount);
        if (clip == null) {
            return;
        }

        lastStoneCaptureTime = now;
        PlayExclusiveSfx(clip, StoneCaptureVolume, Random.Range(0.99f, 1.05f));
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

        stonePlaceClips = LoadAudioClips(StonePlaceClipPaths);
        return stonePlaceClips;
    }

    private static AudioClip LoadStoneCaptureClip(int captureCount)
    {
        if (captureCount > 1) {
            if (stoneMultiCaptureClip == null) {
                stoneMultiCaptureClip = LoadAudioClip(StoneMultiCaptureClipPath);
            }
            return stoneMultiCaptureClip;
        }

        if (stoneSingleCaptureClip == null) {
            stoneSingleCaptureClip = LoadAudioClip(StoneSingleCaptureClipPath);
        }
        return stoneSingleCaptureClip;
    }

    private static AudioClip LoadDuelVoiceClip(DuelVoiceCue cue)
    {
        if (duelVoiceClips.TryGetValue(cue, out AudioClip cachedClip)) {
            return cachedClip;
        }

        int clipIndex = (int)cue;
        if (clipIndex < 0 || clipIndex >= DuelVoiceClipPaths.Length) {
            XNLogger.LogError("Duel voice cue is invalid.", ("cue", cue.ToString()));
            return null;
        }

        AudioClip clip = LoadAudioClip(DuelVoiceClipPaths[clipIndex]);
        if (clip != null) {
            duelVoiceClips[cue] = clip;
        }
        return clip;
    }

    private static AudioClip[] LoadAudioClips(string[] clipPaths)
    {
        var clips = new System.Collections.Generic.List<AudioClip>();
        foreach (string clipPath in clipPaths) {
            AudioClip clip = LoadAudioClip(clipPath);
            if (clip != null) {
                clips.Add(clip);
            }
        }

        return clips.ToArray();
    }

    private static AudioClip PickClip(AudioClip[] clips, ref int lastClipIndex)
    {
        int clipIndex = Random.Range(0, clips.Length);
        if (clips.Length > 1 && clipIndex == lastClipIndex) {
            clipIndex = (clipIndex + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
        }

        lastClipIndex = clipIndex;
        return clips[clipIndex];
    }

    private static void PlayExclusiveSfx(AudioClip clip, float volume, float pitch)
    {
        sfxSource.Stop();
        sfxSource.clip = clip;
        sfxSource.volume = volume;
        sfxSource.pitch = pitch;
        sfxSource.Play();
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
        if (audioRoot != null && bgmSource != null && sfxSource != null && voiceSource != null) {
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

        voiceSource = audioRoot.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.spatialBlend = 0f;
    }
}

public enum DuelVoiceCue
{
    GameStarted,
    StartCounting,
    Byoyomi,
    Overtime,
    PeriodsLeft5,
    PeriodsLeft4,
    PeriodsLeft3,
    PeriodsLeft2,
    LastPeriod,
    Countdown10,
    Countdown09,
    Countdown08,
    Countdown07,
    Countdown06,
    Countdown05,
    Countdown04,
    Countdown03,
    Countdown02,
    Countdown01,
    RemoveDeadStones,
    Pass,
    BlackWins,
    WhiteWins,
    Tie,
    YouHaveWon,
}
