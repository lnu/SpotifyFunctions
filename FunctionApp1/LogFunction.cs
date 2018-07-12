using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SpotifyFunctions
{
    public static class LogFunction
    {
        [FunctionName("LogFunction")]
        public static async Task Run([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");
            string Address = "https://www.rtbf.be/classic21/article/detail_la-playlist-du-vendredi-29-juin-2018?id=9958844";
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string content = null;
                // download the content of the page
                using (var client = new WebClient())
                {
                    content = await client.DownloadStringTaskAsync(Address);

                }
                // extract the table containing the information of the tracks(using HtmlAgilityPack)
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(content);
                string playlistName = doc.DocumentNode.SelectSingleNode("//title").InnerText;
                List<List<string>> table = doc.DocumentNode.SelectSingleNode("//table")
                                    .Descendants("tr")
                                    .Skip(1)
                                    .Select(tr => tr.Elements("td").Select(td => td.InnerText.Trim()).ToList())
                                    .ToList();
                // create the playlist or clear it if it exists

                // for each songs search in spotify
            }
            catch (Exception ex)
            {
                log.Info($"Exception found {ex.Message}");
            }
        }
    }
}
