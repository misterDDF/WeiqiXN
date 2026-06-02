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

## Notes

Sabaki sound assets are covered by the Sabaki MIT license at https://github.com/SabakiHQ/Sabaki/blob/master/LICENSE.md.

Pixabay candidates from the earlier review were not imported because automated downloads were blocked by Cloudflare. They can still be manually downloaded and swapped into `Assets/Audio/BGM/Active/` later if preferred.
