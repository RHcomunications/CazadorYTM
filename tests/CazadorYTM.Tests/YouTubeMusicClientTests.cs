namespace CazadorYTM.Tests;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CazadorYTM.Core.Services;

public class YouTubeMusicClientTests : IDisposable
{
    private readonly string _tempDir;

    public YouTubeMusicClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cazador_ytm_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Parser_HistoryShelves_MapsTrackAndSkipsMissingMetadata()
    {
        const string json = """
        {
          "contents": {
            "singleColumnBrowseResultsRenderer": {
              "tabs": [ { "tabRenderer": { "content": { "sectionListRenderer": { "contents": [
                {
                  "musicShelfRenderer": {
                    "title": { "runs": [ { "text": "Esta semana" } ] },
                    "contents": [
                      {
                        "musicResponsiveListItemRenderer": {
                          "thumbnail": { "musicThumbnailRenderer": { "thumbnail": { "thumbnails": [
                            { "url": "https://img/60.jpg", "width": 60, "height": 60 },
                            { "url": "https://img/400.jpg", "width": 400, "height": 225 }
                          ] } } },
                          "flexColumns": [
                            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Pardesiya" } ] } } },
                            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
                              { "text": "Sonu Nigam" },
                              { "text": ", " },
                              { "text": "Krishnakali Saha" },
                              { "text": " y " },
                              { "text": "Sachin-Jigar" },
                              { "text": " " },
                              { "text": "151 M de visualizaciones" }
                            ] } } }
                          ],
                          "fixedColumns": [
                            { "musicResponsiveListItemFixedColumnRenderer": { "text": { "runs": [ { "text": "2:58" } ] } } }
                          ],
                          "playlistItemData": { "videoId": "N4wK3NtVRT0" }
                        }
                      }
                    ]
                  }
                },
                {
                  "musicShelfRenderer": {
                    "title": { "runs": [ { "text": "La semana pasada" } ] },
                    "contents": [
                      {
                        "musicResponsiveListItemRenderer": {
                          "flexColumns": [
                            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Saru Kumain" } ] } } }
                          ],
                          "playlistItemData": { "videoId": "hoDV96_yGjE" }
                        }
                      }
                    ]
                  }
                }
              ] } } } } ]
            }
          }
        }
        """;

        var (items, continuation) = Parse(json, RadarSection.History);

        Assert.Equal(2, items.Count);
        Assert.Null(continuation);

        var track = items[0];
        Assert.Equal("N4wK3NtVRT0", track.Id);
        Assert.Equal("Pardesiya", track.Title);
        Assert.Equal("Sonu Nigam, Krishnakali Saha y Sachin-Jigar", track.Artist);
        Assert.Equal(178, track.Duration);
        Assert.Equal("02:58", track.DisplayDuration);
        Assert.Equal("https://music.youtube.com/watch?v=N4wK3NtVRT0", track.Url);
        Assert.Equal("https://img/400.jpg", track.ThumbnailUrl);
        Assert.False(track.IsPlaylist);
        Assert.Equal(RadarSection.History, track.Section);

        var sparse = items[1];
        Assert.Equal("Saru Kumain", sparse.Title);
        Assert.Null(sparse.Artist);
        Assert.Null(sparse.Duration);
        Assert.Null(sparse.ThumbnailUrl);
    }

    [Fact]
    public void Parser_PlaylistGrid_SkipsCreateItemAndNormalizesBrowseId()
    {
        const string json = """
        {
          "contents": {
            "singleColumnBrowseResultsRenderer": {
              "tabs": [ { "tabRenderer": { "content": { "sectionListRenderer": { "contents": [
                {
                  "gridRenderer": {
                    "items": [
                      {
                        "musicTwoRowItemRenderer": {
                          "title": { "runs": [ { "text": "Nueva lista" } ] },
                          "navigationEndpoint": { "createPlaylistEndpoint": { "params": "CAI=" } }
                        }
                      },
                      {
                        "musicTwoRowItemRenderer": {
                          "thumbnailRenderer": { "musicThumbnailRenderer": { "thumbnail": { "thumbnails": [
                            { "url": "https://img/pl.jpg", "width": 210, "height": 210 }
                          ] } } },
                          "title": { "runs": [ { "text": "Música que me gusta" } ] },
                          "subtitle": { "runs": [ { "text": "Lista creada automáticamente" } ] },
                          "navigationEndpoint": { "browseEndpoint": { "browseId": "VLLM" } }
                        }
                      }
                    ],
                    "continuations": [ { "nextContinuationData": { "continuation": "TOKEN-GRID" } } ]
                  }
                }
              ] } } } } ]
            }
          }
        }
        """;

        var (items, continuation) = Parse(json, RadarSection.Playlists);

        var playlist = Assert.Single(items);
        Assert.Equal("VLLM", playlist.Id);
        Assert.Equal("Música que me gusta", playlist.Title);
        Assert.Equal("Lista creada automáticamente", playlist.Artist);
        Assert.Equal("https://music.youtube.com/playlist?list=LM", playlist.Url);
        Assert.Equal("https://img/pl.jpg", playlist.ThumbnailUrl);
        Assert.True(playlist.IsPlaylist);
        Assert.Equal("TOKEN-GRID", continuation);
    }

    [Fact]
    public void Parser_LikedShelf_ExtractsContinuationCommandToken()
    {
        const string json = """
        {
          "contents": {
            "twoColumnBrowseResultsRenderer": {
              "secondaryContents": {
                "sectionListRenderer": {
                  "contents": [
                    {
                      "musicPlaylistShelfRenderer": {
                        "contents": [
                          {
                            "musicResponsiveListItemRenderer": {
                              "flexColumns": [
                                { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Tulasi" } ] } } },
                                { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Sumedh K" } ] } } }
                              ],
                              "fixedColumns": [
                                { "musicResponsiveListItemFixedColumnRenderer": { "text": { "runs": [ { "text": "3:34" } ] } } }
                              ],
                              "playlistItemData": { "videoId": "Y8ZApXcnhy4" }
                            }
                          },
                          {
                            "continuationItemRenderer": {
                              "continuationEndpoint": { "continuationCommand": { "token": "TOKEN-LIKED" } }
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
              }
            }
          }
        }
        """;

        var (items, continuation) = Parse(json, RadarSection.LikedSongs);

        var track = Assert.Single(items);
        Assert.Equal("Tulasi", track.Title);
        Assert.Equal("Sumedh K", track.Artist);
        Assert.Equal(214, track.Duration);
        Assert.Equal("TOKEN-LIKED", continuation);
    }

    [Fact]
    public void Parser_ContinuationResponse_ParsesAppendedItemsAndNextToken()
    {
        const string json = """
        {
          "onResponseReceivedActions": [
            {
              "appendContinuationItemsAction": {
                "continuationItems": [
                  {
                    "musicResponsiveListItemRenderer": {
                      "flexColumns": [
                        { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Synkät soinnut rujot riimit" } ] } } },
                        { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Ruoska" } ] } } }
                      ],
                      "fixedColumns": [
                        { "musicResponsiveListItemFixedColumnRenderer": { "text": { "runs": [ { "text": "3:23" } ] } } }
                      ],
                      "playlistItemData": { "videoId": "EM8h1vV8zLY" }
                    }
                  },
                  {
                    "continuationItemRenderer": {
                      "continuationEndpoint": { "continuationCommand": { "token": "TOKEN-NEXT" } }
                    }
                  }
                ]
              }
            }
          ]
        }
        """;

        var (items, continuation) = Parse(json, RadarSection.LikedSongs);

        var track = Assert.Single(items);
        Assert.Equal("Synkät soinnut rujot riimit", track.Title);
        Assert.Equal(203, track.Duration);
        Assert.Equal("TOKEN-NEXT", continuation);
    }

    [Fact]
    public void Parser_GridContinuationResponse_ParsesItemsAndToken()
    {
        const string json = """
        {
          "continuationContents": {
            "gridContinuation": {
              "items": [
                {
                  "musicTwoRowItemRenderer": {
                    "title": { "runs": [ { "text": "Los 100.9 Mejores Tracks" } ] },
                    "subtitle": { "runs": [ { "text": "Jardguel • 101 pistas" } ] },
                    "navigationEndpoint": { "browseEndpoint": { "browseId": "VLPL0hEIdcgJR5wYMRHWHlx6oE9LVGF7hmD6" } }
                  }
                }
              ],
              "continuations": [ { "nextContinuationData": { "continuation": "TOKEN-GRID-NEXT" } } ]
            }
          }
        }
        """;

        var (items, continuation) = Parse(json, RadarSection.Playlists);

        var playlist = Assert.Single(items);
        Assert.Equal("VLPL0hEIdcgJR5wYMRHWHlx6oE9LVGF7hmD6", playlist.Id);
        Assert.Equal("https://music.youtube.com/playlist?list=PL0hEIdcgJR5wYMRHWHlx6oE9LVGF7hmD6", playlist.Url);
        Assert.Equal("TOKEN-GRID-NEXT", continuation);
    }

    [Fact]
    public void Session_ParsesNetscapeCookiesAndBuildsAuthHeader()
    {
        var path = Path.Combine(_tempDir, "cookies.txt");
        File.WriteAllText(path,
            "# Netscape HTTP Cookie File\n" +
            "#HttpOnly_.youtube.com\tTRUE\t/\tTRUE\t1790000000\tSID\tsid-value\n" +
            ".youtube.com\tTRUE\t/\tTRUE\t1790000000\tSAPISID\tabc123\n" +
            ".google.com\tTRUE\t/\tTRUE\t1790000000\tNID\tnid-value\n" +
            ".example.com\tTRUE\t/\tTRUE\t1790000000\tOTHER\tignored\n" +
            ".youtube.com\tTRUE\t/\tTRUE\t1790000000\tPREF\tdup-1\n" +
            ".youtube.com\tTRUE\t/\tTRUE\t1790000000\tPREF\tdup-2\n");

        var session = YouTubeMusicSession.TryCreate(path);

        Assert.NotNull(session);
        Assert.Equal("abc123", session!.Sapisid);
        Assert.Equal(0, session.AccountIndex);
        Assert.Contains("SID=sid-value", session.CookieHeader);
        Assert.Contains("SAPISID=abc123", session.CookieHeader);
        Assert.Contains("NID=nid-value", session.CookieHeader);
        Assert.Contains("PREF=dup-2", session.CookieHeader);
        Assert.DoesNotContain("OTHER", session.CookieHeader);

        var header = session.CreateAuthorizationHeader(DateTimeOffset.FromUnixTimeSeconds(1700000000));
        Assert.Equal("SAPISIDHASH 1700000000_597a8411c1c01f9c14cea84b6ec377b76d93be6e", header);
    }

    [Fact]
    public void Session_MissingSapisid_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "cookies.txt");
        File.WriteAllText(path, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t1790000000\tSID\tsid-value\n");

        Assert.Null(YouTubeMusicSession.TryCreate(path));
        Assert.Null(YouTubeMusicSession.TryCreate(Path.Combine(_tempDir, "missing.txt")));
        Assert.Null(YouTubeMusicSession.TryCreate(null));
    }

    [Theory]
    [InlineData("https://music.youtube.com/playlist?list=LM", "VLLM")]
    [InlineData("https://music.youtube.com/playlist?list=PL0hEIdcg", "VLPL0hEIdcg")]
    [InlineData("https://music.youtube.com/playlist?list=VLPL0hEIdcg", "VLPL0hEIdcg")]
    [InlineData("LM", "VLLM")]
    [InlineData("VLLM", "VLLM")]
    [InlineData("https://music.youtube.com/watch?v=N4wK3NtVRT0", null)]
    [InlineData("", null)]
    public void Client_ToPlaylistBrowseId_NormalizesInput(string input, string? expected)
    {
        Assert.Equal(expected, YouTubeMusicClient.ToPlaylistBrowseId(input));
    }

    [Fact]
    public async Task Client_FetchSectionAsync_CanceledToken_Throws()
    {
        var path = Path.Combine(_tempDir, "cookies.txt");
        File.WriteAllText(path, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t1790000000\tSAPISID\tabc123\n");
        var session = YouTubeMusicSession.TryCreate(path);
        Assert.NotNull(session);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new YouTubeMusicClient(session!);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchSectionAsync(RadarSection.History, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RadarService_FetchRadarItemsAsync_CanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RadarService.FetchRadarItemsAsync(RadarSection.History, cancellationToken: cts.Token));
    }

    [Theory]
    [InlineData(RadarRating.Like, "like/like")]
    [InlineData(RadarRating.Dislike, "like/dislike")]
    [InlineData(RadarRating.None, "like/removelike")]
    public async Task Client_SetTrackRatingAsync_SendsEndpointAndTarget(RadarRating rating, string expectedEndpoint)
    {
        var handler = new RecordingHandler("{}");
        var client = CreateClient(handler);

        await client.SetTrackRatingAsync("abc123", rating);

        Assert.NotNull(handler.LastRequest);
        Assert.Contains($"/youtubei/v1/{expectedEndpoint}", handler.LastRequest!.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.Contains("X-Goog-AuthUser"));
        Assert.True(handler.LastRequest.Headers.Contains("X-Goog-Request-Time"));

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;
        Assert.Equal("WEB_REMIX", root.GetProperty("context").GetProperty("client").GetProperty("clientName").GetString());
        Assert.Equal("abc123", root.GetProperty("target").GetProperty("videoId").GetString());
    }

    [Fact]
    public async Task Client_AddTrackToPlaylistAsync_SendsEditPlaylistAction()
    {
        var handler = new RecordingHandler("{\"status\":\"STATUS_SUCCEEDED\"}");
        var client = CreateClient(handler);

        await client.AddTrackToPlaylistAsync("VLPL123", "abc123");

        Assert.NotNull(handler.LastRequest);
        Assert.Contains("/youtubei/v1/browse/edit_playlist", handler.LastRequest!.RequestUri!.ToString());

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;
        Assert.Equal("PL123", root.GetProperty("playlistId").GetString());
        var action = root.GetProperty("actions")[0];
        Assert.Equal("ACTION_ADD_VIDEO", action.GetProperty("action").GetString());
        Assert.Equal("abc123", action.GetProperty("addedVideoId").GetString());
    }

    [Fact]
    public async Task Client_CreatePlaylistWithTrackAsync_ReturnsPlaylistId()
    {
        var handler = new RecordingHandler("{\"playlistId\":\"PLnueva\"}");
        var client = CreateClient(handler);

        var playlistId = await client.CreatePlaylistWithTrackAsync("Favoritas", "abc123");

        Assert.Equal("PLnueva", playlistId);
        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;
        Assert.Equal("Favoritas", root.GetProperty("title").GetString());
        Assert.Equal("PRIVATE", root.GetProperty("privacyStatus").GetString());
        Assert.Equal("abc123", root.GetProperty("videoIds")[0].GetString());
    }

    [Fact]
    public async Task Client_SetTrackRatingAsync_GatedResponse_Throws()
    {
        var handler = new RecordingHandler(
            "{\"actions\":[{\"showEngagementPanelEndpoint\":{\"identifier\":{\"tag\":\"captcha\"}}}]}");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<YouTubeMusicException>(
            () => client.SetTrackRatingAsync("abc123", RadarRating.Like));
    }

    private YouTubeMusicClient CreateClient(RecordingHandler handler)
    {
        var path = Path.Combine(_tempDir, $"cookies_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path,
            "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t1790000000\tSAPISID\tabc123\n");
        var session = YouTubeMusicSession.TryCreate(path);
        Assert.NotNull(session);
        return new YouTubeMusicClient(session!, "es", new HttpClient(handler));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _postResponse;

        public RecordingHandler(string postResponse)
        {
            _postResponse = postResponse;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"INNERTUBE_CLIENT_VERSION\":\"1.20260101.00.00\"}")
                };
            }

            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_postResponse)
            };
        }
    }

    private static (System.Collections.Generic.List<RadarItem> Items, string? Continuation) Parse(string json, RadarSection section)
    {
        using var document = JsonDocument.Parse(json);
        return YouTubeMusicResponseParser.Parse(document.RootElement, section);
    }
}
