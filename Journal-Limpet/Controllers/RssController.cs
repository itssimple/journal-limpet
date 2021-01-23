using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Parsers;
using Markdig.Syntax;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RssController : ControllerBase
    {
        [Route("")]
        public IActionResult NewsFeed()
        {
            var newsRss = new RssFeed
            {
                Title = "Journal Limpet: News feed",
                Description = "This is the Journal Limpet news feed",
                Link = Url.RouteUrl("", null, HttpContext.Request.Scheme, HttpContext.Request.Host.ToString())
            };

            var newsFiles = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, "News"), "*.md");

            var mdPipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

            foreach (var file in newsFiles)
            {
                var fileContent = System.IO.File.ReadAllText(file);
                var mdFile = Markdown.Parse(fileContent, mdPipeline);

                var frontMatter = (YamlFrontMatterBlock)mdFile.FirstOrDefault(b => b.Parser is YamlFrontMatterParser);
                var itemTitle = ((HeadingBlock)mdFile.FirstOrDefault(b => b.Parser is HeadingBlockParser))?.Inline.FirstOrDefault();

                var data = frontMatter.Lines.Lines.Select(l => l.Slice.ToString().Split(':', 2).Select(s => s.Trim()).ToList()).Where(s => s.Count > 1).ToDictionary(s => s[0], s => s[1]);

                var clearText = Markdown.ToPlainText(fileContent, pipeline: mdPipeline);
                var htmlText = Markdown.ToHtml(fileContent, pipeline: mdPipeline);

                var fileName = new FileInfo(file).Name;

                var newsItem = new RssFeedItem
                {
                    Title = itemTitle.ToString(),
                    Description = htmlText,
                    Link = Url.Page("/News", null, null, HttpContext.Request.Scheme, HttpContext.Request.Host.ToString()) + "/" + fileName.Replace(".md", "")
                };

                if (data.ContainsKey("pubdate"))
                {
                    if (DateTimeOffset.TryParse(data["pubdate"], out var articleTime))
                    {
                        newsItem.PubDate = articleTime;
                    }
                }

                if (data.ContainsKey("category"))
                {
                    newsItem.Category = data["category"];
                }

                newsRss.FeedItems.Add(newsItem);
            }

            return Content(newsRss.ToString(), "application/rss+xml");
        }
    }

    public class RssFeed
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Link { get; set; }

        public List<RssFeedItem> FeedItems { get; set; } = new List<RssFeedItem>();

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            // RSS header
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">");

            // Channel info
            sb.AppendLine("<channel>");
            sb.AppendLine($"  <title>{Title}</title>");
            sb.AppendLine($"  <link>{Link}</link>");
            sb.AppendLine($"  <atom:link href=\"{Link}\" rel=\"self\" type=\"application/rss+xml\" />");
            sb.AppendLine($"  <description>{Description}</description>");

            // All the news items
            foreach (var item in FeedItems)
            {
                sb.AppendLine(ReplaceVariables(item.ToString()));
            }

            // End of channel and RSS
            sb.AppendLine("</channel>");
            sb.AppendLine("</rss>");

            return sb.ToString();
        }

        private string ReplaceVariables(string feedItem)
        {
            return feedItem
                .Replace("$rssUrl$", Link);
        }
    }

    public class RssFeedItem
    {
        public DateTimeOffset PubDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public Guid Guid
        {
            get
            {
                using (MD5 md5 = MD5.Create())
                {
                    return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(Title)));
                }
            }
        }
        public string Link { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("  <item>");
            sb.AppendLine($"    <title>{Title}</title>");
            sb.AppendLine($"    <guid isPermaLink=\"false\">{Guid}</guid>");
            sb.AppendLine($"    <pubDate>{(PubDate.ToString("ddd',' d MMM yyyy HH':'mm':'ss", new CultureInfo("en-US")) + " " + PubDate.ToString("zzzz").Replace(":", ""))}</pubDate>");
            sb.AppendLine($"    <category>{Category}</category>");
            sb.AppendLine($"    <link>{Link}</link>");
            sb.AppendLine($"    <description><![CDATA[{Description}]]></description>");
            sb.Append("  </item>");

            return sb.ToString();
        }
    }
}
