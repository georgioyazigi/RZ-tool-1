using System.Reflection;
using System.Xml.Linq;

public static class XMLExtractor
{
    public static string GetFieldValue(XElement experiment, string fieldName)
    {
        return experiment.Descendants("field")
                         .FirstOrDefault(x => x.Attribute("name")?.Value == fieldName)?
                         .Element("values")?
                         .Element("value")?.Value ?? "N/A";
    }
    #region Initialize SkippedAttachments Logging
    public static void LogSkippedAttachment(string logPath, string fileName, string reason)
    {
        string logEntry = $"{fileName}\t{reason}";
        File.AppendAllText(logPath, logEntry + Environment.NewLine);
    }
    #endregion



    public static Dictionary<string, object> GetHtmlFieldWithAttachments(XElement experiment, string fieldName, string attachmentsPath)
    {
        string localPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string skippedLogPath = Path.Combine(localPath, "SkippedAttachments.txt");
        var fieldData = new Dictionary<string, object>();

        // Get ALL values inside <values> (instead of only the first one)
        var values = GetMultiFieldValues(experiment, fieldName);

        if (values.Count > 0)
        {
            // Join multiple values into a single HTML content block
            string htmlContent = string.Join("<br>", values);
            fieldData["Content"] = htmlContent;

            var attachments = new List<Dictionary<string, string>>();

            foreach (var link in GetAttachmentLinks(htmlContent))
            {
                string attachmentPath = Path.Combine(attachmentsPath, link);
                if (File.Exists(attachmentPath))
                {
                    try
                    {
                        //string base64Data = Convert.ToBase64String(File.ReadAllBytes(attachmentPath));
                        attachments.Add(new Dictionary<string, string>
                        {
                            ["FileName"] = link,
                            ["Base64Content"] = attachmentPath
                        });
                    }
                    catch (OutOfMemoryException ex)
                    {
                        Console.WriteLine($"⚠️ Skipping attachment '{link}' due to memory error: {ex.Message}");
                        LogSkippedAttachment(skippedLogPath, link, "OutOfMemoryException - Not enough memory to read file.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Skipping attachment '{link}' due to unexpected error: {ex.Message}");
                        LogSkippedAttachment(skippedLogPath, link, $"{ex.GetType().Name} - {ex.Message}");
                    }
                }
            }

            fieldData["Attachments"] = attachments;
        }
        else
        {
            fieldData["Content"] = "N/A";
            fieldData["Attachments"] = new List<Dictionary<string, string>>();
        }

        return fieldData;
    }



    public static List<string> GetMultiFieldValues(XElement experiment, string fieldName)
    {
        return experiment.Descendants("field")
                         .Where(x => x.Attribute("name")?.Value == fieldName)
                         .Descendants("value")
                         .Select(v => v.Value.Trim())
                         .ToList();
    }

    private static IEnumerable<string> GetAttachmentLinks(string htmlContent)
    {
        var links = new List<string>();
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(htmlContent);

        foreach (var node in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            string href = node.GetAttributeValue("href", string.Empty);
            if (href.StartsWith("/attachments/"))
            {
                links.Add(href.Replace("/attachments/", ""));
            }
        }

        foreach (var node in doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            string src = node.GetAttributeValue("src", string.Empty);
            if (src.StartsWith("/attachments/"))
            {
                links.Add(src.Replace("/attachments/", ""));
            }
        }

        return links.Distinct();
    }


}
