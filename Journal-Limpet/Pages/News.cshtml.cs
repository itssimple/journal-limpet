using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Parsers;
using Markdig.Syntax;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    public class NewsModel : PageModel
    {
        public List<NewsItem> Items { get; set; } = new List<NewsItem>();
        public NewsItem SingleItem { get; set; }

        public async Task<IActionResult> OnGetAsync(string title = null)
        {
            var newsFiles = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, "News"), "*.md").OrderByDescending(f => f);

            if (string.IsNullOrWhiteSpace(title))
            {
                foreach (var file in newsFiles)
                {
                    NewsItem newsItem = await LoadNewsItem(file);

                    Items.Add(newsItem);
                }
                return Page();
            }
            else
            {
                var article = newsFiles.FirstOrDefault(s => s.Contains(title));
                if (article != null)
                {
                    Items.Clear();
                    Items.Add(await LoadNewsItem(article));
                    return Page();
                }
                else
                {
                    return NotFound();
                }
            }
        }

        private async Task<NewsItem> LoadNewsItem(string file)
        {
            if (!System.IO.File.Exists(file)) return null;

            var mdPipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

            var fileContent = await System.IO.File.ReadAllTextAsync(file);
            var mdFile = Markdown.Parse(fileContent, mdPipeline);

            var frontMatter = (YamlFrontMatterBlock)mdFile.FirstOrDefault(b => b.Parser is YamlFrontMatterParser);
            var itemTitleBlock = (HeadingBlock)mdFile.FirstOrDefault(b => b.Parser is HeadingBlockParser);
            var itemTitle = itemTitleBlock?.Inline.FirstOrDefault();

            var data = frontMatter.Lines.Lines.Select(l => l.Slice.ToString().Split(':', 2).Select(s => s.Trim()).ToList()).Where(s => s.Count > 1).ToDictionary(s => s[0], s => s[1]);

            var clearText = Markdown.ToPlainText(fileContent, pipeline: mdPipeline);
            var htmlText = Markdown.ToHtml(fileContent, pipeline: mdPipeline);

            var fileName = new FileInfo(file).Name;

            mdFile.Remove(frontMatter);
            mdFile.Remove(itemTitleBlock);

            var cleanedOutput = string.Empty;

            using (var tw = new StringWriter())
            {
                var x = new Markdig.Renderers.HtmlRenderer(tw);
                x.Write(mdFile);

                tw.Flush();
                cleanedOutput = tw.ToString();
            }

            var newsItem = new NewsItem
            {
                Title = itemTitle.ToString(),
                Description = cleanedOutput,
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

            return newsItem;
        }

        public class NewsItem
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public string Link { get; set; }
            public DateTimeOffset PubDate { get; set; }
            public string Category { get; set; }
        }
    }
}
