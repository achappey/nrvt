using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using System.IO;
using System.Globalization;

using HtmlAgilityPack;
using CsvHelper;

namespace TaxateurScraper
{
    class Program
    {
        static string BaseUrl = "https://www.nrvt.nl";
        static string BasePath = "/vind-een-taxateur";
        static HttpClient Client = new HttpClient();

        static void Main(string[] args)
        {
            try
            {
                var expertises = GetExpertises();

                for (var i = 0; i < expertises.Count; i++)
                {
                    Console.WriteLine(string.Format("{0}. {1}", i + 1, expertises[i].Name));
                }

                Console.WriteLine("Select expertise and press ENTER");

                var result = Console.ReadLine();

                int resultIndex;

                if (int.TryParse(result.ToString(), out resultIndex))
                {
                    if (expertises.Count >= resultIndex)
                    {
                        Task task = Scrape(expertises[resultIndex - 1]);

                        task.Wait();
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(string.Format("Something went wrong: {0}", exception.Message));
            }
        }

        static async Task Scrape(Expertise expertise)
        {
            var pageCount = await GetPageCount(expertise.Value);

            var result = new List<Taxateur>();

            for (var i = 1; i <= pageCount; i++)
            {
                Console.WriteLine(string.Format("Retrieving page {0}/{1}", i, pageCount));

                var items = await ScrapePage(string.Format("{0}{1}?page={2}", BaseUrl, BasePath, i), expertise.Value);

                result.AddRange(items);

                Thread.Sleep(500);
            }

            var filename = string.Format("{0}_{1}", expertise.Name, DateTime.Now.ToString("yyyyMMddHHmmssfff"));

            Console.WriteLine(string.Format("Writing {0}", filename));

            await WriteCsv(filename, result);

            Console.WriteLine("Completed");
        }

        static async Task<List<Taxateur>> ScrapePage(string url, string expertise)
        {
            var html = await GetHtml(url, expertise);

            return ParsePage(html);
        }

        static async Task<int> GetPageCount(string expertise)
        {
            var html = await GetHtml(string.Format("{0}{1}", BaseUrl, BasePath), expertise);

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var pager = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'pagination')]");
            var pages = pager.SelectNodes(".//a");

            pages.RemoveAt(pages.Count - 1);

            return int.Parse(pages.Last().SelectSingleNode(".//p").InnerText);
        }

        static async Task WriteCsv(string filename, List<Taxateur> data)
        {
            using (var writer = new StreamWriter(string.Format("{0}.csv", filename)))
            using (var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csvWriter.WriteHeader<Taxateur>();

                csvWriter.NextRecord();

                await csvWriter.WriteRecordsAsync(data);
            }
        }

        static async Task<string> GetHtml(string url, string expertise)
        {
            var uri = new Uri(url);
            var formVariables = new List<KeyValuePair<string, string>>();

            formVariables.Add(new KeyValuePair<string, string>("choose", "persoon"));
            formVariables.Add(new KeyValuePair<string, string>("expertise", expertise));

            var result = new List<Taxateur>();

            var formContent = new FormUrlEncodedContent(formVariables);

            using (var message = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = uri,
                Content = formContent
            })
            {
                using (var postResponse = await Client.SendAsync(message))
                {
                    if (postResponse.IsSuccessStatusCode)
                    {
                        var stringContent = await postResponse.Content.ReadAsStringAsync();

                        return stringContent;
                    }

                    throw new Exception(postResponse.ReasonPhrase);
                }
            }
        }

        static List<Taxateur> ParsePage(string html)
        {
            var result = new List<Taxateur>();

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var searchResults = htmlDoc.GetElementbyId("search-results");
            var rows = searchResults.SelectNodes(".//div[contains(@class, 'result-row')]");

            foreach (var row in rows)
            {
                var initials = row.SelectSingleNode(".//div[contains(@class, 'first')]");
                var lastName = row.SelectSingleNode(".//div[contains(@class, 'second')]");
                var company = row.SelectSingleNode(".//div[contains(@class, 'third')]");

                result.Add(new Taxateur()
                {
                    Initials = initials.InnerText?.Trim(),
                    LastName = lastName.InnerText?.Trim(),
                    Company = company.InnerText?.Trim()
                });

            }

            return result;
        }

        static List<Expertise> GetExpertises()
        {
            var result = new List<Expertise>();

            var url = string.Format("{0}{1}", BaseUrl, BasePath);
            var web = new HtmlWeb();
            var doc = web.Load(url);
            var expertise = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'search-expertise')]");
            var rows = expertise.SelectNodes(".//option");

            foreach (var row in rows)
            {
                var value = row.GetAttributeValue("value", null);

                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(new Expertise()
                    {
                        Value = value,
                        Name = row.InnerText
                    });
                }
            }

            return result;
        }
    }

    public class Taxateur
    {
        public string Initials { get; set; }
        public string LastName { get; set; }
        public string Company { get; set; }
    }

    public class Expertise
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }
}
