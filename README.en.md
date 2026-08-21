# Z-Codex

*[Version française](README.md)*

A team build manager for **Guild Wars 1**, spiritual successor to paw\*ned².

Z-Codex lets you put together a party of 8 characters, simulate its mechanics
(damage, spikes, armor, energy, conditions) and exchange builds in the format the
game expects.

Unofficial, free, and unaffiliated with ArenaNet or NCSOFT.

Author: **P. Vincent**.

---

## What it does

**Party building**
- 8 characters × 8 skills, primary and secondary professions, attributes
- Build variants organised as a tree, tied together by locks
- Filterable skill catalogue, search, detailed tooltips

**Simulation**
- **Spike** — damage of a coordinated burst: cast order, life stealing, Deep Wound,
  critical hits, weapon buffs, Grenth effects, conditional and threshold damage
- **Damage vs. armor** — AL 60/80/100/120 and custom levels, armor-ignoring damage,
  penetration, weapon damage of attack skills
- **Armor calculator** — insignias, runes, resistances, temporary effects
- **Energy** — Expertise, Nature Rituals (Quickening Zephyr, Ether Well…),
  attribute boosts (Aura of the Lich, Master of Magic…)
- **Conditions** — band of the 10 conditions, effective and reduced durations
- **Flux** — the 12 monthly Flux, and their impact on the calculations
- **Spawning Power** — weapon spell duration, health and armor of spirits and minions

**Comfort**
- **Community builds** — fetch the PvXwiki build packs from the Extras menu: ~1,460
  builds and ~250 teams, dropped wherever you like, in the format Guild Wars itself
  reads
- **French and English** interface, switchable on the fly (flags at the top right)
- Light and dark themes
- Three icon sizes
- Screenshot of a build or a party, ready to paste
- Undo / redo
- File browser with preview, without opening the build

## File formats

| Extension | Purpose | Access |
|---|---|---|
| `.zcx` | native format — full party, equipment, settings | read / write |
| `.pn3` | previous native format | read / write |
| `.pwnd` | paw\*ned² files | read / write |
| `.txt` | in-game template code — skills (`O…`) or equipment (`P…`) | read / write |

Template codes copy and paste straight to and from Guild Wars.

## Installing

Windows 10 or 11, 64-bit. No prerequisites: the .NET runtime ships with the
application.

Download the latest version from the
[releases page](https://github.com/LuxIudicium/Z-Codex/releases/latest).

Run the `Z-Codex-…-setup.exe` you downloaded and follow the wizard. Z-Codex installs **for your
account only**, under `%LocalAppData%\Programs\Z-Codex`, so it needs no
administrator rights.

To remove it, use **Settings ▸ Apps** or the Start menu shortcut. The uninstaller
asks whether to keep your builds and the downloaded catalogue; answering *yes*
saves you the initial download should you ever reinstall.

### First launch

**An internet connection is required the first time you start Z-Codex.**

Z-Codex distributes no Guild Wars data and no Guild Wars images. It fetches them
itself, on your machine, from the public
[Guild Wars Wiki](https://wiki.guildwars.com): 1,507 skills, their progression
tables, their French texts and their icons — about 8 MB.

**Expect about five minutes**, with a progress window on screen throughout. The
request rate is deliberately capped so as not to strain the wiki, which is a free
community service. This happens only once: afterwards the application starts in a
few seconds and works offline.

If the download is interrupted, restart it from **Extras ▸ Update skills**.

Once a game update is released, Z-Codex detects it and offers to refresh its
catalogue. The refresh can also be triggered manually from the same menu entry.

### Where your data lives

Everything sits in `%AppData%\Z-Codex`:

```
zcodex.db          skill catalogue
settings.json      display preferences
icons\             skill icons
professions\  conditions\  stats\  flux.jpg
armor\  weapons\   equipment images, downloaded on demand
crash.log          only after an error — useful for a bug report
```

Your builds are saved wherever you choose.

Deleting this folder resets the application; it will download everything again on
the next launch.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
git clone https://github.com/LuxIudicium/Z-Codex.git
cd Z-Codex
dotnet build Z-Codex.sln
dotnet run --project src/ZCodex.App
```

To produce the self-contained build that the installer packages:

```
dotnet publish src/ZCodex.App -c Release -p:PublishProfile=win-x64-selfcontained
```

Output lands in `src/ZCodex.App/bin/publish/win-x64/`.

### Building the installer

Additionally requires [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`).

```
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

The script chains the publish, the generation of the wizard artwork and the
compilation, and drops `installer/output/Z-Codex-<version>-setup.exe` — roughly
50 MB for a 155 MB payload. Use `-SkipPublish` and `-SkipImages` to iterate on the
wizard alone.

The version number shown everywhere is read from the published executable: change
`<Version>` in `src/ZCodex.App/ZCodex.App.csproj` and nothing else.

The release procedure — tagging, GitHub release and checking that update
detection works — is documented in [RELEASING.md](RELEASING.md) (French).

### Code layout

| Project | Contents |
|---|---|
| `ZCodex.App` | WPF interface, views and view models |
| `ZCodex.Core` | models, game calculations, template codecs |
| `ZCodex.Data` | SQLite database (Entity Framework Core) |
| `ZCodex.Scraper` | reading the public wiki |

Source comments are in French.

## Licence and trademarks

The source code is released under the **MIT licence**, © 2026 P. Vincent (see
[LICENSE](LICENSE)).

Guild Wars, its expansions, its skills, its icons and its imagery belong to
ArenaNet, LLC and NCSOFT Corporation. **No game asset is redistributed with this
software**: assets are downloaded at runtime, on the user's own machine, from the
public wiki, and remain subject to that wiki's terms of reuse.

Z-Codex is an independent work, inspired by paw\*ned² but sharing none of its source
code. Its `.pwnd` support is a codec written from the file format alone, and reads as
well as writes it.

Community builds come from [PvXwiki](https://gwpvx.fandom.com), an independent wiki
hosted by Fandom. Written by its contributors, they are licensed under
**CC BY-NC-SA 3.0**: Z-Codex downloads them at runtime, on the user's own machine, and
redistributes none of them. They stay under that licence once imported — credit
PvXwiki if you share them on, and no commercial use.

Full notices are in [LICENSE](LICENSE).
