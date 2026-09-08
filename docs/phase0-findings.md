# Phase 0 — Feasibility findings & go/no-go

_Date: 2026-09-08. Machine: ASUS ROG Crosshair X870E GLACIAL / Ryzen 9 9950X3D / 2× G.Skill Trident Z5 RGB
`F5-6000J3040G32G` (DDR5-6000)._

## 1. Can iCUE natively "adopt" the G.Skill RAM? — No (confirmed)

Corsair only integrates third-party hardware through **partner SDKs** (the maker signs an NDA, Corsair ships the
plugin). G.Skill is not a partner. The public [iCUE SDK](https://corsairofficial.github.io/cue-sdk/) exposes
`CorsairGetLedColors / CorsairSetLedColors / CorsairGetDevices / CorsairSubscribeForEvents / CorsairSetLayerPriority`
— **no API to register a new device into iCUE's UI**. A "G.Skill RAM tile in iCUE" is not possible with anything
public.

**The workable design stays:** a bridge that *reads* iCUE's live colors from a real Corsair device and *replays*
them onto the DIMMs. You author effects in iCUE; the RAM follows.

## 2. What iCUE can drive (your Corsair gear)

OpenRGB's HID scan on this machine found:

| Device | Use as mirror source? |
|---|---|
| **Corsair iCUE Link System Hub** | ✅ primary — your fans / cooler live here, lots of LEDs |
| **Corsair Lighting Node Pro** | ✅ RGB strips / older fans |
| ASUS ROG Crosshair X870E (mobo, via Aura) | n/a |
| Logitech G512 keyboard, Creative AE-5 | not Corsair |

The iCUE SDK will expose the iCUE Link Hub + Lighting Node Pro; the bridge samples a horizontal slice of one of
them and spreads it across the DIMM LEDs.

## 3. The G.Skill Trident Z5 RGB protocol (extracted from OpenRGB source)

Trident Z5 RGB uses an **ENE / ASUS-Aura** RGB controller — OpenRGB has no G.Skill-specific driver, it uses its
generic `ENESMBusController`. Verified against OpenRGB `Controllers/ENESMBusController/`:

- **Bus:** AMD FCH SMBus (PawnIO module `SmbusPIIX4.bin`). `IF_DRAM_SMBUS` includes `AMD_FCH_SMBUS_DEV`, so the
  detector *does* run on this AM5 board — if the bus exposes the DIMM controllers.
- **Address:** ENE DRAM lands at `0x70–0x77` (with a slot-remap dance via `0x77` register `0x80F8/0x80F9`).
- **Register access:** write pointer = `write_word_data(dev, 0x00, byteswap(reg))`; read = `read_byte_data(dev, 0x81)`;
  write = `write_byte_data(dev, 0x01, val)`.
- **Direct control:** `ENE_REG_DIRECT (0x8020) = 1`, write RGB triples to `ENE_REG_COLORS_DIRECT_V2 (0x8100)`
  (30 bytes / 10 LEDs) or `0x8000` (15 bytes / 5 LEDs), then `ENE_REG_APPLY (0x80A0) = 0x01`.
- **LED count:** config table `0x1C00`, offset `0x02`.
- Mode/speed/direction registers `0x8021/0x8022/0x8023` also documented.

This is enough to write `GSkillCue.GSkillRam` once bus access is proven.

## 4. The blocker — and a new complication

**PawnIO has never loaded on this system.** Both prior OpenRGB runs logged
`Could not open PawnIO … initialization aborted` and detected **zero** I2C/DIMM devices. So we have **no evidence
yet** that the DIMM RGB controllers are reachable from the OS on this X870E. That's the Phase 0 question and it's
still open — it needs PawnIO installed + OpenRGB run elevated.

**New complication found while probing:** three RGB stacks are running **right now**, and SMBus allows only one
owner at a time:

- Corsair **iCUE** (`iCUE`, `Corsair.Service`, plugin hosts)
- ASUS **Armoury Crate** (`ArmourySocketServer`, `ArmourySwAgent`, `ASUS MB Manager`, `asus_framework`,
  `AsusCertService`) — and the **Aura DRAM Component** is installed, i.e. Armoury Crate almost certainly already
  drives this RAM
- **SignalRGB** (`SignalRgbService`) — also supports Trident Z5 DDR5

Fighting all three for raw SMBus ownership is the exact scenario behind the documented DDR5 "SPD corruption →
won't boot" reports.

## 5. Recommendation — switch the backend to the ASUS Aura SDK

Given that **Armoury Crate already owns the SMBus and already controls this RAM**, the safe, robust design is:

```
iCUE SDK  (read colors from iCUE Link Hub)  ──►  GSkillCue bridge  ──►  ASUS Aura SDK  (write RAM colors)
```

- **Zero direct SMBus access from us** → **zero brick risk**, no PawnIO, no admin driver.
- Aura SDK (`AuraServiceLib`, COM, shipped with Armoury Crate's Lighting Service — already installed here)
  enumerates the DRAM as an addressable Aura device and takes per-LED colors.
- Cost: Armoury Crate / Lighting Service must keep running (it already does), and you'd stop SignalRGB from also
  grabbing the RAM.

The native PawnIO/SMBus build from the approved plan still works as a fallback, but on *this* machine it means
disabling Armoury Crate + SignalRGB and accepting the DDR5 probe risk.

### Options

| # | Backend | Brick risk | Needs running | Notes |
|---|---|---|---|---|
| A | **ASUS Aura SDK** (recommended) | none | Armoury Crate + iCUE | Aura owns bus; we only send colors |
| B | Native PawnIO/SMBus (original plan) | low-but-real on DDR5/AM5 | iCUE only; must **close** Armoury Crate + SignalRGB | Full control, no Armoury Crate dependency |
| C | Phase-0 probe first, then choose | probe carries the risk | — | Install PawnIO, close other RGB apps, run OpenRGB elevated, see if DIMMs appear + capture addresses |
