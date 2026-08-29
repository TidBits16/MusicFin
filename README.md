# MusicFin: Smarter Music Tagging

A Jellyfin plugin that fixes messy music libraries by matching your files against real artist discographies — not song-by-song guessing.

**Jellyfin 10.11+**

## The problem

Most taggers trust whatever album name is already on each file. One wrong tag spreads: tracks land on the wrong album, track numbers drift, and genres get applied inconsistently. Compilations and split releases make it worse.

## What this does

For each album artist in your library, the plugin pulls that artist's catalog, finds which release your tracks actually belong to, and writes the correct metadata — album names, track order, years, genres, and artist tags. Combined releases win when you own songs from both halves; various-artists comps are skipped.

Dry run is on by default, so the first pass only logs what it would change.

## Install

1. **Dashboard → Plugins → Repositories** → add:
   - Name: `Fin Plugins`
   - URL: `https://raw.githubusercontent.com/TidBits16/FinPlugins/main/manifest.json`
2. **Catalog** → refresh → install **MusicFin: Smarter Music Tagging** → restart when prompted.
3. Open **Plugins → MusicFin: Smarter Music Tagging** to configure, or run it from **Scheduled Tasks**.

(That same repository URL also lists ExplicitFin and LyricFin.)

After a successful run, scan your music library so Jellyfin picks up the changes.

## Build locally

For development or packaging your own build:

```bash
dotnet build Jellyfin.Plugin.DeezerTagger.csproj -c Release
./scripts/package.sh
```

The release zip lands in `dist/`.
