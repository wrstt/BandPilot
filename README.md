# BandPilot

**Choose which Wi-Fi band and which access point you connect to — on Intel BE200 / BE201 / BE202 (Wi-Fi 7) adapters.**

Windows hides the single most useful Wi-Fi decision from you. On a hotel, campus, office or apartment network, one name like `Red Roof Inn` is not one radio — it is a 2.4 GHz radio, a 5 GHz radio and often a 6 GHz radio, on *every* access point in the building. Windows silently picks one, frequently a congested 2.4 GHz radio two floors away, and offers no way to see which one you got or to change it.

BandPilot shows you every radio separately and connects you to the one you pick.

<!-- Screenshot placeholder: add a capture of the Bands page here. -->

---

## Why this exists

Intel's Killer Performance Suite has this feature, but it refuses to install unless it finds a Killer-branded adapter. The BE200 series is Intel's own Wi-Fi 7 silicon and is *not* Killer-branded, so the suite ignores it.

BandPilot is an independent, open-source reimplementation of the parts that matter, written from scratch against public Windows APIs. It shares no code with Intel's software.

---

## Features

### Bands & APs — the main event
Lists every BSS (one entry per AP radio) grouped by network, showing band, channel, signal, Wi-Fi generation and BSSID. Pick one, click connect, and you are pinned to that exact radio.

The **Rating** column is an opinionated guide rather than raw signal strength. Signal is only worth 60 of the 100 points, because a 2.4 GHz radio at -50 dBm is usually *slower* in practice than a 6 GHz one at -70 dBm — 2.4 GHz is narrow and crowded. Letting raw RSSI dominate would recommend exactly the wrong radio, which is the mistake this tool exists to prevent.

### Adapter
The driver's own radio settings — preferred band, roaming aggressiveness, channel widths — read live from the installed driver. Nothing is hardcoded to a driver revision, because the available keywords and accepted values genuinely differ between Intel driver versions for the same card. Use this to stop Windows wandering back to 2.4 GHz an hour after you pin.

### Priority & limits
Per-application traffic prioritisation (DSCP marking) and bandwidth caps, implemented as standard Windows QoS policies written straight to the registry — so they work on Home editions, which have no `gpedit.msc`.

### Live traffic
Per-process upload and download rates via an ETW kernel session. Windows exposes no per-process network performance counter, so listening to kernel TCP/IP events is the only way to attribute bytes to a PID. Select a process and jump straight to creating a priority rule for it.

---

## Install

### Option A — download the release
Grab `BandPilot.exe` from the [Releases](../../releases) page. It is a single self-contained file with no prerequisites; the .NET runtime is bundled.

Right-click ▸ **Run as administrator**.

> Windows SmartScreen will warn about an unsigned executable from an unknown publisher. That is expected for an unsigned open-source binary. Click **More info ▸ Run anyway**, or build it yourself from source below.

### Option B — build from source
Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/wrstt/BandPilot.git
cd BandPilot
.\build.ps1
```

The binary lands in `dist\BandPilot.exe`.

---

## Requirements

- Windows 10 or 11, 64-bit
- A Wi-Fi adapter (built for Intel BE2xx, but the band picker works on any adapter Windows supports)
- Administrator rights — required to pin a BSSID, write QoS policy, and open an ETW session
- The network must already be saved in Windows, since BandPilot reuses the stored credentials rather than asking for them

---

## How to use it

1. Connect to the network normally through Windows once, so the password is saved.
2. Open BandPilot as administrator. It preselects your Wi-Fi 7 adapter if you have more than one radio.
3. On **Bands & APs**, tick *Only show the network I'm on* to cut the noise.
4. You will typically see several rows under one network name. The `●` marks where you are now.
5. Pick a row — usually the highest-rated 5 GHz or 6 GHz entry — and click **Connect to this access point**.
6. If Windows drifts back later, open **Adapter** and lower *Roaming Aggressiveness*.

---

## Caveats worth knowing

**Pinning is per-connection, not permanent.** It survives until the adapter roams, the radio resets, or you reconnect. The Adapter page settings are the standing preferences that make it stick.

**Your driver has the final say.** `WlanConnect` with a desired-BSSID list is a strong constraint, but some drivers treat it as a hint and will still roam under a weak signal. This is a driver behaviour, not something an application can override.

**QoS priority needs two things you might not have.** Windows ignores DSCP policies on non-domain-joined PCs until a registry switch is set — use *Enable QoS marking* in the sidebar, then restart. Beyond that, DSCP marks only help end-to-end if your router honours them; many consumer routers ignore or strip them. Bandwidth *limits* apply locally and work regardless.

**6 GHz needs everything to agree.** Your card, your driver, your router and your regulatory region all have to support 6 GHz for those radios to appear at all.

---

## Project layout

```
src/
  Native/WlanApi.cs        P/Invoke for wlanapi.dll (structs, enums, entry points)
  Wifi/WifiService.cs      Scanning, BSS enumeration, BSSID-pinned connect
  Wifi/BandTools.cs        Frequency to band/channel, rating, formatting
  Adapter/                 Driver settings via the NetAdapter PowerShell module
  Qos/QosManager.cs        Windows QoS policy read/write
  Monitor/                 ETW per-process bandwidth accounting
  Ui/                      WinForms UI, built in code (no designer files)
tests/LayoutTests/         Struct layout and band-math verification
tools/Show-WifiBands.ps1   No-install PowerShell band viewer
```

### About the tests

A wrong struct size or field offset in the Native Wifi layer does not crash — it silently yields plausible but incorrect signal strengths, channels and BSSIDs, which is far worse than a crash. `tests/LayoutTests` asserts every size and offset against the C headers, plus the channel arithmetic and the rating weights. It targets plain `net8.0`, so it runs on any OS:

```bash
cd tests/LayoutTests
dotnet run -c Release
```

This caught a real bug during development: the structs holding `WCHAR[256]` fields defaulted to ANSI marshalling, which would have halved their size and broken every read.

---

## What this is not

BandPilot does **not** replicate DoubleShot Pro (bonding Wi-Fi and Ethernet simultaneously). That genuinely depends on proprietary Killer driver behaviour and cannot be reimplemented from user space.

---

## Not affiliated with Intel

BandPilot is independent software written against publicly documented Windows APIs. It contains no Intel code, is not derived from Intel Killer Performance Suite, and is not endorsed by or affiliated with Intel Corporation. "Killer" and "Intel" are trademarks of their respective owners, used here only to describe compatibility.

## Licence

MIT — see [LICENSE](LICENSE).
