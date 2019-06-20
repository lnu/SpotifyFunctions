using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
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
    public static class CreatePlaylistForClassic21Metal
    {
        static SpotifyWebAPI spotifyWebApi = new SpotifyWebAPI()
        {
            UseAuth = true,
            AccessToken = Environment.GetEnvironmentVariable("SpotifyToken")
        };

        [FunctionName("CreatePlaylistForClassic21Metal")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)]HttpRequestMessage req, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            // webclient fails without this
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // reading playlist url from request parameters
            var address = await req.Content.ReadAsStringAsync();

            log.LogInformation($"Address:{address}");

            // return if address not valid(could be improved
            if (string.IsNullOrEmpty(address) || !address.StartsWith("https://www.rtbf.be/classic21/article"))
            {
                log.LogError("Invalid address");
                return new BadRequestObjectResult("Invalid playlist address");
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
                    log.LogError($"Spotify error:{spotifyProfile.Error.Message}");
                    return new BadRequestObjectResult($"Spotify error:{spotifyProfile.Error.Message}");
                }

                var playlists = await spotifyWebApi.GetUserPlaylistsAsync(spotifyProfile.Id);
                var thisWeekPlaylist = playlists.Items.FirstOrDefault(p => p.Name == playlistName);
                if (thisWeekPlaylist != null)
                {
                    log.LogWarning($"Playlist {playlistName} already exists");
                    return new OkObjectResult($"Playlist {playlistName} already exists");
                }
                var newPlaylist = await spotifyWebApi.CreatePlaylistAsync(spotifyProfile.Id, playlistName, true);


                if (newPlaylist.HasError())
                {
                    log.LogError($"Spotify error:{newPlaylist.Error.Message}");
                    return new BadRequestObjectResult($"Spotify playlist creation error:{spotifyProfile.Error.Message}");
                }

                int foundSongs = 0;

                // for each songs search in spotify
                foreach (var t in table)
                {
                    log.LogInformation($"Searching {t[1]} from {t[0]} on {t[2]}");

                    // remove At  from the song name 
                    var regEx = new Regex(@"(At )", RegexOptions.IgnoreCase);
                    var songNameCleaned = t[1].Substring(regEx.Match(t[1]).Length > 0 ? regEx.Match(t[1]).Length : 0);

                    var songs = await spotifyWebApi.SearchItemsAsync(string.Concat(songNameCleaned, " ", t[0]), SearchType.Track, market: "BE");

                    // remove (year) , New! or Out mm/yy from album name 
                    regEx = new Regex(@"(\(\d{4}\)|, NEW|,NEW|Single,|, Out \d{2}\/\d{2})", RegexOptions.IgnoreCase);
                    //var albumNameCleaned = regEx.Replace(t[2], "").Trim();
                    var albumNameCleaned = t[2].Substring(0, regEx.Match(t[2]).Index > 0 ? regEx.Match(t[2]).Index : t[2].Length);

                    log.LogInformation($"Cleaned album name:{albumNameCleaned}");

                    // filter tracks by the right artist
                    //var artistsFiltered = songs.Tracks?.Items.Where(p => p.Artists.Any(q => string.Compare(q.Name, t[0], true) == 0));

                    // get song from right album if possible
                    var song = songs.Tracks?.Items.FirstOrDefault(p => p.Album.Name.IndexOf(albumNameCleaned, StringComparison.CurrentCultureIgnoreCase) != -1);
                    if (song == null)
                    {
                        song = songs.Tracks?.Items.FirstOrDefault();
                        if (song != null)
                        {
                            log.LogInformation($"Full match impossible, picking first one:{song.Name} {song.Album.Name} {song.Artists.First().Name}");
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
                        log.LogInformation("Song not found");
                    }
                }
                log.LogInformation($"{foundSongs}/{table.Count} songs added");
                return new OkObjectResult($"Playlist {playlistName} created with {foundSongs}/{table.Count} songs");
            }
            catch (Exception ex)
            {
                log.LogInformation($"Exception found {ex.Message}");
                return new BadRequestObjectResult($"An unhandled exception occured");
            }
        }
    }
}
