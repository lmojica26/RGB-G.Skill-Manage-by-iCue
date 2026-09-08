# GSkillCue — manage G.Skill Trident Z5 RGB from Corsair iCUE

**GSkillCue** makes your **G.Skill Trident Z5 RGB** DDR5 memory follow whatever lighting you set
up in **Corsair iCUE**, in real time.

iCUE can't natively control non‑Corsair hardware (that needs a Corsair hardware‑partner deal), and
G.Skill's memory isn't on that list. So GSkillCue doesn't try to add a fake device to iCUE —
instead it **reads the colours iCUE is already displaying** on a Corsair device you own (fans,
cooler, LED strips, a keyboard…) through the official iCUE SDK, and **replays them onto the RAM**
over the SMBus using the same ENE/Aura protocol OpenRGB uses.

```
iCUE ──(iCUE SDK: CorsairGetLedColors)──► GSkillCue ──(SMBus, ENE direct mode)──► Trident Z5 RGB
```

Build any effect you like in iCUE, tell GSkillCue which Corsair device to mirror, and the RAM
tracks it.

---

## ⚠️ Read before you run it — DDR5 SMBus safety

Controlling RGB on DDR5 means sharing the SMBus with the memory's SPD and PMIC chips. Done wrong,
that can corrupt the SPD and stop the machine from booting. GSkillCue is deliberately conservative:

* `GSkillCue.Smbus` **physically cannot issue a write outside `0x70–0x77`** (the ENE RGB
  controller range). Any attempt to write to the SPD (`0x50–0x57`) or anywhere else throws before
  it touches the bus.
* `gskillcue spd --backup` / `spd --verify` snapshot and check your SPD so you can prove nothing
  changed.

**You still must:**

1. **Only one program may drive the memory bus.** Fully remove / disable other RAM‑RGB software:
   * **SignalRGB** — uninstall.
   * **ASUS Armoury Crate** — uninstall with ASUS's *Armoury Crate Uninstall Tool* (uninstalling
     just the Store app leaves `asus_framework` running, which fights GSkillCue for the bus and
     produces wrong colours).
   * **OpenRGB** — don't run it at the same time.
   * **G.Skill Trident Z Lighting Control** — uninstall.
2. **(Recommended) BIOS → enable "SPD Write Disable" / "SPD Write Protect"** if your board exposes
   it (many ASUS AM5 boards don't — that's OK, the software guardrail covers the same risk).
3. **Back up the SPD first:** `gskillcue spd --backup spd-before.txt`, then `spd --verify` after a
   session.

This project has been used successfully on an **ASUS ROG Crosshair X870E + Ryzen 9 9950X3D +
G.Skill F5‑6000J3040G32G (2×32 GB)**. Other boards/kits are untested — proceed carefully.

---

## Requirements

* Windows 10/11 x64
* **PawnIO** kernel driver — <https://pawnio.eu> (signed; the SMBus access layer)
* **Corsair iCUE** running, with **Settings → SDK → "Enable SDK"** turned **on**, and at least one
  Corsair RGB device to mirror
* GSkillCue runs **elevated** (PawnIO needs administrator)
* G.Skill Trident Z5 **RGB** memory with an ENE/Aura RGB controller

## Install

1. **Install PawnIO** from <https://pawnio.eu> (run its installer once).
2. Download the latest GSkillCue release (or build from source — see below) and unzip it anywhere.
3. In iCUE: **Settings → SDK → Enable SDK**.
4. Make sure no other RGB software is managing the RAM (see the safety section).

## First run — bring‑up

Open an **Administrator** terminal in the `cli` folder and go in order:

```powershell
gskillcue smbus --scan                    # read-only. Must list a device at 0x71 / 0x73 (or 0x70-0x77).
gskillcue spd --backup spd-before.txt      # read-only SPD snapshot
gskillcue ram --detect                     # should print your module(s) + LED count
gskillcue ram --test rainbow               # first write — the RAM should cycle a rainbow
gskillcue ram --test off
gskillcue spd --verify spd-before.txt       # confirms only volatile sensor bytes moved
```

Find the Corsair device to mirror:

```powershell
gskillcue icue --list
gskillcue icue --dump "{device-id}"        # watch values change as you edit an iCUE effect
```

## Everyday use — the tray app

Run **`GSkillCueTray.exe`** (as administrator). It puts an icon in the system tray:

| Icon | Meaning |
|---|---|
| 🟣 purple | idle |
| 🟢 green | mirroring |
| 🟡 amber | paused (e.g. a conflicting RGB app started) |
| 🔴 red | fault — check the log |

Right‑click for **Start/Stop**, **Mapping** mode, and **Settings…**:

* **Mirror source** — which Corsair device the RAM follows
* **Mapping** — *Average* (one colour), *Spatial slice* (sample the source left→right and spread
  it across the DIMM LEDs — default), *Per‑DIMM split*
* **Frame rate**, **Brightness**, **Smoothing**
* **On exit** — hold the last frame / turn the RAM off / set a static colour
* **Start with Windows** — installs a Scheduled Task so it launches elevated at logon without a
  UAC prompt

Config: `%APPDATA%\GSkillCue\config.json` · Logs: `%APPDATA%\GSkillCue\logs\`

## CLI reference

```
gskillcue smbus --scan [--port N]            Read-only SMBus probe
gskillcue spd --backup <file> [--port N]     Snapshot SPD page 0 (read-only)
gskillcue spd --verify <file> [--port N]     Re-read SPD and diff against a snapshot
gskillcue ram --detect [--no-remap] [--port N]
gskillcue ram --test <red|green|blue|white|rainbow|off> [--seconds N] [--port N]
gskillcue icue --list
gskillcue icue --dump <deviceId> [--seconds N]
gskillcue run                                Headless mirror loop
```

Set `GSKILLCUE_DEBUG=1` for stack traces.

## How it works

| Project | Role |
|---|---|
| `GSkillCue.Core` | colour maths, mapping, config, logging, conflict detection |
| `GSkillCue.Smbus` | PawnIO P/Invoke + AMD‑FCH SMBus; **hard write allow‑list (0x70–0x77 only)** |
| `GSkillCue.GSkillRam` | ENE/Aura DRAM protocol — detect, enter direct mode, push per‑LED colour |
| `GSkillCue.ICue` | iCUE SDK v4 P/Invoke wrapper (read‑only) |
| `GSkillCue.Bridge` | the frame loop: iCUE → mapping → RAM |
| `GSkillCue.Tray` | WinForms system‑tray app |
| `GSkillCue.Cli` | `gskillcue` diagnostics + headless `run` |
| `GSkillCue.Tests` | xUnit — mapping, allow‑list, ENE protocol against an in‑memory fake |

The ENE controllers on Trident Z5 sit at SMBus `0x71`/`0x73`, identify as `AUDA0-E6K5-0101`, and
take per‑LED colour in **R, B, G** byte order at register `0x8100` while the "direct" flag
(`0x8020`) is set. GSkillCue uses large SMBus block writes for a ~13 fps refresh.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `PawnIO access denied` | Run elevated. |
| `pawnio_open failed` / `PawnIOLib.dll not found` | Install PawnIO from pawnio.eu. |
| `smbus --scan` finds nothing at 0x70–0x77 | Another RGB app owns the bus, or your board doesn't expose the DIMM bus. Close Armoury Crate/SignalRGB/OpenRGB; try `--port 1`. |
| Colours look wrong / "kind of red" / flicker | `asus_framework` (Armoury Crate remnant) or another RGB app is fighting for the bus. Remove it fully. |
| `iCUE SDK did not connect` | iCUE not running, or **Enable SDK** is off in iCUE settings. |
| Bridge says "Paused — conflicting RGB software" | Close SignalRGB / OpenRGB / ASUS LightingService. |

## Build from source

```powershell
dotnet build GSkillCue.sln -c Release
dotnet test  tests/GSkillCue.Tests
./scripts/publish.ps1        # self-contained x64 build → ./dist
```

.NET 8 SDK required. The vendored `iCUESDK.x64_2019.dll` and PawnIO `.bin` modules are checked in;
the PawnIO **driver** is not — install it separately.

## Licence

**GPL‑2.0‑or‑later** — this project ports GPL code from
[OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB). See `LICENSE` and `NOTICE.md`.

Not affiliated with Corsair, G.Skill, ASUS, or the OpenRGB project.
