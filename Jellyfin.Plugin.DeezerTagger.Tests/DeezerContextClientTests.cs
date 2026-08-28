using System.Text.Json;
using Jellyfin.Plugin.DeezerTagger;
using Xunit;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class DeezerContextClientTests
{
    [Fact]
    public void ShouldFetchAlbumFromList_RejectsVariousArtistsCompilation()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "id": 1,
              "title": "Best Pop Songs of 2005!",
              "record_type": "compilation",
              "artist": { "id": 99, "name": "Various Artists" }
            }
            """);

        Assert.False(DeezerContextClient.ShouldFetchAlbumFromList(doc.RootElement, "Cool Band"));
    }

    [Fact]
    public void ShouldFetchAlbumFromList_AcceptsArtistStudioAlbum()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "id": 2,
              "title": "Cool Album",
              "record_type": "album",
              "artist": { "id": 10, "name": "Cool Band" }
            }
            """);

        Assert.True(DeezerContextClient.ShouldFetchAlbumFromList(doc.RootElement, "Cool Band"));
    }

    [Fact]
    public void ShouldFetchAlbumFromList_AcceptsArtistGreatestHitsCompilation()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "id": 3,
              "title": "Greatest Hits",
              "record_type": "compilation",
              "artist": { "id": 10, "name": "Pop Star" }
            }
            """);

        Assert.True(DeezerContextClient.ShouldFetchAlbumFromList(doc.RootElement, "Pop Star"));
    }

    [Fact]
    public void ShouldFetchAlbumFromList_AcceptsEps()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "id": 4,
              "title": "What No One's Thinking",
              "record_type": "ep",
              "artist": { "id": 10, "name": "AJR" }
            }
            """);

        Assert.True(DeezerContextClient.ShouldFetchAlbumFromList(doc.RootElement, "AJR"));
    }

    [Fact]
    public void ShouldFetchAlbumFromList_AcceptsSingles()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "id": 5,
              "title": "Hit Song",
              "record_type": "single",
              "artist": { "id": 10, "name": "Cool Band" }
            }
            """);

        Assert.True(DeezerContextClient.ShouldFetchAlbumFromList(doc.RootElement, "Cool Band"));
    }
}
