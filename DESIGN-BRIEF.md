# BandPilot — UI Design Brief

A complete specification of the existing application, written to be handed to a
designer or a design tool. Everything below describes what exists today in
working, shipped code. The goal is a redesign of the visual layer, not a change
of function.

**Repository:** https://github.com/wrstt/BandPilot
**Release:** https://github.com/wrstt/BandPilot/releases/tag/v1.0.0

---

## 1. What the product is

BandPilot is a Windows desktop utility that lets you **choose which Wi-Fi radio you connect to.**

A network name is not a radio. On a hotel, campus, office or apartment network,
one SSID like `Red Roof Inn` is really a 2.4 GHz radio, a 5 GHz radio and often a
6 GHz radio, on *every* access point in the building — sometimes forty radios
behind one name. Windows silently picks one for you, frequently a congested
2.4 GHz radio two floors away, and gives you no way to see which one you got or
to change it.

BandPilot lists every radio separately and connects you to the one you pick.

It exists because Intel's Killer Performance Suite has this feature but refuses
to install on anything that is not a Killer-branded adapter — including Intel's
own BE200 / BE201 / BE202 Wi-Fi 7 cards. BandPilot is an independent
reimplementation built on public Windows APIs, sharing no Intel code.

### The emotional core

The user is usually on bad hotel or public Wi-Fi, mildly frustrated, and wants
one thing: **"why is this slow, and which one should I be on?"** The interface
should answer that in the first two seconds of looking at it. Everything else is
secondary.

### The single most important design insight

The app's whole thesis is that **the obvious choice is usually wrong.** The
strongest signal is frequently the worst connection, because 2.4 GHz travels
furthest but is narrow and crowded. The interface must actively steer people
away from the strong-but-bad option toward the moderate-but-good one.

This is why there is a **Rating** column that is deliberately *not* signal
strength. Raw signal is worth only 60 of its 100 points; band membership is worth
up to 32 and Wi-Fi generation up to 8. A 6 GHz radio at -70 dBm outranks a
2.4 GHz radio at -50 dBm, because in practice it is faster.

**If the redesign does one thing well, it should make that contradiction
immediately visible** — the row you are currently on being ranked below a row you
are not on is the moment the product justifies itself.

---

## 2. Who uses it

- Anyone on shared Wi-Fi who suspects they are on the wrong band: hotels, dorms, offices, apartment buildings, conferences.
- PC builders who bought an Intel BE200 and discovered Killer software will not install.
- Gamers and streamers who want traffic prioritisation without a subscription tool.

Assume moderate technical confidence. They know what 5 GHz means. They do not
necessarily know what DSCP, BSSID or EHT mean — those terms appear in the UI and
should be presented so they can be understood from context or ignored safely.

---

## 3. Platform and implementation constraints

**Read this before designing. It defines the ceiling.**

The app is **C# / .NET 8 / WinForms**, built entirely in code with no designer
files and no XAML. It is a single self-contained 68 MB executable that runs
elevated (administrator) on Windows 10 and 11, 64-bit.

### Cheap in WinForms
Flat colour fills, borders, custom fonts, per-cell text colour in list views,
list grouping, flat buttons, standard combo boxes / text boxes / checkboxes,
fixed and proportional layout, custom-painted panels and rows via owner-draw.

### Expensive or awkward in WinForms
Rounded corners, drop shadows, gradients, opacity and blur, animation and
transitions, custom-styled scroll bars, custom-styled combo box popups, crisp
vector icons at arbitrary DPI, anything resembling a CSS transform.

### The escape hatch
**Design for the ideal, not the constraint.** If the strongest design needs
rounded cards, animation, real iconography or smooth transitions, say so
explicitly and the implementation can migrate from WinForms to **WPF**, which
handles all of it natively. That migration is a known, bounded cost and is worth
paying for a materially better interface.

So: propose the best design. Annotate anything you believe exceeds the cheap
list, so the trade-off is a deliberate decision rather than a surprise.

### Window
- Default size 1120 × 720, minimum 980 × 640, freely resizable
- Must degrade gracefully down to the minimum; the AP list is the element that should absorb extra space
- Dark theme only at present. A light theme is welcome as an addition, not a replacement.

---

## 4. Current visual language

A dark, flat, GitHub-adjacent palette. Treat it as a starting point, not a
constraint — a stronger palette is welcome, but band colours must stay
distinguishable from each other and from the status colours.

| Token | Hex | Use |
|---|---|---|
| Background | `#16181D` | Window background |
| Surface | `#1E2128` | Sidebar, cards, list background |
| SurfaceAlt | `#262A33` | Inputs, active nav item, current-row highlight |
| Border | `#343944` | 1px borders |
| Text | `#E2E6EE` | Primary text |
| TextDim | `#929AA8` | Secondary text, labels, hints |
| Accent | `#58A6FF` | Primary buttons, active nav, download figures |
| Good | `#56D38A` | Healthy state, upload figures, rating ≥ 58 |
| Warn | `#E8B34C` | Caution, rating 38–57 |
| Bad | `#EE6A6A` | Errors, rating < 38 |
| Band 6 GHz | `#A78BFA` | 6 GHz rows |
| Band 5 GHz | `#58A6FF` | 5 GHz rows |
| Band 2.4 GHz | `#E8B34C` | 2.4 GHz rows |

Typography today: Segoe UI 9pt body, Segoe UI Semibold 13pt page titles, Segoe UI
Semibold 15pt wordmark, Consolas 9pt for MAC addresses. Signal strength renders
as four block characters, `▮▮▮▯`.

Note the collision: 5 GHz and Accent are the same blue, and 2.4 GHz and Warn are
the same amber. Worth resolving.

---

## 5. Application shell

A fixed **208 px left sidebar** on `Surface`, with a content area filling the rest.

Sidebar contents, top to bottom:
1. Wordmark **"BandPilot"** (15pt semibold)
2. Subtitle **"Wi-Fi control for Intel BE2xx"** (8pt, dim)
3. Five nav items, each full-width, 40 px tall, 2 px apart, left-aligned text:
   - **Bands & APs**
   - **Adapter**
   - **Priority & limits**
   - **Live traffic**
   - **About**
4. A separated utility button: **"Enable QoS marking"**

Active nav item today: `SurfaceAlt` background, `Accent` text, bold. Inactive:
`Surface` background, `TextDim` text. There are no icons — adding a coherent icon
set would be a clear improvement.

---

## 6. Page 1 — Bands & APs

**The primary screen and roughly 80% of the product's value.** It opens here.
If time is limited, spend it all on this page.

### Structure, top to bottom

**A. Adapter bar (~40 px)**
- Label "Wi-Fi adapter"
- Dropdown, ~420 px, listing wireless adapters by description, e.g. `Intel(R) Wi-Fi 7 BE200 320MHz`. Auto-selects a Wi-Fi 7 card when several exist. Frequently only one entry — consider collapsing to plain text in that case.
- Checkbox **"Only show the network I'm on"** — cuts a 40-row list to 3–4 rows. Heavily used; arguably should default on.

**B. Current connection banner (~74 px, `Surface`)**
- Line 1, 13pt: the SSID, e.g. `Red Roof Inn`. Reads `Not connected` when idle.
- Line 2, dim, currently one long run-on string:
  `5 GHz, channel 44  ·  Wi-Fi 6 (ax)  ·  signal 61%  ·  390 Mbps down / 390 Mbps up  ·  AP 9C:1E:95:4A:2B:C1`

  This line is dense and hard to scan. Breaking it into labelled stats would be a
  strong improvement. **It should also carry a judgement** — if you are on a
  radio that ranks below an available one, this banner is the right place to say
  so.

**C. Access point list (fills remaining space)**

A grouped table. One group per network, one row per radio.

Group header today: `Red Roof Inn   (4 radios)   — connected`

Columns:

| Column | Width | Content | Colour |
|---|---|---|---|
| Band | 90 | `2.4 GHz` / `5 GHz` / `6 GHz`, prefixed `●` when current | Band colour |
| Ch | 55 | Channel number, right aligned | Text |
| Signal | 90 | `▮▮▮▯` (0–4 bars) | Rating colour |
| dBm | 60 | e.g. `-68`, right aligned | Text |
| Generation | 110 | `Wi-Fi 7 (be)`, `Wi-Fi 6 (ax)`, `Wi-Fi 5 (ac)`, `Wi-Fi 4 (n)` | Text |
| Access point (BSSID) | 170 | `9C:1E:95:4A:2B:C1`, monospace | Text |
| Rating | 70 | `0`–`100`, right aligned | Rating colour |

Rows sort by rating descending within each group. The connected row gets a
`SurfaceAlt` background and bold band cell. Double-clicking a row connects to it.

**D. Action bar (~44 px)**
- **Rescan** (secondary, ~110 px) — takes about 4.5 seconds; currently only disables the button, with no progress indication
- **Connect to this access point** (primary, ~240 px)
- **Back to automatic** (secondary, ~170 px) — hands the choice back to Windows

### Realistic sample data

Use this for mockups. It is a true-to-life hotel network, and note that the row
the user is currently connected to ranks **third of four** — this is the exact
situation the product exists to reveal.

Network: **Red Roof Inn** (4 radios) — connected

| | Band | Ch | Signal | dBm | Generation | BSSID | Rating |
|---|---|---|---|---|---|---|---|
| | 6 GHz | 37 | ▮▮▯▯ | -68 | Wi-Fi 7 (be) | 9C:1E:95:4A:2B:C2 | **66** |
| | 5 GHz | 44 | ▮▮▮▯ | -59 | Wi-Fi 6 (ax) | 9C:1E:95:4A:2B:C1 | **60** |
| ● | 2.4 GHz | 6 | ▮▮▮▮ | -52 | Wi-Fi 6 (ax) | 9C:1E:95:4A:2B:C0 | **51** |
| | 5 GHz | 149 | ▮▮▯▯ | -71 | Wi-Fi 6 (ax) | 9C:1E:95:52:7F:81 | **46** |

Second network: **Red Roof Guest** (2 radios)

| | Band | Ch | Signal | dBm | Generation | BSSID | Rating |
|---|---|---|---|---|---|---|---|
| | 5 GHz | 36 | ▮▮▯▯ | -74 | Wi-Fi 5 (ac) | 4A:2B:C0:11:9F:03 | 40 |
| | 2.4 GHz | 11 | ▮▮▮▯ | -63 | Wi-Fi 4 (n) | 4A:2B:C0:11:9F:02 | 32 |

Observe: the user has four full signal bars and is on the worst useful radio in
the building. **Making that legible at a glance is the design problem.**

### States to cover
- **Connected** — as above
- **Not connected** — banner reads `Not connected`, list still populated
- **Scanning** — 4.5 s after Rescan; currently unindicated
- **Empty** — no networks in range
- **Connecting** — 3.5 s after clicking connect; banner currently shows `Connecting to 6 GHz channel 37 on 9C:1E:95:4A:2B:C2 ...`
- **No saved profile** — a dialog explaining the network must be joined through Windows once first, so the password is stored
- **Single-radio network** — nothing to choose; group of one

---

## 7. Page 2 — Adapter

Driver-level radio settings, read live from the installed driver.

- Header: the adapter description, 13pt
- Checkbox **"Show every driver setting"** — off by default, showing only band and roaming settings; on, shows all ~30
- Table: **Setting** (300) · **Current value** (220) · **Driver keyword** (220, monospace, dim)
- Below: label **"Change to"**, a dropdown of valid values (~260), **Apply setting** (primary), **Reload**
- A status line at the bottom that turns green on success, amber on caution, red on error

Real rows: `Preferred Band` = `No Preference`; `Roaming Aggressiveness` = `3. Medium`;
`802.11a/b/g Wireless Mode` = `Dual Band`; `Channel Width for 2.4GHz` = `20 MHz`;
`Channel Width for 5GHz` = `Auto`.

The values are whatever the driver reports and differ between driver versions, so
nothing can be hardcoded — the design must tolerate unknown labels and value sets.

Purpose in the user's journey: pinning on page 1 lasts only for the current
connection. **This page is where a choice is made permanent.** `Roaming
Aggressiveness` is the setting that stops Windows drifting back. That relationship
is currently invisible and would benefit enormously from being made explicit.

---

## 8. Page 3 — Priority & limits

Per-application traffic prioritisation and bandwidth caps, stored as standard
Windows QoS policies.

- Title "Priority and limits"
- Table of existing rules: **Rule** (170) · **Application** (200) · **Protocol** (80) · **Remote port** (100) · **Priority** (210) · **Speed limit** (120)
- An editor panel below (~176 px) on `Surface`, six fields in a 2 × 3 grid:
  - **Rule name** — text
  - **Protocol** — dropdown: `*`, `TCP`, `UDP`
  - **Application** — text plus a **Browse…** button opening a file picker; stores the bare filename, e.g. `valorant.exe`
  - **Remote port** — text, `*` or `443` or `27015-27020`
  - **Priority** — dropdown of DSCP presets
  - **Limit (Mbit/s)** — text, empty means unlimited
- Buttons: **Save rule** (primary) · **Delete rule** · **Reload**
- Status line at the bottom

Priority dropdown values, exactly as written today:
```
46 - EF, highest (voice/games)
40 - CS5, very high (video)
34 - AF41, high
26 - AF31, above normal
18 - AF21, slightly raised
0  - default / best effort
8  - CS1, background (deprioritise)
```
Note the ordering oddity: `8` is the *lowest* priority despite being numerically
above `0`. Presenting these as a ranked scale rather than raw DSCP numbers would
be a real usability gain.

Sample rules: `Valorant priority` / `valorant.exe` / `*` / `*` / `46 - EF, highest` / `unlimited`
and `Backup throttle` / `backup.exe` / `TCP` / `*` / `8 - CS1, background` / `20 Mbit/s`.

**Empty state matters here** — most users arrive with zero rules and no idea what
to create. Suggested starter rules would be valuable.

A persistent warning appears when QoS marking is not yet enabled system-wide:
> Priority marking is currently inactive on this PC. Use Tools ▸ Enable QoS marking to turn it on.

---

## 9. Page 4 — Live traffic

Per-process bandwidth, updating once per second.

- Title "Live traffic"
- Totals line: `Down 12.4 Mbit/s   ·   Up 1.1 Mbit/s   ·   14 processes seen`
- Table: **Process** (210) · **PID** (70) · **Download** (110) · **Upload** (110) · **Total down** (110) · **Total up** (110), all numerics right aligned
- Download figures in `Accent`, upload in `Good`, cumulative totals dim
- Sorted by current throughput descending, so rows reorder constantly
- Buttons: **Start monitoring** (primary; becomes **Stop monitoring**) · **Reset totals** · **Create a rule for this app** — the last jumps to page 3 with the process pre-filled

Sample: `chrome` 4821 · 8.2 Mbit/s · 340 kbit/s · 1.20 GB · 88 MB.
`steam` 9102 · 3.9 Mbit/s · 120 kbit/s · 640 MB · 12 MB.
`valorant` 3344 · 210 kbit/s · 180 kbit/s · 44 MB · 38 MB.

Design notes: rows jumping around once a second is visually noisy — some damping
or a sparkline per row would help. A stacked total-throughput graph over time is
an obvious addition. **Off state** is the default: the page shows nothing until
Start is pressed.

---

## 10. Page 5 — About

A single scrolling block of plain text: what the app does, why band choice
matters, how pinning behaves, the QoS caveats, and a disclaimer that the project
is unaffiliated with Intel. Currently unstyled and unloved; any structure at all
would improve it.

---

## 11. Dialogs

All are stock Windows message boxes today and could become in-app panels.

1. **No saved profile** — "There is no saved Windows profile for *X*. Connect to it once through the normal Windows network list, then come back here to choose its band."
2. **Confirm driver change** — "Set *Preferred Band* to *Prefer 5GHz*? The Wi-Fi connection will drop briefly while the radio restarts."
3. **Confirm delete rule**
4. **Enable QoS marking** — explains that Windows ignores priority rules on non-domain PCs until a registry switch is set, names the key, and warns a restart is needed
5. **Errors** — driver or API failures, shown verbatim

---

## 12. What to deliver

Highest value first:

1. **Bands & APs page** — the whole screen, in the connected state, using the sample data above. This is the product.
2. The same page in **scanning**, **empty** and **not connected** states.
3. **Application shell** — sidebar, nav, active state, and an icon set if you propose one.
4. **Live traffic** page.
5. **Priority & limits** page, including its empty state.
6. **Adapter** page.
7. Colour and type scale as tokens, so they can map onto the existing `Theme` class.

### Explicitly open questions
- Is a table the right form for the AP list, or would cards per radio read better?
- How should the rating be shown — a number, a bar, a letter grade, a verdict like "best available"?
- How do we surface "you are on a worse radio than one available" without nagging?
- Should band be conveyed by colour, by icon, by grouping, or by column position?
- Do the 5 GHz/Accent and 2.4 GHz/Warn colour collisions need resolving?
- Should "Only show the network I'm on" default to on?

### Constraints that cannot change
- Windows desktop, dark theme, 1120 × 720 default and resizable to 980 × 640
- The data shown is fixed by what the Windows API provides; no invented fields
- Band, channel, signal, generation, BSSID and rating must all remain visible somewhere for each radio
- Text must be selectable/copyable where it is an identifier (BSSID especially)

---

## 13. Handing the result back

Return whatever is natural — mockups, a component inventory, tokens, or HTML/CSS.
Annotate anything from the "expensive in WinForms" list so the trade-off between
staying on WinForms and migrating to WPF can be made deliberately.

The existing implementation lives in `src/Ui/` — `Theme.cs` holds every colour and
font, and each page is one file (`BandsPage.cs`, `AdapterPage.cs`,
`PriorityPage.cs`, `MonitorPage.cs`, `MainForm.cs`), so a redesign maps cleanly
onto the existing structure.
