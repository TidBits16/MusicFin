<div align="center">

<p align="center">
  <img src="backdrop.svg" alt="MusicFin backdrop" width="100%">
</p>

# MusicFin: Smarter Music Tagging

Problem: most music taggers <strong><em>kinda suck</em></strong>.

They don't take into context the full album when identifying tracks so your music collection looks like this:

<p align="center">
  <img src="repo_graphics/problems.jpg" alt="The Problem" width="100%">
</p>

I got tired of manually sorting tracks and made a plugin that identifies an artists' full discographies and sorts the tracks into the most logical order:

<p align="center">
  <img src="repo_graphics/resolution.jpg" alt="The Resolution" width="100%">
</p>
Finally, I have music that's sorted correctly!

Works with Singles & EPs!

Works with combo-albums!

Works even if you don't have the full album!

Works with "Live" albums!

Auto sorts singles and EPs when an artist releases a new album!


## Installing
<strong>Step 1</strong>
<p align="center">
  <img src="repo_graphics/plugins.jpg" alt="Plugins Location" width="100%">
</p>

<strong>Dashboard --> Plugins --> Manage Repositories</strong> --> <strong>+ New Repository</strong>:<br>
Name: <code>FinPlugins</code> (or whatever :P )<br>
URL: <code>https://raw.githubusercontent.com/TidBits16/FinPlugins/main/manifest.json</code><br>
<br>
(p.s. this bundle includes my other FinPlugins since they are designed to work together. <strong><em>they are not required to install!</em></strong>)<br>
For just <strong>MusicFin</strong> you can use this URL: <code>https://raw.githubusercontent.com/TidBits16/MusicFin/main/manifest.json</code>
<br>
<br>
<strong>Then Restart JellyFin!</strong>

<strong>Step 2</strong>
<p align="center">
  <img src="repo_graphics/where_to_find.jpg" alt="Where To Find Repo" width="100%">
</p>

<strong>Plugins</strong> --> <strong>All</strong> --> <strong>MusicFin: Smarter Music Tagging</strong> --> <strong>Install</strong><br>
<br>
<strong>Once Installed, Restart JellyFin Again!</strong></center>

## Build Locally

For development or packaging your own build:

```bash
dotnet build Jellyfin.Plugin.DeezerTagger.csproj -c Release
./scripts/package.sh
```

The release zip will be in `dist/`.

Designed for <strong>Jellyfin 10.11+</strong> (you probably have this already :D)
<br>
Licensed under the <a href="LICENSE">GNU General Public License v3.0</a>
<p align="center">
  <a href="https://github.com/TidBits16/MusicFin"><img src="repo_graphics/musicfin.svg" alt="MusicFin" width="72" height="72"></a>
  &nbsp;
  <a href="https://github.com/TidBits16/ExplicitFin"><img src="repo_graphics/explicitfin.svg" alt="ExplicitFin" width="72" height="72"></a>
  &nbsp;
  <a href="https://github.com/TidBits16/LyricFin"><img src="repo_graphics/lyricfin.svg" alt="LyricFin" width="72" height="72"></a>
  &nbsp;
  <a href="https://github.com/TidBits16/ArtistFin"><img src="repo_graphics/artistfin.svg" alt="ArtistFin" width="72" height="72"></a>
</p>
<p align="center"><a href="https://github.com/TidBits16/FinPlugins">Check out these other plugins!</a></p>
</div>
