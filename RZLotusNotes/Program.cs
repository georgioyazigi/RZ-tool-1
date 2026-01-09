using Common.Models;
using HtmlAgilityPack;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using spr_live_migration;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;

#region Get config data

string localPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
string configLocation = Path.Combine(localPath, "config.xml");

if (!File.Exists(configLocation))
{
    Console.WriteLine("Configuration file not found. Make sure config.xml is present in the correct location.");
    return;
}

FileStream Content = File.OpenRead(configLocation);
XDocument xd1 = XDocument.Load(Content);

// Ensure all the required configuration values are available
string apiKey = xd1.Element("UploadConfig")?.Element("apiKey")?.Value;
string tenantUrl = xd1.Element("UploadConfig")?.Element("tenantURL")?.Value;
string expTemplateID = xd1.Element("UploadConfig")?.Element("experimentTemplate")?.Value;
string sourceFilesLocation = xd1.Element("UploadConfig")?.Element("sourceFilesLocation")?.Value;
string attachmentsPath = xd1.Element("UploadConfig")?.Element("sourceFilesAttachmentsLocation")?.Value;
string reportOnly = xd1.Element("UploadConfig")?.Element("reportOnly")?.Value ?? "0"; // Default to "0" if not found
int closeFlag = 0;
var closeNode = xd1.Element("UploadConfig")?.Element("closeExperiments");
if (closeNode != null && int.TryParse(closeNode.Value, out int parsed))
    closeFlag = parsed;

bool closeExperimentsEnabled = closeFlag == 1;

bool mapDepartmentsContributing = true; // default ON

var depToggleNode = xd1
    .Element("UploadConfig")?
    .Element("attributeToggles")?
    .Element("DepartmentsContributing");

if (depToggleNode != null && int.TryParse(depToggleNode.Value, out int depToggle))
    mapDepartmentsContributing = depToggle == 1;


if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(tenantUrl) || string.IsNullOrEmpty(sourceFilesLocation))
{
    Console.WriteLine("Configuration missing one or more required values (apiKey, tenantURL, sourceFilesLocation). Please verify the config.xml file.");
    return;
}

Console.WriteLine("Configuration loaded successfully.");

string experimentLogcsvFilePath = Path.Combine(localPath, "Experiment_Log.csv");


#endregion

#region Load Data from XML
if (!Directory.Exists(sourceFilesLocation))
{
    Console.WriteLine($"Data directory not found: {sourceFilesLocation}. Please check thes path in config.xml.");
    return;
}
foreach (string fileLocation in Directory.GetFiles(sourceFilesLocation, "*.xml", SearchOption.TopDirectoryOnly))
{
    XDocument xmlDoc = XDocument.Load(fileLocation);




    #endregion

    #region Variables
    string projRootFolder = Environment.CurrentDirectory;
    Dictionary<string, string> dctOutMapping = new();

    List<string> lstToSkip = new List<string>();
    List<string> lstComplete = new List<string>();
    #endregion

    #region Initialize CSV Logging
    bool fileExists = File.Exists(experimentLogcsvFilePath);

    // Create the CSV header if the file is new
    if (!fileExists)
    {
        File.WriteAllText(experimentLogcsvFilePath, "Experiment EID,Document unid,Status,Error Message\n");
    }
    #endregion

    #region Initialize success Logging
    string successtxt = Path.Combine(localPath, "success.txt");
    bool fileExist = File.Exists(successtxt);
    List<string> completed = new List<string>();
    // Create the CSV header if the file is new
    if (File.Exists(successtxt))
    {
        completed = File.ReadAllLines(successtxt).ToList();
    }
    #endregion


    // Setup Connection
    SNBAPI.SetupConnectionToSNB(tenantUrl, apiKey);
    int experimentIndex = 0; // Counter to track the index of the current experiment
                             // Load config mapping
    Dictionary<string, int> attributeMap = xd1
        .Element("UploadConfig")?
        .Element("attributeMapping")?
        .Elements()
        .ToDictionary(
            el => el.Name.LocalName.Replace("_", " "), // Normalize key
            el => int.Parse(el.Attribute("attribute").Value)
        );
    int snbUsersAttributeId = int.Parse(
        xd1.Element("UploadConfig")?
           .Element("attributeMapping")?
           .Element("SNBUsers")?
           .Attribute("attribute")?.Value ?? "0"
    );
    List<string> validOwnerEmails = await SNBAPI.GetAttributeOptionsAsync(tenantUrl, apiKey, snbUsersAttributeId);
    HashSet<string> validOwnersLower = validOwnerEmails.Select(e => e.Trim().ToLower()).ToHashSet();

    await ValidateAttributesFromXml(xmlDoc, attributeMap, apiKey, tenantUrl);

    // Process each experiment and upload to SNB
    foreach (var experiment in xmlDoc.Descendants("document"))
    {
        experimentIndex++; // Increment index for each experiment

        {
            Console.WriteLine();
            Console.WriteLine("_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-");
            Console.WriteLine("Processing the experiment number " + experimentIndex);
            string rawOwner = NormalizeEmail(XMLExtractor.GetFieldValue(experiment, "COOWNER"))?.Trim().ToLower();
            string validatedOwner = validOwnersLower.Contains(rawOwner)
                ? rawOwner
                : "r.hilhorst@rijkzwaan.nl";

            if (validatedOwner != rawOwner)
                Console.WriteLine($"⚠️ Invalid or missing owner '{rawOwner}', using fallback.");


            var experimentData = new Dictionary<string, object>
            {
                ["ExperimentID"] = experiment.Attribute("unid")?.Value ?? "N/A",
                ["Title"] = XMLExtractor.GetFieldValue(experiment, "title"),
                ["Document Owner"] = validatedOwner,
                ["Department"] = XMLExtractor.GetFieldValue(experiment, "DEPARTMENT"),
                ["Departments Contributing"] = XMLExtractor.GetMultiFieldValues(experiment, "DEPARTMENTS"),
                ["ConfidentialityLevel"] = XMLExtractor.GetFieldValue(experiment, "NodeSecurityLevel"),
                ["Genus"] = XMLExtractor.GetFieldValue(experiment, "validGenus"),
                ["Crop"] = XMLExtractor.GetMultiFieldValues(experiment, "validproject_crop"),
                ["CropSpecies"] = XMLExtractor.GetMultiFieldValues(experiment, "validcropgrouplatin"),
                ["Biotic Stress Category"] = XMLExtractor.GetMultiFieldValues(experiment, "validpathogentype"),
                ["Pathogen"] = XMLExtractor.GetMultiFieldValues(experiment, "project_pathogen"),
                ["DocumentID"] = XMLExtractor.GetFieldValue(experiment, "InheritID"),
                ["DocumentNumber"] = XMLExtractor.GetFieldValue(experiment, "project_ID"),
                ["DocumentCode"] = XMLExtractor.GetFieldValue(experiment, "project_user_id"),
                ["ParentPath"] = XMLExtractor.GetFieldValue(experiment, "parentPath"),
                ["ParentID"] = XMLExtractor.GetFieldValue(experiment, "parentDocId"),
                ["Technique"] = XMLExtractor.GetMultiFieldValues(experiment, "project_technique"),
                ["WorkfrontProjectURL"] = XMLExtractor.GetFieldValue(experiment, "project_workfront_url"),
                ["Migration Wave"] = XMLExtractor.GetFieldValue(experiment, "project_migriation_wave"),
                ["PRORZ Document Type"] = XMLExtractor.GetFieldValue(experiment, "node_type")
            };
            // NEW: normalize and store the creation date from <field name="mile_start">
            var prorzCreationDate = GetProrzCreationDate(experiment);
            if (!string.IsNullOrWhiteSpace(prorzCreationDate))
            {
                experimentData["PRORZ Creation Date"] = prorzCreationDate;
            }
            //skip experiments completed in the previous run
            if (completed.Contains(experimentData["ExperimentID"]))
                continue;
            // Adding new content sections with attachments, skipping if content is empty or "N/A"
            string[] sections = { "Introduction", "MethodMaterial", "Body", "project_discussion", "mile_conclusion", "treatments" };
            foreach (var section in sections)
            {
                var sectionContent = XMLExtractor.GetHtmlFieldWithAttachments(experiment, section, attachmentsPath);
                if (sectionContent is Dictionary<string, object> sectionDict && sectionDict.TryGetValue("Content", out var contentObj) && contentObj is string content && !string.IsNullOrWhiteSpace(content) && content.ToUpper() != "N/A")
                {
                    experimentData[section] = sectionContent;
                }
            }

            experimentData["Departments Contributing"] = CleanList(experimentData.TryGetValue("Departments Contributing", out var dc) ? dc : null);
            experimentData["Crop"] = CleanList(experimentData.TryGetValue("Crop", out var crop) ? crop : null);
            experimentData["CropSpecies"] = CleanList(experimentData.TryGetValue("CropSpecies", out var cs) ? cs : null);
            experimentData["Pathogen"] = CleanList(experimentData.TryGetValue("Pathogen", out var pg) ? pg : null);
            experimentData["Biotic Stress Category"] = CleanList(experimentData.TryGetValue("Biotic Stress Category", out var bsc) ? bsc : null);
            experimentData["Technique"] = CleanList(experimentData.TryGetValue("Technique", out var tech) ? tech : null);
            experimentData["treatments"] = CleanList(
        experimentData.TryGetValue("treatments", out var treat) ? treat : null
    );

            string dep = experimentData.TryGetValue("Department", out var depObj) ? depObj?.ToString() : null;

            if (string.IsNullOrWhiteSpace(dep))
            {
                // Try common plural sources in order
                var keys = new[] { "Departments Contributing", "DEPARTMENTS", "Departments" };

                foreach (var k in keys)
                {
                    if (!experimentData.TryGetValue(k, out var v) || v == null) continue;

                    if (v is List<string> ls && ls.Count > 0 && !string.IsNullOrWhiteSpace(ls[0]))
                    {
                        dep = ls[0].Trim();
                        break;
                    }

                    var s = v?.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        dep = s.Trim();
                        break;
                    }
                }

                // Set it back so your dctAttributes picks it up
                experimentData["Department"] = dep ?? "";
            }


            // Apply ACC defaults (per-experiment) BEFORE building attributes
            (experimentData, var accChanges) = ApplyAccFieldDefaults(experimentData, conflictsPerField: null);
            if (accChanges.Count > 0)
                Console.WriteLine("ACC defaults applied:\n - " + string.Join("\n - ", accChanges));


            Dictionary<string, string> dctAttributes = new()
    {
/*        { "name", SanitizeString(experimentData["Title"]?.ToString() ?? "N/A") },
*/        { "Description", SanitizeString(experimentData["Title"]?.ToString() ?? "N/A") },
        { "Document Owner", SanitizeString(experimentData["Document Owner"]?.ToString() ?? "N/A") },
        { "Department", SanitizeString(experimentData["Department"]?.ToString() ?? "N/A") },
        { "Confidentiality Level", SanitizeString(experimentData["ConfidentialityLevel"]?.ToString() ?? "N/A") },
        { "Genus", SanitizeString(experimentData["Genus"]?.ToString() ?? "N/A") },
        { "Crop", "~" + SanitizeString(string.Join(", ", experimentData["Crop"] as List<string> ?? new List<string>())) },
        { "Crop Species", "~" + SanitizeString(string.Join(", ", experimentData["CropSpecies"] as List<string> ?? new List<string>())) },
        { "Biotic Stress Category", "~" + SanitizeString(string.Join(", ", experimentData["Biotic Stress Category"] as List<string> ?? new List<string>())) },
        { "Pathogen", "~" +SanitizeString(string.Join(", ", experimentData["Pathogen"] as List<string> ?? new List<string>())) },
        { "PRORZ Document ID", SanitizeString(experimentData["DocumentID"]?.ToString() ?? "N/A") },
        { "PRORZ Document Number", SanitizeString(experimentData["DocumentNumber"]?.ToString() ?? "N/A") },
        { "PRORZ Document Code", SanitizeString(experimentData["DocumentCode"]?.ToString() ?? "N/A") },
        { "PRORZ Parent Path", SanitizeString(experimentData["ParentPath"]?.ToString() ?? "N/A") },
        { "PRORZ Parent ID", SanitizeString(experimentData["ParentID"]?.ToString() ?? "N/A") },
        { "PRORZ Technique", SanitizeString(string.Join("; ", experimentData["Technique"] as List<string> ?? new List<string>())) },
        { "Workfront Project URL", SanitizeString(experimentData["WorkfrontProjectURL"]?.ToString() ?? "N/A") },
        { "Migration Wave", SanitizeString(experimentData["Migration Wave"]?.ToString() ?? "N/A") },
        { "PRORZ Document Type", SanitizeString(experimentData["PRORZ Document Type"]?.ToString() ?? "N/A") }
    };

            if (mapDepartmentsContributing)
            {
                dctAttributes["Departments Contributing"] =
                    "~" + SanitizeString(string.Join(", ",
                        experimentData["Departments Contributing"] as List<string> ?? new List<string>()));
            }
            else
            {
                Console.WriteLine("Skipping attribute: Departments Contributing (disabled in config)");
            }

            // NEW: only add the date if we actually parsed one (avoid sending "N/A" to a Date field)
            if (experimentData.TryGetValue("PRORZ Creation Date", out var createdObj))
            {
                var created = createdObj?.ToString();
                if (!string.IsNullOrWhiteSpace(created))
                    dctAttributes["PRORZ Creation Date"] = created;
            }

            static string NormalizeEmail(string s) =>
        (s ?? "").Trim().Replace(',', '.').ToLowerInvariant();

            static (Dictionary<string, object> Data, List<string> Changes) ApplyAccFieldDefaults(
        Dictionary<string, object> experimentData,
        IReadOnlyDictionary<string, string[]>? conflictsPerField = null)
            {
                var changes = new List<string>();

                foreach (var field in AccDefaults.TargetFields)
                {
                    bool hasConflict = conflictsPerField != null
                                       && conflictsPerField.TryGetValue(field, out var vals)
                                       && vals != null && vals.Where(v => !string.IsNullOrWhiteSpace(v))
                                                              .Select(v => v.Trim())
                                                              .Distinct(StringComparer.OrdinalIgnoreCase)
                                                              .Skip(1) // more than one distinct value
                                                              .Any();

                    bool missing =
                 !experimentData.TryGetValue(field, out var raw) ||
                 raw is null ||
                 (raw is string s && string.IsNullOrWhiteSpace(s)) ||
                 (raw is List<string> ls && ls.Count == 0);

                    if (missing || hasConflict)
                    {
                        var useNA = AccDefaults.UseNAForField.TryGetValue(field, out var flag) && flag;
                        var defaultValue = useNA ? AccDefaults.NotApplicable : string.Empty;

                        // If the current slot is a list, set a 1-item list; otherwise set a scalar
                        if (experimentData.TryGetValue(field, out var current) && current is List<string>)
                            experimentData[field] = string.IsNullOrEmpty(defaultValue) ? new List<string>() : new List<string> { defaultValue };
                        else
                            experimentData[field] = defaultValue;

                        changes.Add($"{field}: {(missing ? "missing" : "conflict")} → \"{defaultValue}\"");
                    }

                }

                return (experimentData, changes);
            }


            //string notebookID = "journal:defa5f6c-86ef-4c34-981f-ac66c53783ae";
            string notebookID = xd1.Element("UploadConfig")?.Element("notebookID")?.Value;
            MapEntries(dctAttributes);
            string expID = null;
            // if (!string.IsNullOrEmpty(notebookID))
            {
                try
                {

                    expID = SNBAPI.CreateExpinSNB("", dctAttributes, notebookID, expTemplateID);
                    Console.WriteLine($"Successfully created experiment with ID: {expID}");
                    string shareId = SNBAPI.GetExperimentShareId(expID, apiKey, tenantUrl);
                    string prorzOwnerEmail = experimentData["Document Owner"].ToString(); // Already validated earlier
                    string prorzOwnerId = SNBAPI.GetPRORZOwnerId(apiKey, prorzOwnerEmail);

                    if (!string.IsNullOrEmpty(prorzOwnerId) && !string.IsNullOrEmpty(shareId))
                    {
                        Console.WriteLine($"🔄 Updating ownership for experiment {expID} (shareId: {shareId})...");

                        try
                        {
                            // 🔹 Get the PRORZ Owner full details
                            var prorzOwnerDetails = SNBAPI.GetUserDetails(prorzOwnerId, apiKey, tenantUrl);
                            if (prorzOwnerDetails == null)
                            {
                                Console.WriteLine($"⚠️ Failed to retrieve details for PRORZ Owner ID: {prorzOwnerId}");
                            }
                            else
                            {
                                MessageResponse updateResponse = AddNewShareForOwner(expID, prorzOwnerId, apiKey, tenantUrl);


                                if (updateResponse.Status == Constants.StatusCompleted)
                                {
                                    Console.WriteLine($"✅ Successfully updated PRORZ Owner for experiment {expID} to user {prorzOwnerId}");
                                }
                                else
                                {
                                    Console.WriteLine($"❌ Failed to update PRORZ Owner for experiment {expID}: {updateResponse.Data}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Error updating PRORZ Owner for experiment {expID}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ PRORZ Owner ID or Share ID is empty for {expID}. Skipping ownership update.");
                    }
                    bool hasLargeSections = await UploadContentSectionsAndAttachments(expID, experimentData);

                    if (closeExperimentsEnabled && !hasLargeSections)
                    {
                        SNBAPI.closeExperimentWithoutSigning(expID, "RZ Lotus Notes");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"⚠️ Not closing experiment {expID}. " +
                            $"Config closeExperiments={(closeExperimentsEnabled ? "1" : "0")}, " +
                            $"hasLargeSections={(hasLargeSections ? "true" : "false")}."
                        );
                    }


                    string documentID = experimentData.ContainsKey("ExperimentID") ? experimentData["ExperimentID"].ToString() : "N/A";

                    File.AppendAllText(experimentLogcsvFilePath, $"{expID},{documentID},Success,N/A\n");
                    File.AppendAllText(successtxt, $"{documentID}\n");

                    //SNBAPI.closeExperimentWithoutSigning(expID, "RZ Lotus Notes");
                }
                catch (Exception ex)
                {
                    if (!String.IsNullOrEmpty(expID))
                    {
                        SNBAPI.Trash(expID);
                    }
                    string documentID = experimentData.ContainsKey("ExperimentID") ? experimentData["ExperimentID"].ToString() : "N/A";
                    string sanitizedError = ex.Message.Replace(",", ";").Replace("\n", " ").Replace("\r", " "); // Prevents breaking CSV structure

                    Console.WriteLine($"❌ Error while creating experiment {experimentData["Title"]}: {ex.Message}");
                    File.AppendAllText(experimentLogcsvFilePath, $"{expID},{documentID},-,Experiment Creation,Failure,{sanitizedError}\n");
                }


            }
        }
    }
}


static MessageResponse AddNewShareForOwner(string expID, string newOwnerID, string apiKey, string tenantUrl)
{
    // 🔹 Step 1: Construct the Correct JSON Payload
    string createSharePayload = $@"
    {{
        ""data"": {{
            ""attributes"": {{
                ""hasFullControl"": true,
                ""user"": {{
                    ""userId"": ""{newOwnerID}""
                }}
            }}
        }}
    }}";

    // 🔹 Step 2: Send the POST Request
    string createShareEndpoint = $"{tenantUrl}/api/rest/v1.0/entities/{expID}/shares";
    MessageResponse createResponse = SNBAPI.PostContentToService(createShareEndpoint, createSharePayload, apiKey);

    return createResponse;
}


void MapEntries(Dictionary<string, string> dctAttributes)
{
    if (dctAttributes["Confidentiality Level"] == "1")
    {
        dctAttributes["Confidentiality Level"] = "Confidential";
    }
    else
    {
        dctAttributes["Confidentiality Level"] = "Strictly confidential";
    }
}

static string SanitizeString(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return "";

    // Remove unescaped quotes at the ends (causing double escaping)
    input = input.Trim();

    if (input.EndsWith("\"") && !input.EndsWith("\\\""))
        input = input.Substring(0, input.Length - 1);

    if (input.StartsWith("\"") && !input.StartsWith("\\\""))
        input = input.Substring(1);

    return input
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\t", "\\t")
        .Replace("\r", "")
        .Replace("\n", "\\n");
}




static string ValidateTechnique(IEnumerable<string> techniques)
{
    if (techniques == null || !techniques.Any())
    {
        Console.WriteLine("Invalid technique: (empty). Skipping this attribute.");
        return string.Empty;
    }

    var validTechniques = new HashSet<string>
    {
        "Crossing", "Phenotyping", "Mutagenesis", "Genebank"
    };

    var filteredTechniques = techniques.Where(validTechniques.Contains).ToList();

    if (!filteredTechniques.Any())
    {
        Console.WriteLine($"Invalid techniques: {string.Join(", ", techniques)}. Skipping this attribute.");
        return string.Empty;
    }

    return string.Join(", ", filteredTechniques); // ✅ Outputs in "Crossing, Phenotyping" format
}


async Task<bool> UploadContentSectionsAndAttachments(string experimentID, Dictionary<string, object> experimentData)
{
    bool hasLargeSections = false; // <-- track large sections

    // Fetch existing layout pages & experiment sections
    Dictionary<string, string> pages = SNBAPI.GetLayoutPages(experimentID);
    Dictionary<string, string> experimentSections = SNBAPI.GetExperimentChildren(experimentID);

    // CSV for logging external URLs (kept)
    string csvFilePath = Path.Combine(Environment.CurrentDirectory, "ExternalLinks.csv");
    if (!File.Exists(csvFilePath))
        File.WriteAllText(csvFilePath, "ExperimentID,SectionName,URL\n");

    // Sections to process from XML
    string[] sections = { "Introduction", "MethodMaterial", "Body", "project_discussion", "mile_conclusion", "treatments" };

    foreach (string sectionName in sections)
    {
        if (!(experimentData.ContainsKey(sectionName) && experimentData[sectionName] is Dictionary<string, object> section))
            continue;

        try
        {
            // 1) Extract HTML from XML
            string sectionHtml = section["Content"]?.ToString() ?? string.Empty;

            // 2) Parse & normalize
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(sectionHtml);

            TransformFontTagsToSpans(doc);
            EnsureTableCellBackgrounds(doc);

            // 3) <a> tags:
            //    - Log external URLs
            //    - Upload local attachments and rewrite href to SNB file entity
            var aTags = doc.DocumentNode.SelectNodes("//a");
            if (aTags != null)
            {
                foreach (var aTag in aTags)
                {
                    var href = aTag.GetAttributeValue("href", null);
                    var imgInLink = aTag.SelectSingleNode("img");

                    if (string.IsNullOrWhiteSpace(href)) continue;

                    bool isExternal =
                        href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("https", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("mailto", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("/Research/", StringComparison.OrdinalIgnoreCase) ||
                        href.StartsWith("/C", StringComparison.OrdinalIgnoreCase);

                    if (isExternal)
                    {
                        // Log external-only; we don't rewrite these
                        File.AppendAllText(csvFilePath, $"{experimentID},{sectionName},\"{href}\"{Environment.NewLine}");
                        continue;
                    }

                    // Local attachment path: upload and rewrite
                    if (!pages.ContainsKey("Attachments"))
                    {
                        string attachmentsPageID = SNBAPI.CreatePage(experimentID, "Attachments");
                        pages.Add("Attachments", attachmentsPageID);
                    }

                    try
                    {
                        string fileID = UploadAttachment(href, experimentID, pages["Attachments"]);

                        // If the link wraps an image, inline that image as base64 to keep the HTML portable
                        if (imgInLink != null)
                        {
                            string imgSrc = imgInLink.GetAttributeValue("src", null);
                            if (!string.IsNullOrEmpty(imgSrc) && File.Exists(imgSrc))
                            {
                                byte[] imgBytes = File.ReadAllBytes(imgSrc);
                                string mimeType = GetMimeType(imgSrc);
                                string base64 = Convert.ToBase64String(imgBytes);
                                imgInLink.SetAttributeValue("src", $"data:{mimeType};base64,{base64}");
                            }
                        }

                        // Rewrite anchor href to SNB entity link
                        aTag.SetAttributeValue("href", $"{tenantUrl}elements/entity/{fileID}");
                    }
                    catch (Exception attachmentEx)
                    {
                        string sanitizedError = attachmentEx.Message.Replace(",", ";").Replace("\n", " ").Replace("\r", " ");
                        Console.WriteLine($"❌ Failed to upload attachment {href}: {sanitizedError}");
                        File.AppendAllText(experimentLogcsvFilePath, $"{experimentID},{experimentData["ExperimentID"]},Attachments,{href},Failure,{sanitizedError}\n");
                    }
                }
            }

            // 4) Inline <img> (not inside <a>) when they point to local files
            var imgTags = doc.DocumentNode.SelectNodes("//img");
            if (imgTags != null)
            {
                foreach (var imgTag in imgTags)
                {
                    if (imgTag.ParentNode?.Name == "a") continue; // handled above
                    var imgSrc = imgTag.GetAttributeValue("src", null);
                    if (!string.IsNullOrEmpty(imgSrc) && File.Exists(imgSrc))
                    {
                        byte[] imgBytes = File.ReadAllBytes(imgSrc);
                        string mimeType = GetMimeType(imgSrc);
                        string base64 = Convert.ToBase64String(imgBytes);
                        imgTag.SetAttributeValue("src", $"data:{mimeType};base64,{base64}");
                    }
                }
            }

            Console.WriteLine($"entering for sectionName='{sectionName}'");
            Console.WriteLine($"section keys: {string.Join(", ", section.Keys)}");

            object valuesObj = null;
            bool hasValues = section.TryGetValue("Values", out valuesObj) || section.TryGetValue("values", out valuesObj);
            Console.WriteLine($"Section has Values key: {hasValues}; runtime type: {valuesObj?.GetType().FullName ?? "<null>"}");


            // 5) Treatments special case
            if (string.Equals(sectionName, "treatments", StringComparison.OrdinalIgnoreCase))
            {
                if (section.TryGetValue("Content", out var rawObj))
                {
                    var raw = rawObj?.ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        // Detect if it's "plain text + <br>" only (no other tags)
                        bool onlyBrTags = System.Text.RegularExpressions.Regex
                            .IsMatch(raw, @"^(?:[^<>]|<\s*br\s*/?\s*>)+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        string htmlToInject;

                        if (onlyBrTags)
                        {
                            // Split on various <br> spellings and newlines, trim, encode each token, then join with <br>
                            var parts = System.Text.RegularExpressions.Regex.Split(raw, @"(<\s*br\s*/?\s*>)|\r?\n")
                                .Where(s => !string.IsNullOrWhiteSpace(s) && !System.Text.RegularExpressions.Regex.IsMatch(s, @"<\s*br\s*/?\s*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                                .Select(s => System.Net.WebUtility.HtmlEncode(s.Trim()));

                            htmlToInject = string.Join("<br>", parts);
                        }
                        else
                        {
                            // Already proper HTML—just normalize <br> variants so later formatters don’t drop them
                            htmlToInject = System.Text.RegularExpressions.Regex.Replace(raw, @"<\s*br\s*/?\s*>", "<br>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        }

                        var bodyNode = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
                        bodyNode.InnerHtml = htmlToInject;
                        Console.WriteLine($"[Treatments] Injected Content HTML ({htmlToInject.Length} chars).");
                    }
                    else
                    {
                        Console.WriteLine("[Treatments] Content is empty.");
                    }
                }
                else
                {
                    Console.WriteLine("[Treatments] No Content key found.");
                }
            }



            // 6) Section name mapping to SNB section titles / filenames
            string fileName = sectionName switch
            {
                "Body" => "Results",
                "project_discussion" => "Discussion",
                "MethodMaterial" => "Method and materials",
                "treatments" => "Treatments",
                "mile_conclusion" => "Conclusion",
                _ => sectionName
            };

            // 7) Final cleanups (keep your existing helpers)
            CompressInlineImagesInHtmlDocument(doc);
            string fixedHtml = FixBulletAndMicroFormatting(doc.DocumentNode.OuterHtml);
            fixedHtml = ExpandRowSpansFlatten(fixedHtml);

            // 8) Size check and behavior switch
            byte[] sectionHtmlBytes = Encoding.UTF8.GetBytes(fixedHtml);
            int htmlSize = sectionHtmlBytes.Length;

            if (htmlSize > 512000)
            {//todo: ma lezem saker l exp ... men saker l exp bi tene tool ba3ed ma na3mol upload lal large section
                hasLargeSections = true; // <-- mark that this experiment has large sections
                // Find target section entity first (same logic you already have below)
                if (TryGetValueCI(experimentSections, fileName, out string sectionID) && !string.IsNullOrEmpty(sectionID))
                {
                    Console.WriteLine($"📦 Streaming large HTML into section '{fileName}' (ID: {sectionID}) in 300KB chunks…");
                    var largeLog = Path.Combine(Environment.CurrentDirectory, "LargeSections.csv");
                    string backupsDir = Path.Combine(Environment.CurrentDirectory, "SectionBackups");
                    // sanitize sectionID for file/log use
                    string cleanSectionId = sectionID.StartsWith("text:", StringComparison.OrdinalIgnoreCase)
                        ? sectionID.Substring(5)   // drop "text:"
                        : sectionID;

                    Directory.CreateDirectory(backupsDir);
                    string backupPath = Path.Combine(
                        backupsDir,
                        $"{fileName}_{cleanSectionId}.html"
                    );
                    File.WriteAllText(backupPath, fixedHtml, Encoding.UTF8);

                    if (!File.Exists(largeLog))
                    {
                        File.WriteAllText(
                            largeLog,
                            "ExperimentID,SectionID,SectionName,SizeBytes,BackupPath,Status,Error,CloseStatus,CloseError\n"
                        );

                    }

                    File.AppendAllText(
     largeLog,
     $"{experimentID},{sectionID},{fileName},{htmlSize},\"{backupPath}\",PENDING,,,\n"
 );


                    continue; // finished this section (NO UI paste in Tool 1)
                }
                else
                {
                    Console.WriteLine($"❌ Section '{fileName}' not found. Skipping replacement.");
                    continue;
                }
            }
            else // 9) Normal path (<=500KB): create/replace the autotext content
            {
                string finalHtml = FixBulletAndMicroFormatting(doc.DocumentNode.OuterHtml);
                finalHtml = ExpandRowSpansFlatten(finalHtml);
                byte[] finalBytes = Encoding.UTF8.GetBytes(finalHtml);

                // Try case-insensitive lookup first
                if (TryGetValueCI(experimentSections, fileName, out string sectionID) && !string.IsNullOrEmpty(sectionID))
                {
                    Console.WriteLine($"📝 Replacing content in Section '{fileName}' (ID: {sectionID})...");
                    try { SNBAPI.ReplaceAttachment(sectionID, finalBytes, "text/html"); }
                    catch (Exception ex) { LogErrorToCsv(sectionID, ex); }
                }
                else
                {
                    // Only auto-create the Treatments section
                    if (!string.Equals(fileName, "Treatments", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"❌ Section '{fileName}' not found. Skipping replacement.");
                        continue;
                    }

                    Console.WriteLine($"➕ Section '{fileName}' not found. Creating via UploadAttachment…");

                    // Use the same pattern as your overflow path: upload an HTML file to a stable page
                    const string sectionsPageName = "Treatments";
                    string sectionsPageId = EnsurePage(pages, experimentID, sectionsPageName);

                    try
                    {
                        string newSectionId = SNBAPI.UploadAttachment(
                            fileName,           // <- becomes the element/file name ("Treatments")
                            experimentID,
                            ".html",
                            finalBytes,
                            sectionsPageId
                        );

                        // Remember the id so the rest of this run (and next runs) can ReplaceAttachment
                        experimentSections[fileName] = newSectionId;
                        Console.WriteLine($"✅ Created new section '{fileName}' (ID: {newSectionId}) under '{sectionsPageName}'.");
                    }
                    catch (Exception ex)
                    {
                        string sanitized = ex.Message.Replace(",", ";").Replace("\n", " ").Replace("\r", " ");
                        Console.WriteLine($"❌ Failed to create new section '{fileName}': {sanitized}");
                        LogErrorToCsv($"create:{fileName}", ex);
                        continue;
                    }
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing HTML content for section {sectionName}: {ex.Message}");
        }
    }
    return hasLargeSections;
}

static void OpenInChrome(string url)
{
    try
    {
#if WINDOWS
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    @"Google\Chrome\Application\chrome.exe")
        };
        var chrome = Array.Find(paths, File.Exists);

        if (chrome != null)
            Process.Start(new ProcessStartInfo(chrome, $"\"{url}\"") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); // fallback: default browser
#elif OSX
        Process.Start("open", url);
#elif LINUX
        Process.Start("xdg-open", url);
#else
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
#endif
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Couldn't open browser: {ex.Message}");
    }
}

// FLATTEN rowspans: keep text in first row; follower rows get empty placeholder cells
// NOTE: Assumes no COLSPAN. If you have colspan too, tell me—we can extend the grid logic.
static string ExpandRowSpansFlatten(string html)
{
    var doc = new HtmlAgilityPack.HtmlDocument();
    doc.LoadHtml(html);

    foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
    {
        var rows = table.SelectNodes(".//tr")?.ToList();
        if (rows == null || rows.Count == 0) continue;

        var grid = new List<List<HtmlNode>>();
        var rowSpans = new Dictionary<(int r, int c), (HtmlNode origin, int remaining)>();

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var cells = row.SelectNodes("./th|./td")?.ToList() ?? new List<HtmlNode>();
            var outRow = new List<HtmlNode>();

            // we rebuild the row, so clear it
            foreach (var cell in cells) cell.Remove();

            int c = 0;
            while (true)
            {
                // insert carried cells first if any for this column
                if (rowSpans.TryGetValue((r, c), out var carry))
                {
                    // clone carried cell but blank the content
                    var clone = HtmlNode.CreateNode(carry.origin.OuterHtml);
                    clone.Attributes.Remove("rowspan");
                    clone.InnerHtml = "&nbsp;";

                    // make the seam less visible
                    var style = clone.GetAttributeValue("style", "");
                    if (!Regex.IsMatch(style, @"border-top\s*:", RegexOptions.IgnoreCase))
                        style = (style + ";border-top:none").Trim(';');
                    clone.SetAttributeValue("style", style);

                    outRow.Add(clone);

                    // schedule next carry if needed
                    var left = carry.remaining - 1;
                    rowSpans.Remove((r, c));
                    if (left > 0) rowSpans[(r + 1, c)] = (carry.origin, left);

                    c++;
                    continue;
                }

                // consume a real cell
                if (cells.Count == 0) break;
                var next = cells[0];
                cells.RemoveAt(0);

                outRow.Add(next);

                // register rowspan
                if (int.TryParse(next.GetAttributeValue("rowspan", "0"), out var rs) && rs > 1)
                {
                    next.Attributes.Remove("rowspan");
                    rowSpans[(r + 1, c)] = (next, rs - 1);
                }

                c++;
            }

            // put rebuilt cells back into the row
            foreach (var n in outRow) row.AppendChild(n);
            grid.Add(outRow);

            // now that we know row’s dominant bg, sync carried placeholders we just added
            // Only propagate IF the <tr> itself has a bg (no “dominant” inference)
            string rowBg = null;

            // read TR background from either bgcolor attr or style
            var trBgAttr = row.GetAttributeValue("bgcolor", null);
            if (!string.IsNullOrWhiteSpace(trBgAttr))
            {
                rowBg = trBgAttr;
            }
            else
            {
                var trStyle = row.GetAttributeValue("style", "");
                var m = Regex.Match(trStyle, @"background-color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (m.Success) rowBg = m.Groups[1].Value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(rowBg))
            {
                foreach (var td in row.SelectNodes("./td|./th") ?? Enumerable.Empty<HtmlNode>())
                {
                    // does the cell ALREADY have a bg?
                    bool hasAttrBg = !string.IsNullOrWhiteSpace(td.GetAttributeValue("bgcolor", null));
                    string style = td.GetAttributeValue("style", "");
                    bool hasStyleBg = Regex.IsMatch(style, @"background-color\s*:\s*[^;]+", RegexOptions.IgnoreCase);

                    if (!hasAttrBg && !hasStyleBg)
                        // fill only blank cells
                        td.SetAttributeValue("style",
                            (string.IsNullOrWhiteSpace(style) ? "" : style.Trim().TrimEnd(';') + ";")
                            + $"background-color:{rowBg}");
                }

                // strip row bg so it can’t bleed through
                row.Attributes.Remove("bgcolor");
                var rowStyle = row.GetAttributeValue("style", "");
                rowStyle = Regex.Replace(rowStyle, @"\s*background-color\s*:\s*[^;]+;?", "", RegexOptions.IgnoreCase).Trim(';');
                if (string.IsNullOrWhiteSpace(rowStyle)) row.Attributes.Remove("style");
                else row.SetAttributeValue("style", rowStyle);
            }


        }
    }

    return doc.DocumentNode.OuterHtml;
}
static List<string> CleanList(object obj)
{
    var src = obj as List<string> ?? new List<string>();
    return src
        .Select(s => s?.Trim() ?? "")
        .Where(s => !string.IsNullOrWhiteSpace(s) &&
                    !s.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static string FixBulletAndMicroFormatting(string html)
{
    // Fix improperly closed bullet lists: <ul><li>content</ul> → <ul><li>content</li></ul>
    html = Regex.Replace(
        html,
        @"(<ul[^>]*>\s*<li>)(.*?)(?=</ul>)",
        match =>
        {
            var content = match.Groups[2].Value;
            if (!content.Contains("</li>"))
                return $"{match.Groups[1].Value}{content}</li>";
            return match.Value;
        },
        RegexOptions.Singleline | RegexOptions.IgnoreCase
    );

    // Normalize µ formatting: prevent breaking lines due to extra spacing or tag misuse
    // Remove any &nbsp or line breaks between micro symbol and "L"
    html = Regex.Replace(
        html,
        @"<font color=[\""]?#3A3A3A[\""]?>µ</font>(\s|&nbsp;)*L",
        "<font color=\"#3A3A3A\">µ</font>L",
        RegexOptions.IgnoreCase
    );

    // Same for ng/µL or 2 µL etc.
    html = Regex.Replace(
        html,
        @"ng/<font color=[\""]?#3A3A3A[\""]?>µ</font>(\s|&nbsp;)*L",
        "ng/<font color=\"#3A3A3A\">µ</font>L",
        RegexOptions.IgnoreCase
    );

    html = Regex.Replace(
        html,
        @"\b(\d+)\s*<font color=[\""]?#3A3A3A[\""]?>µ</font>(\s|&nbsp;)*L",
        "$1 <font color=\"#3A3A3A\">µ</font>L",
        RegexOptions.IgnoreCase
    );

    return html;
}


void CompressInlineImagesInHtmlDocument(HtmlDocument doc)
{
    var imgNodes = doc.DocumentNode.SelectNodes("//img[starts-with(@src,'data:image/')]");
    if (imgNodes == null) return;

    foreach (var img in imgNodes)
    {
        try
        {
            var base64 = img.GetAttributeValue("src", "");
            var base64Data = base64.Substring(base64.IndexOf(",") + 1);
            byte[] imageBytes = Convert.FromBase64String(base64Data);

            using (var inputStream = new MemoryStream(imageBytes))
            using (var image = Image.Load<Rgba32>(inputStream))
            using (var outputStream = new MemoryStream())
            {
                var encoder = new JpegEncoder
                {
                    Quality = 75 // Adjust quality (e.g. 30–70) as needed
                };

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(1000, 1000) // Resize down if needed
                }));

                image.Save(outputStream, encoder);

                string newBase64 = Convert.ToBase64String(outputStream.ToArray());
                img.SetAttributeValue("src", $"data:image/jpeg;base64,{newBase64}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to compress image: {ex.Message}");
        }
    }
}

static async Task ValidateAttributesFromXml(XDocument xmlDoc, Dictionary<string, int> attributeMap, string apiKey, string tenantUrl)
{
    // 1. Prepare log files
    string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    string missingLog = Path.Combine(baseDir, "MissingAttributeValues.csv");

    if (!File.Exists(missingLog))
        File.WriteAllText(missingLog, "ExperimentID,AttributeName,MissingValue\n");


    // 2. Load SNB options for mapped attributes
    Dictionary<string, List<string>> snbOptionsMap = new();
    foreach (var kvp in attributeMap)
    {
        try
        {
            var values = await SNBAPI.GetAttributeOptionsAsync(tenantUrl, apiKey, kvp.Value);
            snbOptionsMap[kvp.Key] = values;
            Console.WriteLine($"✅ Loaded {values.Count} options for {kvp.Key}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to fetch SNB options for {kvp.Key}: {ex.Message}");
        }
    }

    // 3. Validate XML experiments
    foreach (var doc in xmlDoc.Descendants("document"))
    {
        string expId = doc.Attribute("unid")?.Value ?? "UNKNOWN";
        var fields = doc.Elements("field");

        foreach (var field in fields)
        {
            string rawAttrName = field.Attribute("name")?.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(rawAttrName)) continue;

            string attrName = attributeMap.Keys.FirstOrDefault(x =>
                string.Equals(x, rawAttrName, StringComparison.OrdinalIgnoreCase));

            if (attrName == null)
            {
                // Not in mapping — log as unexpected
                foreach (var val in field.Elements("values").Elements("value"))
                {
                    string raw = val.Value?.Trim() ?? "";

                }
                continue;
            }

            if (!snbOptionsMap.TryGetValue(attrName, out List<string> snbOptions)) continue;
            HashSet<string> snbLowerSet = snbOptions.Select(v => v.Trim().ToLower()).ToHashSet();

            List<string> xmlValues = new();
            foreach (var valNode in field.Elements("values").Elements("value"))
            {
                string val = valNode.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(val) && val.ToUpper() != "N/A")
                    xmlValues.Add(val);
            }

            foreach (string xmlVal in xmlValues)
            {
                if (!snbLowerSet.Contains(xmlVal.Trim().ToLower()))
                {
                    File.AppendAllText(missingLog, $"{expId},{attrName},{xmlVal}\n");
                    Console.WriteLine($"❌ {expId} | {attrName} -> Missing: {xmlVal}");
                }
            }
        }
    }
}


void LogErrorToCsv(string sectionID, Exception ex)
{
    string sanitizedError = ex.Message.Replace(",", ";").Replace("\n", " ").Replace("\r", " "); // Prevent CSV structure break
    string logEntry = $"{DateTime.Now},{sectionID},ReplaceAttachment,Failure,{sanitizedError}\n";

    try
    {
        File.AppendAllText(experimentLogcsvFilePath, logEntry);
    }
    catch (Exception logEx)
    {
        Console.WriteLine($"Failed to write to log file: {logEx.Message}");
    }
}

// Helper function to upload an attachment and return the file ID
string UploadAttachment(string filePath, string experimentID, string pageID)
{
    string fileType = Path.GetExtension(filePath);
    string fileName = Path.GetFileName(filePath);

    // Clean the filename (e.g., remove # and other reserved characters)
    string cleanFileName = Regex.Replace(fileName, "[#%&{}\\\\<>*?/ $!'\":@+`|=]", "_");
    string correctFileName = cleanFileName.Substring(cleanFileName.IndexOf("_") + 1);

    // Create a temp file path using the clean name
    string tempFilePath = Path.Combine(Path.GetTempPath(), cleanFileName);
    File.Copy(filePath, tempFilePath, overwrite: true);

    byte[] fileBytes = File.ReadAllBytes(tempFilePath);

    return SNBAPI.UploadAttachment(correctFileName, experimentID, fileType, fileBytes, pageID);
}

// Helper function to get MIME type
string GetMimeType(string filePath)
{
    var mimeTypes = new Dictionary<string, string>
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".svg", "image/svg+xml" },
        { ".pdf", "application/pdf" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".xlsm", "application/vnd.ms-excel.sheet.macroEnabled.12" },
        { ".xlsb", "application/vnd.ms-excel.sheet.binary.macroEnabled.12" },
        { ".xls", "application/vnd.ms-excel" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".docm", "application/vnd.ms-word.document.macroEnabled.12" },
        { ".doc", "application/msword" },
        { ".csv", "text/csv" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { ".pptm", "application/vnd.ms-powerpoint.presentation.macroEnabled.12" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".potx", "application/vnd.openxmlformats-officedocument.presentationml.template" },
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".bmp", "image/bmp" },
        { ".rtf", "text/plain" },
        { ".txt", "text/plain" },

        // Lab / research formats
        { ".fa", "application/fasta" },
        { ".fasta", "application/fasta" },
        { ".fastq", "application/fastq" },
        { ".fq", "application/fastq" },
        { ".gff3", "text/x-gff3" },
        { ".bed", "text/x-bed" },
        { ".prism", "application/octet-stream" },
        { ".pzfx", "application/xml" },
        { ".czi", "image/x-czi" },
        { ".heic", "image/heic" },
        { ".sgn", "application/octet-stream" },

        // Chemistry
        { ".cdxml", "chemical/x-cdxml" },
        { ".rxn", "chemical/x-mdl-rxnfile" },
        { ".cdx", "chemical/x-cdx" },

        // Compressed / archives
        { ".gz", "application/gzip" },
        { ".7z", "application/x-7z-compressed" },
        { ".zip", "application/octet-stream" },   // left as-is for consistency with SNBAPI
        { ".tar.gz", "application/gzip" },
        { ".tgz", "application/gzip" },

        // Misc
        { ".json", "application/json" },
        { ".ps", "application/postscript" },

        // Formats defaulted to octet-stream
        { ".mnova", "application/octet-stream" },
        { ".msg", "application/octet-stream" },
        { ".dts", "application/octet-stream" },
        { ".xps", "application/octet-stream" },
        { ".mov", "application/octet-stream" },   // kept as in SNBAPI
        { ".dna", "application/octet-stream" },
        { ".ble", "application/octet-stream" },
        { ".imp", "application/octet-stream" },
        { ".ltv", "application/octet-stream" },
        { ".xlt", "application/octet-stream" },
        { ".zdp", "application/octet-stream" },
        { ".pcrd", "application/octet-stream" },

        { ".mp4", "video/mp4" },
        { ".slk", "application/vnd.ms-excel" },
        { ".jfif", "image/jpeg" }

    };

    string extension = Path.GetExtension(filePath).ToLower();
    return mimeTypes.ContainsKey(extension) ? mimeTypes[extension] : "application/octet-stream";
}



void TransformFontTagsToSpans(HtmlDocument doc)
{
    var fontNodes = doc.DocumentNode.SelectNodes("//font[@color]");
    if (fontNodes == null) return;

    foreach (var fontNode in fontNodes)
    {
        string color = fontNode.GetAttributeValue("color", "#000");
        var span = HtmlNode.CreateNode("<span></span>");
        span.SetAttributeValue("style", $"color:{color}");

        // Move the inner HTML and child nodes
        span.InnerHtml = fontNode.InnerHtml;

        // Replace <font> with <span>
        fontNode.ParentNode.ReplaceChild(span, fontNode);
    }
}

void EnsureTableCellBackgrounds(HtmlDocument doc)
{
    var tdNodes = doc.DocumentNode.SelectNodes("//td[@bgcolor]");
    if (tdNodes == null) return;

    foreach (var td in tdNodes)
    {
        string color = td.GetAttributeValue("bgcolor", "#ffffff");
        td.SetAttributeValue("style", $"background-color:{color}");
        td.Attributes.Remove("bgcolor");
    }
}

static string GetProrzCreationDate(XElement experiment)
{
    var raw = XMLExtractor.GetFieldValue(experiment, "mile_start");
    if (string.IsNullOrWhiteSpace(raw)) return null;

    // ✅ Include date+time patterns (H:mm[:ss]) with both '-' and '/' separators
    string[] formats = {
        // date only
        "d-M-yyyy","dd-M-yyyy","d-MM-yyyy","dd-MM-yyyy",
        "d/M/yyyy","dd/M/yyyy","d/MM/yyyy","dd/MM/yyyy",
        "yyyy-MM-dd",

        // date + time (24h, allow single-digit hour)
        "d-M-yyyy H:mm","d-M-yyyy H:mm:ss",
        "dd-M-yyyy H:mm","dd-M-yyyy H:mm:ss",
        "d-MM-yyyy H:mm","d-MM-yyyy H:mm:ss",
        "dd-MM-yyyy H:mm","dd-MM-yyyy H:mm:ss",

        "d/M/yyyy H:mm","d/M/yyyy H:mm:ss",
        "dd/M/yyyy H:mm","dd/M/yyyy H:mm:ss",
        "d/MM/yyyy H:mm","d/MM/yyyy H:mm:ss",
        "dd/MM/yyyy H:mm","dd/MM/yyyy H:mm:ss",

        // ISO variants
        "yyyy-MM-dd HH:mm:ss","yyyy-MM-ddTHH:mm:ss","yyyy-MM-ddTHH:mm:ssZ"
    };

    if (DateTime.TryParseExact(
            raw.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,  // assume local clock in source
            out var dtExact)
       || DateTime.TryParse(
            raw.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out dtExact))
    {
        // Use just the date component at UTC midnight
        var utcMidnight = new DateTime(dtExact.Year, dtExact.Month, dtExact.Day, 0, 0, 0, DateTimeKind.Utc);
        return utcMidnight.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    Console.WriteLine($"⚠️ Could not parse mile_start '{raw}'. Skipping PRORZ Creation Date.");
    return null;
}

static bool TryGetValueCI(Dictionary<string, string> dict, string key, out string value)
{
    foreach (var kv in dict)
        if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
        { value = kv.Value; return true; }
    value = null; return false;
}

static string EnsurePage(Dictionary<string, string> pages, string experimentID, string pageName)
{
    if (!TryGetValueCI(pages, pageName, out var pageId) || string.IsNullOrEmpty(pageId))
    {
        pageId = SNBAPI.CreatePage(experimentID, pageName);
        pages[pageName] = pageId;
        Console.WriteLine($"➕ Created page '{pageName}' (ID: {pageId})");
    }
    return pageId;
}

public class AttributeMapping
{
    public string Name { get; set; }
    public int AttributeId { get; set; }
}



public static class SNBUI
{
    public static async Task PasteSourceAndSaveAsync(string tenantUrl, string apiKey, string sectionId, string fullHtml)
    {
        await BrowserManager.InitAsync(tenantUrl); // ensures window is up & session is ready
        var page = await BrowserManager.NewPageAsync(); // NEW TAB (left open)

        var editUrl = $"{tenantUrl}elements/entity/{sectionId}/edit";

        // Go to edit view
        await page.GotoAsync(editUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120_000 });

        // Open View → Source code
        await page.ClickAsync("text=View");
        await page.WaitForTimeoutAsync(3000);
        await page.ClickAsync("text=Source code");

        // Source dialog
        var dialog = page.Locator(".tox-dialog");
        await dialog.WaitForAsync(new() { Timeout = 500_000 });

        var ta = dialog.Locator("textarea");
        await ta.WaitForAsync();

        // Clear then chunk-fill
        await page.EvaluateAsync(@"(sel) => { 
        const el = document.querySelector(sel); if (el) { el.value=''; el.dispatchEvent(new Event('input',{bubbles:true})); }
    }", ".tox-dialog textarea");

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(fullHtml);
        int i = 0;
        while (i < bytes.Length)
        {
            int take = System.Math.Min(300_000, bytes.Length - i);
            string part = System.Text.Encoding.UTF8.GetString(bytes, i, take);
            await page.EvaluateAsync(@"({ sel, chunk }) => {
            const el = document.querySelector(sel);
            el.value += chunk;
            el.dispatchEvent(new Event('input', { bubbles: true }));
        }", new { sel = ".tox-dialog textarea", chunk = part });
            i += take;
        }
        await page.WaitForTimeoutAsync(5000);

        // Save dialog
        await dialog.Locator("button:has-text('Save')").ClickAsync();

        // Save the page/section
        var saved = false;
        foreach (var sel in new[] {
        "button:has-text('Save')",
        "[data-testid='save']",
        "button[type='submit']",
        "button:has-text('Publish')",
        "button:has-text('Apply')"
    })
        {
            var b = await page.QuerySelectorAsync(sel);
            if (b is not null)
            {
                await b.ClickAsync(new() { Timeout = 7000 });
                saved = true;
                break;
            }
        }
        if (!saved)
        {
            await page.EvaluateAsync(@"() => { const f = document.querySelector('form'); if (f) f.submit(); }");
        }

        await page.WaitForTimeoutAsync(15000);
        System.Console.WriteLine("✅ Replaced section source via TinyMCE and saved. Tab left open as requested.");
        // IMPORTANT: do NOT close the page (tab). Leave it open.
    }

    // ---------- helpers ----------

    private static async Task TryEnterEditMode(IPage page)
    {
        string[] selectors =
        {
            "button:has-text('Edit')",
            "a:has-text('Edit')",
            "[data-testid='edit']",
            "button[title='Edit']",
            "button:has-text('Modify')",
            "button:has-text('Update')",
            "button:has-text('Edit content')",
            // pop/open menus then choose Edit
            "[aria-label='More']",
            "button:has-text('More')",
            "button:has-text('Actions')",
            "[aria-label='Actions']"
        };

        foreach (var sel in selectors)
        {
            var el = await page.QuerySelectorAsync(sel);
            if (el != null)
            {
                Console.WriteLine($"• Click {sel}");
                await el.ClickAsync(new() { Timeout = 300000 });
                await page.WaitForTimeoutAsync(400);

                // If it was a menu, try clicking Edit inside it
                var nestedEdit = await page.QuerySelectorAsync("text=Edit, text=Edit content, [data-testid='edit']");
                if (nestedEdit != null)
                {
                    Console.WriteLine("• Click nested Edit");
                    await nestedEdit.ClickAsync(new() { Timeout = 300000 });
                    await page.WaitForTimeoutAsync(400);
                }
                break;
            }
        }
    }

    private static async Task TryEnterSourceMode(IPage page)
    {
        string[] sels =
        {
            "button:has-text('Source')",
            "[aria-label='Source']",
            "button:has-text('HTML')",
            "button[title='Source']",
            "[data-command='source']",
            "button:has-text('<>')",
            "button[aria-label='View source']",
            "button:has-text('Code')",
            "button[title='Code']",
        };

        foreach (var sel in sels)
        {
            var el = await page.QuerySelectorAsync(sel);
            if (el != null)
            {
                Console.WriteLine($"• Click {sel}");
                await el.ClickAsync(new() { Timeout = 2000 });
                await page.WaitForTimeoutAsync(30000);
                break;
            }
        }
    }

    private sealed class EditableTarget
    {
        public Func<Task> Clear { get; init; }
        public Func<string, Task> Append { get; init; }
    }

    private static async Task<EditableTarget?> FindEditableTarget(IPage page)
    {
        // main frame
        var main = await ResolveEditableIn(page.MainFrame);
        if (main != null) return main;

        // iframes
        foreach (var frame in page.Frames)
        {
            if (frame == page.MainFrame) continue;
            var inside = await ResolveEditableIn(frame);
            if (inside != null) return inside;
        }
        return null;
    }

    private static async Task<EditableTarget?> ResolveEditableIn(IFrame frame)
    {
        // TinyMCE / generic editable body
        if (await frame.QuerySelectorAsync("body") is not null)
        {
            bool looksEditable = await frame.EvaluateAsync<bool>(
                @"() => {
                    const b = document.body;
                    if (!b) return false;
                    if (b.classList.contains('mce-content-body')) return true;
                    if (b.getAttribute('contenteditable') === 'true') return true;
                    return !!document.querySelector('div[contenteditable], p, table, div'); // heuristic
                }");
            if (looksEditable)
            {
                return new EditableTarget
                {
                    Clear = () => frame.EvaluateAsync("() => { document.body.innerHTML = '' }"),
                    Append = (t) => frame.EvaluateAsync("(html) => { document.body.insertAdjacentHTML('beforeend', html) }", t)
                };
            }
        }

        // CKEditor 5
        if (await frame.QuerySelectorAsync("[contenteditable='true']") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.EvaluateAsync(@"() => {
                    const el = document.querySelector('[contenteditable=""true""]'); if (el) el.innerHTML = '';
                }"),
                Append = (t) => frame.EvaluateAsync(@"(s) => {
                    const el = document.querySelector('[contenteditable=""true""]'); if (el) el.insertAdjacentHTML('beforeend', s);
                }", t)
            };
        }

        // CKEditor 4 Source
        if (await frame.QuerySelectorAsync("textarea.cke_source") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.FillAsync("textarea.cke_source", ""),
                Append = (t) => frame.EvaluateAsync(@"(s) => {
                    const ta = document.querySelector('textarea.cke_source');
                    ta.value += s; ta.dispatchEvent(new Event('input', { bubbles: true }));
                }", t)
            };
        }

        // CodeMirror
        if (await frame.QuerySelectorAsync(".CodeMirror") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.EvaluateAsync(@"() => {
                    const cm = document.querySelector('.CodeMirror')?.CodeMirror; if (cm) cm.setValue('');
                }"),
                Append = (t) => frame.EvaluateAsync(@"(s) => {
                    const cm = document.querySelector('.CodeMirror')?.CodeMirror; if (cm) cm.replaceRange(s, { line: cm.lastLine(), ch: 0 });
                }", t)
            };
        }

        // Quill
        if (await frame.QuerySelectorAsync(".ql-editor") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.EvaluateAsync(@"() => { const el = document.querySelector('.ql-editor'); if (el) el.innerHTML=''; }"),
                Append = (t) => frame.EvaluateAsync(@"(s) => { const el = document.querySelector('.ql-editor'); if (el) el.insertAdjacentHTML('beforeend', s); }", t)
            };
        }

        // Froala
        if (await frame.QuerySelectorAsync(".fr-element") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.EvaluateAsync(@"() => { const el = document.querySelector('.fr-element'); if (el) el.innerHTML=''; }"),
                Append = (t) => frame.EvaluateAsync(@"(s) => { const el = document.querySelector('.fr-element'); if (el) el.insertAdjacentHTML('beforeend', s); }", t)
            };
        }

        // Monaco (VS Code editor) — sometimes used for HTML source
        if (await frame.QuerySelectorAsync(".monaco-editor") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.EvaluateAsync(@"() => {
                    const e = (window as any).monaco?.editor?.getEditors?.()?.[0];
                    if (e) e.setValue('');
                }"),
                Append = (t) => frame.EvaluateAsync(@"(s) => {
                    const e = (window as any).monaco?.editor?.getEditors?.()?.[0];
                    if (e) e.setValue(e.getValue() + s);
                }", t)
            };
        }

        // Generic contenteditable
        if (await frame.QuerySelectorAsync("[contenteditable]") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.EvaluateAsync(@"() => {
                    const el = document.querySelector('[contenteditable]'); if (el) el.innerHTML='';
                }"),
                Append = (t) => frame.EvaluateAsync(@"(s) => {
                    const el = document.querySelector('[contenteditable]'); if (el) el.insertAdjacentHTML('beforeend', s);
                }", t)
            };
        }

        // Plain textarea
        if (await frame.QuerySelectorAsync("textarea") is not null)
        {
            return new EditableTarget
            {
                Clear = () => frame.FillAsync("textarea", ""),
                Append = (t) => frame.EvaluateAsync(@"(s) => {
                    const ta = document.querySelector('textarea'); ta.value += s;
                    ta.dispatchEvent(new Event('input', { bubbles: true }));
                }", t)
            };
        }

        return null;
    }
}

// ACC default constants (exact casing as requested by Rob)
internal static class AccDefaults
{
    public const string NotApplicable = "*Not Applicable";

    // Toggle per field if ACC prefers empty instead of "Not Applicable"
    // (Leave all 'true' to use "Not Applicable" unless you’re told to leave empty.)
    public static readonly Dictionary<string, bool> UseNAForField = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Crop"] = true,
        ["CropSpecies"] = false,
        ["Genus"] = true,
        ["Pathogen"] = false
    };

    // The four fields we normalize for ACC testing
    public static readonly string[] TargetFields = { "Crop", "CropSpecies", "Genus", "Pathogen" };
}

public static class BrowserManager
{
    private static IPlaywright _playwright;
    private static IBrowser _browser;
    private static IBrowserContext _context;
    private static readonly object _lock = new();

    private const string StorageFile = "playwright.state.json";

    public static async Task InitAsync(string tenantUrl)
    {
        if (_playwright != null) return; // already initialized

        lock (_lock)
        {
            if (_playwright != null) return;
        }

        var pw = await Playwright.CreateAsync();
        var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 1500
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = File.Exists(StorageFile) ? StorageFile : null
        });

        // Prime login once if needed
        var page = await context.NewPageAsync();
        await page.GotoAsync(tenantUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        if (!File.Exists(StorageFile))
        {
            await page.WaitForTimeoutAsync(8000); // give you time to sign in once
            await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = StorageFile });
        }
        // Keep the first tab (and window) open; do NOT close it.

        lock (_lock)
        {
            _playwright = pw;
            _browser = browser;
            _context = context;
        }
    }

    public static async Task<IPage> NewPageAsync()
    {
        if (_context == null) throw new System.InvalidOperationException("BrowserManager not initialized. Call InitAsync first.");
        return await _context.NewPageAsync(); // caller decides whether to close; you wanted to keep tabs open
    }

    // Optional: app shutdown hook if you ever want to close everything.
    public static async Task DisposeAsync()
    {
        await _context?.CloseAsync();
        await _browser?.CloseAsync();
        _playwright?.Dispose();
        _playwright = null; _browser = null; _context = null;
    }
}
