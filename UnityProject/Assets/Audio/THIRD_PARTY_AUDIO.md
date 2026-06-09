# Audio Assets

Updated on 2026-06-02 for active light piano BGM, crisp Sabaki stone SFX, and BGM preview candidates.

## Active Set

| Project path | Source | Author | License | Note |
| --- | --- | --- | --- | --- |
| `Assets/Audio/BGM/Active/HappyMomentsPianoFull.ogg` | https://opengameart.org/content/happy-moments | Centurion_of_war | CC0 | Active BGM for both `MainMenuScene` and `DuelScene`; imported from `piano1.4.ogg`. |
| `Assets/Audio/SFX/StonePlace/StonePlace_Sabaki_00.mp3` | https://github.com/SabakiHQ/Sabaki/tree/master/data | Yichuan Shen / Sabaki contributors | MIT | Active placement SFX group imported from Sabaki `data/0.mp3`; Sabaki uses `0..4.mp3` for placement playback. |
| `Assets/Audio/SFX/StonePlace/StonePlace_Sabaki_01.mp3` | https://github.com/SabakiHQ/Sabaki/tree/master/data | Yichuan Shen / Sabaki contributors | MIT | Active placement SFX group imported from Sabaki `data/1.mp3`. |
| `Assets/Audio/SFX/StonePlace/StonePlace_Sabaki_02.mp3` | https://github.com/SabakiHQ/Sabaki/tree/master/data | Yichuan Shen / Sabaki contributors | MIT | Active placement SFX group imported from Sabaki `data/2.mp3`. |
| `Assets/Audio/SFX/StonePlace/StonePlace_Sabaki_03.mp3` | https://github.com/SabakiHQ/Sabaki/tree/master/data | Yichuan Shen / Sabaki contributors | MIT | Active placement SFX group imported from Sabaki `data/3.mp3`. |
| `Assets/Audio/SFX/StonePlace/StonePlace_Sabaki_04.mp3` | https://github.com/SabakiHQ/Sabaki/tree/master/data | Yichuan Shen / Sabaki contributors | MIT | Active placement SFX group imported from Sabaki `data/4.mp3`. |
| `Assets/Audio/SFX/Capture/Capture_Single.mp3` | https://github.com/SabakiHQ/Sabaki/tree/master/data | Yichuan Shen / Sabaki contributors | MIT | Active single-stone capture SFX, renamed from Sabaki `data/capture3.mp3`. |
| `Assets/Audio/SFX/Capture/Capture_Multi.mp3` | https://github.com/SabakiHQ/Sabaki/tree/master/data | Yichuan Shen / Sabaki contributors | MIT | Active multi-stone capture SFX, renamed from Sabaki `data/capture1.mp3`; used when one move captures more than one stone. |

## BGM Preview Candidates

These candidates are imported for editor preview only. They are not referenced by `GameAudio` and are not listed in `runtime_asset` until one is selected as active runtime BGM.

| Project path | Source | Author | License | Note |
| --- | --- | --- | --- | --- |
| `Assets/Audio/BGM/Alternatives/Similar_ForgetMeNot_Looped.ogg` | https://opengameart.org/content/forget-me-not | Kistol | CC0 | Gentle looped piano candidate; imported from `forget_me_not_in_f_major_looped.ogg`. |
| `Assets/Audio/BGM/Alternatives/Similar_ElevateSoftPiano.ogg` | https://opengameart.org/content/elevate-instrument-tracks | Fupi | CC0 | Happy soft-piano candidate; imported from `elevatesoftpiano.ogg`. |
| `Assets/Audio/BGM/Alternatives/Similar_LoopTown.ogg` | https://opengameart.org/content/loop-town | Fupi | CC0 | Cheerful comparison candidate with additional arrangement beyond piano; imported from `loopcity.ogg`. |

## Duel Voice Preview

The files under `Assets/Audio/Voice/OgsPreview/*.wav` are Mandarin Chinese preview clips used by the duel voice-prompt wiring. Most clips are sliced from Online-Go.com audio sprites. `StartCounting.wav` and `Byoyomi.wav` are local Microsoft Huihui Desktop TTS preview clips so the entry-byoyomi prompts say "开始读秒" and "读秒" instead of the OGS `zh-cn-inni` pack's "byoyomi" wording. The runtime code references these exact filenames through `GameAudio`, so alternate final voice files can replace them in place without changing code or `runtime_asset` rows.

| Project path | Source | Author | License | Note |
| --- | --- | --- | --- | --- |
| `Assets/Audio/Voice/OgsPreview/*.wav`, except `StartCounting.wav` and `Byoyomi.wav` | `F:\WorkSpace\online-go.com\assets\sound\zh-cn-inni-phrases.v7.mp3`, `F:\WorkSpace\online-go.com\assets\sound\zh-cn-inni-numbers.v7.mp3`, and sprite offsets in `F:\WorkSpace\online-go.com\src\lib\sfx_sprites.ts` | Online-Go.com contributors / Inni voice pack | Apache-2.0 | Preview duel voice cues sliced with FFmpeg into 44.1 kHz mono PCM wav files. |
| `Assets/Audio/Voice/OgsPreview/StartCounting.wav` | Local Windows TTS, text: `开始读秒` | Microsoft Huihui Desktop | Windows voice preview asset | Temporary local preview replacement for OGS `start_counting`, which says "开始 byoyomi" in the `zh-cn-inni` pack. |
| `Assets/Audio/Voice/OgsPreview/Byoyomi.wav` | Local Windows TTS, text: `读秒` | Microsoft Huihui Desktop | Windows voice preview asset | Temporary local preview replacement for OGS `byoyomi`, which says "byoyomi" in the `zh-cn-inni` pack. |

Current preview cue filenames:

- `GameStarted.wav`
- `StartCounting.wav`
- `Byoyomi.wav`
- `Overtime.wav`
- `PeriodsLeft5.wav`
- `PeriodsLeft4.wav`
- `PeriodsLeft3.wav`
- `PeriodsLeft2.wav`
- `LastPeriod.wav`
- `Countdown10.wav`
- `Countdown09.wav`
- `Countdown08.wav`
- `Countdown07.wav`
- `Countdown06.wav`
- `Countdown05.wav`
- `Countdown04.wav`
- `Countdown03.wav`
- `Countdown02.wav`
- `Countdown01.wav`
- `RemoveDeadStones.wav`
- `Pass.wav`
- `BlackWins.wav`
- `WhiteWins.wav`
- `Tie.wav`
- `YouHaveWon.wav`

## Notes

Sabaki sound assets are covered by the Sabaki MIT license at https://github.com/SabakiHQ/Sabaki/blob/master/LICENSE.md.

Online-Go.com sound assets are covered by the Online-Go.com Apache-2.0 license at `F:\WorkSpace\online-go.com\LICENSE`.

Pixabay candidates from the earlier review were not imported because automated downloads were blocked by Cloudflare. They can still be manually downloaded and swapped into `Assets/Audio/BGM/Active/` later if preferred.
