using HtmlAgilityPack;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SpotifyFunctions
{
    public static class LogFunction
    {
        static SpotifyWebAPI spotifyWebApi = new SpotifyWebAPI()
        {
            UseAuth = true,
            AccessToken = Environment.GetEnvironmentVariable("SpotifyToken")
        };

        [FunctionName("LogFunction")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");

            // webclient fails without this
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // reading playlist url from request parameters
            var address = await req.Content.ReadAsStringAsync();

            log.Info($"Address:{address}");

            // return if address not valid(could be improved
            if (string.IsNullOrEmpty(address) || !address.StartsWith("https://www.rtbf.be/classic21/article"))
            {
                log.Error("Invalid address");
                return;
            }

            try
            {
                string content = null;
                // download the content of the page
                using (var client = new WebClient())
                {
                    content = await client.DownloadStringTaskAsync(address);

                }
                // extract the table containing the information of the tracks(using HtmlAgilityPack)
                var doc = new HtmlDocument();
                doc.LoadHtml(content);
                string playlistName = doc.DocumentNode.SelectSingleNode("//title").InnerText;
                List<List<string>> table = doc.DocumentNode.SelectSingleNode("//table")
                                    .Descendants("tr")
                                    .Skip(1)
                                    .Where(tr => tr.Elements("td").All(p => !string.IsNullOrEmpty(p.InnerText.Trim())))
                                    .Select(tr => tr.Elements("td").Select(td => HtmlEntity.DeEntitize(td.InnerText.Trim())).ToList())
                                    .ToList();

                // create the playlist or exit it if it exists
                var spotifyProfile = await spotifyWebApi.GetPrivateProfileAsync();

                if (spotifyProfile.HasError())
                {
                    log.Error($"Spotify error:{spotifyProfile.Error.Message}");
                    return;
                }

                var playlists = await spotifyWebApi.GetUserPlaylistsAsync(spotifyProfile.Id);
                var thisWeekPlaylist = playlists.Items.FirstOrDefault(p => p.Name == playlistName);
                if (thisWeekPlaylist != null)
                {
                    log.Warning($"Playlist {playlistName} already exists");
                    return;
                }
                var newPlaylist = await spotifyWebApi.CreatePlaylistAsync(spotifyProfile.Id, playlistName, true);

                int foundSongs = 0;

                // for each songs search in spotify
                foreach (var t in table)
                {
                    log.Info($"Searching {t[1]} from {t[0]} on {t[2]}");
                    var songs = await spotifyWebApi.SearchItemsAsync(string.Concat(t[1], " ", t[0]), SearchType.Track, market: "BE");

                    // remove (year) , New! or Out mm/yy from album name 
                    var regEx = new Regex(@"(\(\d{4}\)|, NEW !|Single,|, Out \d{2}\/\d{2})", RegexOptions.IgnoreCase);
                    var albumNameCleaned = regEx.Replace(t[2], "").Trim();

                    // filter tracks by the right artist
                    //var artistsFiltered = songs.Tracks?.Items.Where(p => p.Artists.Any(q => string.Compare(q.Name, t[0], true) == 0));

                    // get song from right album if possible
                    var song = songs.Tracks?.Items.FirstOrDefault(p => p.Album.Name.IndexOf(albumNameCleaned, StringComparison.CurrentCultureIgnoreCase) != -1);
                    if (song == null)
                    {
                        song = songs.Tracks?.Items.FirstOrDefault();
                        if (song != null)
                        {
                            log.Info($"Full match impossible, picking first one:{song.Name} {song.Album.Name} {song.Artists.First().Name}");
                        }
                    }

                    //var song = songs.Tracks?.Items.FirstOrDefault(p => p.Album.Name.IndexOf(albumNameCleaned, StringComparison.CurrentCultureIgnoreCase) != -1);
                    //if (song == null)
                    //{
                    //    song = songs.Tracks?.Items.FirstOrDefault();
                    //}

                    if (song != null)
                    {
                        await spotifyWebApi.AddPlaylistTrackAsync(spotifyProfile.Id, newPlaylist.Id, song.Uri);
                        foundSongs++;
                    }
                    else
                    {
                        log.Info("Song not found");
                    }
                }
                log.Info($"{foundSongs}/{table.Count} songs added");
            }
            catch (Exception ex)
            {
                log.Info($"Exception found {ex.Message}");
            }
        }
    }
}
