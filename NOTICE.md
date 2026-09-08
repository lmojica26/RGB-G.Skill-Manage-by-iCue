# Third-party code and components

GSkillCue is licensed **GPL-2.0-or-later** (see `LICENSE`) because it incorporates code derived
from OpenRGB.

| Component | Origin | Licence | How it's used |
|---|---|---|---|
| ENE / ASUS-Aura DRAM RGB protocol (`GSkillCue.GSkillRam`) and the PawnIO SMBus transport (`GSkillCue.Smbus`) | [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) — `Controllers/ENESMBusController`, `i2c_smbus/Windows/i2c_smbus_pawnio.cpp` | GPL-2.0-or-later | Ported to C#. Register maps, detection logic and the `ioctl_smbus_xfer` packing follow the OpenRGB implementation. |
| `PawnIOLib.dll`, `SmbusPIIX4.bin`, `SmbusI801.bin` (`vendor/pawnio/`) | [PawnIO](https://pawnio.eu) / [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) — namazso | LGPL-2.1 (lib) / GPL-2.0 (modules) | Redistributed unmodified. The **PawnIO driver** is not bundled — install it from https://pawnio.eu. |
| `iCUESDK.x64_2019.dll` (`vendor/icue/`) | [Corsair iCUE SDK](https://github.com/CorsairOfficial/cue-sdk) v4.0.84 | Corsair SDK licence (redistribution permitted for apps that interface with iCUE) | P/Invoke target for reading iCUE's live lighting. |

The `iCUESDK.x64_2019.dll` is Corsair's redistributable. It is included for convenience; the
canonical copy is in the `redist/x64` folder of the iCUE SDK release.
