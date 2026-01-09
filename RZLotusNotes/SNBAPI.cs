using Common.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace spr_live_migration
{
    public class SNBAPI
    {
        private static readonly string logsFolders = @"C:\LiveSNB";
        private static string destURL = "";
        private static string apiKey = "";
        public static string FetchNBinSNB(string notebookName)
        {
            string id = string.Empty;
            Dictionary<string, string> dctHeaders = new Dictionary<string, string>()
            {
                { "accept", "application/vnd.api+json" }
            };

            //check if notebook exists
            MessageResponse getResponse = GetContentFromService(destURL + Constants.EntitiesEndpoint + "?page%5Boffset%5D=0&page%5Blimit%5D=100&includeTypes=journal", apiKey);
            if (getResponse.Status == Constants.StatusCompleted)
            {
                JObject data = JObject.Parse(getResponse.Data);
                JToken responseNotebook = data["data"].Children().FirstOrDefault(x => x.ToString().Contains(notebookName));

                if (responseNotebook == null ||
                    (responseNotebook != null && (
                    responseNotebook["attributes"]["flags"]["isTrashed"] != null &&
                    responseNotebook["attributes"]["flags"]["isTrashed"].ToString().ToLower() == "true")))
                {
                    throw new Exception("Notebook " + notebookName + " Not found in SNB)");
                }
                else
                {
                    id = data["data"].Children().FirstOrDefault(x => x.ToString().Contains(notebookName))["attributes"]["id"].ToString();
                }
            }
            else
            {
                Log(getResponse.Data, "CreateNotebook");
            }

            return id;
        }

        internal static void RemoveEmptyPages(string snbExpId)
        {
            string layout = SNBLayout.getLayout(destURL, apiKey, snbExpId);
            ExperimentLayout jresp = System.Text.Json.JsonSerializer.Deserialize<ExperimentLayout>(layout);
            foreach (Common.Models.Data s in jresp.data)
            {
                if (s.attributes.entities.Count == 0)
                {
                    if (s.attributes.index == 0)
                    {
                        continue;
                        //first section handled after all other sections cleaned
                    }
                    SNBLayout.deletePage(destURL, apiKey, snbExpId, s.attributes.id);
                }
            }
            // handle first section by moving it down then deleting
            if (jresp.data[0].attributes.entities.Count == 0)
            {
                SNBLayout.updatePageLayout(destURL, apiKey, snbExpId, jresp.data[0].attributes.id, jresp.data[0].attributes.name, 1);
                SNBLayout.deletePage(destURL, apiKey, snbExpId, jresp.data[0].attributes.id);
            }
        }
        public static void UpdatePageContent(string pageID, string content)
        {
            string endpoint = $"/api/rest/v1.0/entities/{pageID}/content?force=true";
            string body = $"{{\"data\":{{\"type\":\"page\",\"attributes\":{{\"content\":\"{content}\"}}}}}}";

            try
            {
                MessageResponse response = PATCHContentToService(destURL + endpoint, body, apiKey);
                if (response.Status != Constants.StatusCompleted)
                {
                    throw new Exception("Error updating page content: " + response.Data);
                }
                else
                {
                    Console.WriteLine($"Page {pageID} content updated successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while updating page {pageID}: {ex.Message}");
                throw;
            }
        }


        public static string CreateNBinSNB(string notebookName)
        {
            string id = string.Empty;
            Dictionary<string, string> dctHeaders = new Dictionary<string, string>()
            {
                { "accept", "application/vnd.api+json" }
            };

            //check if notebook exists
            MessageResponse getResponse = GetContentFromService(destURL + Constants.EntitiesEndpoint + "?page%5Boffset%5D=0&page%5Blimit%5D=100&includeTypes=journal", apiKey);
            if (getResponse.Status == Constants.StatusCompleted)
            {
                JObject data = JObject.Parse(getResponse.Data);
                JToken responseNotebook = data["data"].Children().FirstOrDefault(x => x.ToString().Contains(notebookName));

                if (responseNotebook == null ||
                    (responseNotebook != null && (
                    responseNotebook["attributes"]["flags"]["isTrashed"] != null &&
                    responseNotebook["attributes"]["flags"]["isTrashed"].ToString().ToLower() == "true")))
                {
                    string body = "{\"data\":{\"type\":\"journal\",\"attributes\":{\"name\":\"" + notebookName + "\",\"Notebook disabled\":\"Enabled\"}}}";
                    MessageResponse postResponse = PostContentToService(destURL + Constants.EntitiesEndpoint, body, apiKey);
                    if (postResponse.Status == Constants.StatusCompleted)
                    {
                        JObject returnNotebook = JObject.Parse(postResponse.Data);
                        id = returnNotebook["data"]["id"].ToString();
                    }
                    else
                    {
                        Log(postResponse.Data, "CreateNotebook");
                    }
                }
                else
                {
                    id = data["data"].Children().FirstOrDefault(x => x.ToString().Contains(notebookName))["attributes"]["id"].ToString();
                }
            }
            else
            {
                Log(getResponse.Data, "CreateNotebook");
            }

            return id;
        }

        public static void FindAndTrash(string experimentId)
        {
            string body = "{  \"query\": {                \"$and\": [                  {                    \"$and\": [                      { " +
                        "\"$match\": {                            \"field\": \"name\",\"as\": \"text\",\"value\": \"" + experimentId + "\"," +
              "\"mode\": \"keyword\" }         }        ]      },      {                    \"$match\": {\"field\": \"type\", " +
          "\"value\": \"experiment\",\"mode\": \"keyword\"   }    }  ]  } }";
            string endpoint = "/api/rest/v1.0/entities/search?page%5Boffset%5D=0&page%5Blimit%5D=1&stopAfterItems=1000000";
            try
            {
                using (HttpClient client = new HttpClient())
                {

                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, destURL + endpoint))
                    {
                        Console.Write("POST\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);

                        request.Content = new StringContent(body, null, "application/vnd.api+json");


                        using (HttpResponseMessage response = client.SendAsync(request).Result)
                        {
                            Console.Write((int)response.StatusCode + "\n");

                            if (response.IsSuccessStatusCode)
                            {
                                string resp = response.Content.ReadAsStringAsync().Result;
                                JObject returnTemplate = JObject.Parse(resp);
                                string expID = returnTemplate["data"][0]["id"].ToString();
                                //trash
                                SNBAPI.Trash(expID);

                            }
                            else
                            {
                                string errorMessage = response.Content.ReadAsStringAsync().Result;
                                Console.WriteLine("Experiment finding Failed" + errorMessage);
                            }


                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Experiment trashing Failed\nCritical\n" + ex.Message);
                throw ex;
            }
        }

        public static string FindNotebook(string entityName)
        {
            string body = "{  \"query\": {                \"$and\": [                  {                    \"$and\": [                      { " +
                        "\"$match\": {                            \"field\": \"name\",\"as\": \"text\",\"value\": \"" + entityName + "\"," +
              "\"mode\": \"keyword\" }         }        ]      },      {                    \"$match\": {\"field\": \"type\", " +
          "\"value\": \"journal\",\"mode\": \"keyword\"   }    }  ]  } }";
            string endpoint = "/api/rest/v1.0/entities/search?page%5Boffset%5D=0&page%5Blimit%5D=1&stopAfterItems=100000";
            string expID = null;
            try
            {
                using (HttpClient client = new HttpClient())
                {

                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, destURL + endpoint))
                    {
                        Console.Write("POST\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);

                        request.Content = new StringContent(body, null, "application/vnd.api+json");


                        using (HttpResponseMessage response = client.SendAsync(request).Result)
                        {
                            Console.Write((int)response.StatusCode + "\n");

                            if (response.IsSuccessStatusCode)
                            {
                                string resp = response.Content.ReadAsStringAsync().Result;
                                JObject returnTemplate = JObject.Parse(resp);
                                try
                                {
                                    expID = returnTemplate["data"][0]["id"].ToString();
                                }
                                catch
                                {
                                    Console.WriteLine("Notebook not found");
                                    throw;
                                }

                            }
                            else
                            {
                                string errorMessage = response.Content.ReadAsStringAsync().Result;
                                Console.WriteLine("Notebook finding Failed" + errorMessage);
                            }


                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Notebook not found\n" + ex.Message);
                throw ex;
            }
            return expID;
        }
        public static bool validateExperiments(string exp, int counter)
        {
            try
            {
                string id = exp;

                string endpoint = "api/rest/v1.0/entities/" + id;

                string resp = GetContentFromService(destURL + endpoint, new Dictionary<string, string>(), apiKey);


                JObject job = JObject.Parse(resp);

                var jTrashed = job.SelectToken("data.attributes.flags.isTrashed");
                if (jTrashed != null && job.SelectToken("data.attributes.flags.isTrashed").ToObject<bool>() == true)
                {
                    return false;
                }


                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(exp + "," + counter + "," + ex.Message);
                Console.ReadLine();
                return false;
            }

        }

        public static string GetContentFromService(string url, Dictionary<string, string> headers, string apiKey)
        {
            // Console.Write("Get" + "\t| " + url + "\t");
            try
            {
                using (HttpClient client = new HttpClient())
                {

                    HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
                    webRequest.Headers.Add("x-api-key", apiKey);
                    webRequest.ContentType = "application/vnd.api+json";

                    webRequest.Method = "GET";

                    foreach (string headerName in headers.Keys)
                    {
                        if (headerName.ToLower() != "content-type")
                        {
                            webRequest.Headers.Add(headerName, headers[headerName]);
                        }
                    }
                    //    Console.Write(HttpMethod.Get.ToString() + "\t| " + url + "\t");
                    HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();
                    //Console.Write((int)response.StatusCode + "\n");

                    if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Accepted)
                    {
                        string result = string.Empty;
                        using (var streamReader = new StreamReader(response.GetResponseStream()))
                        {
                            result = streamReader.ReadToEnd();

                        }

                        return result;
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return response.StatusCode.ToString();
                    }
                    else
                    {
                        throw new Exception(response.ToString());
                    }
                }
            }
            catch (TimeoutException timeEx)
            {
                throw timeEx;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public static void RenameEntity(string entityId, string newName)
        {
            string body = "{\"data\": [ {\"attributes\": {\"name\": \"Name\",\"value\": \"" + newName + "\"}}]}";
            MessageResponse response = PATCHContentToService(destURL + "/api/rest/v1.0/entities/" + entityId + "/properties?force=true", body, apiKey);
            if (response.Status == Constants.StatusCompleted)
            {
                JObject val = JObject.Parse(response.Data);
            }
            else
            {
                throw new Exception("Error renaming entity:" + response.Data);
            }
        }

        public static void PatchAttributes(string attributeId, Dictionary<string, List<string>> options)
        {
            string id = string.Empty;

            StringBuilder sb = new StringBuilder();
            foreach (var option in options)
            {
                StringBuilder sbValues = new StringBuilder();
                foreach (var value in option.Value)
                {
                    sbValues.Append("\"" + value.ToString() + "\",");
                }
                string strValues = sbValues.Length != 0 ? sbValues.ToString().Remove(sbValues.ToString().Length - 1) : "";
                sb.Append("{ \"parentValue\": \"" + option.Key + "\", \"options\": [ " + strValues + " ] },");
            }
            string strOptions = sb.Length != 0 ? sb.ToString().Remove(sb.ToString().Length - 1) : "";

            string json = "{ \"data\": { \"type\": \"attribute\", \"id\": \"" + attributeId + "\", \"attributes\": { \"hierarchicalOptions\": [ " + strOptions + " ] } } }";

            Dictionary<string, string> dctHeader = new Dictionary<string, string>();

            MessageResponse response = PATCHContentToService(destURL + "/api/rest/v1.0/attributes/" + attributeId, json.Replace("\\", "\\\\"), apiKey);

            if (response.Status == Constants.StatusCompleted)
            {
                JObject returnNotebook = JObject.Parse(response.Data);
            }
            else
            {
                throw new Exception("Error creating experiment:" + response.Data);
            }

        }

        public static string CreatePage(string expID, string pageName)
        {
            string createResp = SNBLayout.createPage(destURL, apiKey, expID, pageName);
            CreatePageResp jresultCreation = System.Text.Json.JsonSerializer.Deserialize<CreatePageResp>(createResp);
            return jresultCreation.data.id;
        }

        public static void Trash(string entityId)
        {
            string endpoint = "/api/rest/v1.0/entities/" + entityId + "?force=true";

            using (HttpClient client = new HttpClient())
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, destURL + endpoint))
                {
                    Console.Write("DELETE\t| " + endpoint + "\t");
                    request.Headers.Add("x-api-key", apiKey);
                    using (HttpResponseMessage response = client.SendAsync(request).Result)
                    {
                        Console.Write((int)response.StatusCode + "\n");
                        if (response.IsSuccessStatusCode)
                        {
                            // return response.Content.ReadAsStringAsync().Result;
                        }
                        else
                        {
                            string errorMessage = response.Content.ReadAsStringAsync().Result;
                            Console.WriteLine("Deleting entity Failed" + errorMessage);
                            throw new Exception(errorMessage);
                        }
                    }
                }
            }
        }

        public static string getADTColumnDefinition(string tableId)
        {
            string endPoint = "/api/rest/v1.0/adt/" + tableId + "/_column";
            string resp = makeWebApiCall(HttpMethod.Get, endPoint, "Get ADT Column Definition");
            return resp;

        }

        public static string makeWebApiCall(HttpMethod httpMethod, string endPoint, string action, dynamic postData = null)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(httpMethod, destURL + endPoint))
                    {
                        Console.Write(httpMethod.ToString() + "\t| " + endPoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);

                        if (httpMethod != HttpMethod.Get && httpMethod != HttpMethod.Delete)
                        {
                            request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(postData), null, "application/vnd.api+json");
                        }

                        using (HttpResponseMessage response = client.SendAsync(request).Result)
                        {
                            Console.Write((int)response.StatusCode + "\n");
                            if (response.IsSuccessStatusCode)
                            {
                                return response.Content.ReadAsStringAsync().Result;
                            }
                            else
                            {
                                string errorMessage = response.Content.ReadAsStringAsync().Result;
                                Console.WriteLine(action + " Failed" + errorMessage);
                                throw new Exception(errorMessage);

                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                throw new Exception(ex.Message);
            }


        }

        public static string addTableTemplateToExperiment(string tableName, string tempID, List<string> experimentId)
        {
            try
            {
                var data = makeAddTableToExperimentObj(tableName, tempID, experimentId);
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                string endPoint = "/api/rest/v1.0/entities?force=true";
                string response = makeWebApiCall(HttpMethod.Post, endPoint, "Add table to Experiment", data);
                JObject jdata = JObject.Parse(response);
                JToken responseNotebook = jdata["data"].Children().FirstOrDefault(x => x.ToString().Contains("grid:"));

                return ((Newtonsoft.Json.Linq.JProperty)responseNotebook).Value.ToString();

            }
            catch (Exception ex)
            {

                Console.WriteLine(ex.Message);
                throw new Exception(ex.Message);
            }
        }

        public static string bulkUpdateRowsADT(string tableId, List<Dictionary<string, object>> dctRowvalues)
        {
            string json = getADTColumnDefinition(tableId);
            try
            {

                JObject jsonObject = JObject.Parse(json);
                JToken propertyValue = jsonObject.SelectToken("data.attributes.columns");
                JArray jColumns = (JArray)propertyValue;

                var data = mapBulkColumns(jColumns, dctRowvalues, "", "create");
                string jsondd = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                string endPoint = "/api/rest/v1.0/adt/" + tableId + "?force=true";
                return makeWebApiCall(new HttpMethod("PATCH"), endPoint, "Bulk Insert Rows ADT", data);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw new Exception(ex.Message);
            }
        }

        public static string bulkUpdateRowsADT(string tableId, List<Dictionary<string, string>> dctRowvalues)
        {
            string json = getADTColumnDefinition(tableId);
            try
            {

                JObject jsonObject = JObject.Parse(json);
                JToken propertyValue = jsonObject.SelectToken("data.attributes.columns");
                JArray jColumns = (JArray)propertyValue;

                var data = mapBulkColumns(jColumns, dctRowvalues, "", "create");
                string jsondd = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                string endPoint = "/api/rest/v1.0/adt/" + tableId + "?force=true";
                return makeWebApiCall(new HttpMethod("PATCH"), endPoint, "Bulk Insert Rows ADT", data);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw new Exception(ex.Message);
            }
        }

        public static dynamic mapBulkColumns(JArray ADTcolumns, List<Dictionary<string, string>> lstdctCol, string id, string action)
        {
            dynamic postObject = new System.Dynamic.ExpandoObject();
            postObject.data = new List<dynamic>();
            foreach (Dictionary<string, string> dctCol in lstdctCol)
            {
                var obj = makeRowObject(ADTcolumns, dctCol, id, action);
                ((List<dynamic>)postObject.data).Add(obj);
            }

            return postObject;
        }

        public static dynamic mapBulkColumns(JArray ADTcolumns, List<Dictionary<string, object>> lstdctCol, string id, string action)
        {
            dynamic postObject = new System.Dynamic.ExpandoObject();
            postObject.data = new List<dynamic>();
            foreach (Dictionary<string, object> dctCol in lstdctCol)
            {
                var obj = makeRowObject(ADTcolumns, dctCol, id, action);
                ((List<dynamic>)postObject.data).Add(obj);
            }
            // Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(postObject));
            return postObject;
        }

        private static dynamic makeAddTableToExperimentObj(string tableName, string tempId, List<string> experimentId)
        {
            dynamic postObj = new System.Dynamic.ExpandoObject();
            postObj.data = new System.Dynamic.ExpandoObject();
            postObj.data.type = "grid";
            postObj.data.attributes = new System.Dynamic.ExpandoObject();
            postObj.data.attributes.name = tableName;


            postObj.data.relationships = new System.Dynamic.ExpandoObject();
            postObj.data.relationships.ancestors = new System.Dynamic.ExpandoObject();
            postObj.data.relationships.template = new System.Dynamic.ExpandoObject();
            postObj.data.relationships.template.data = new System.Dynamic.ExpandoObject();
            postObj.data.relationships.template.data.type = "grid";
            postObj.data.relationships.template.data.id = tempId;
            postObj.data.relationships.ancestors.data = new List<System.Dynamic.ExpandoObject>();

            var ancestorsData = new List<System.Dynamic.ExpandoObject>();
            foreach (string id in experimentId)
            {
                dynamic ancestorsDataObj = new System.Dynamic.ExpandoObject();
                ancestorsDataObj.type = "experiment";
                ancestorsDataObj.id = id;
                ancestorsData.Add(ancestorsDataObj);
            }
            postObj.data.relationships.ancestors.data = ancestorsData;


            return postObj;

        }

        public static string postRowToADT(string tableId, Dictionary<string, object> dctRowvalues)
        {

            string json = getADTColumnDefinition(tableId);

            try
            {

                JObject jsonObject = JObject.Parse(json);
                JToken propertyValue = jsonObject.SelectToken("data.attributes.columns");
                JArray jColumns = (JArray)propertyValue;

                var data = mapColumns(jColumns, dctRowvalues);

                string endPoint = "/api/rest/v1.0/adt/" + tableId + "?force=true";
                string response = makeWebApiCall(HttpMethod.Post, endPoint, "Post Single Row to ADT", data);
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw new Exception(ex.Message);
            }
        }

        public static string postRowToADT(string tableId, Dictionary<string, string> dctRowvalues)
        {

            string json = getADTColumnDefinition(tableId);

            try
            {

                JObject jsonObject = JObject.Parse(json);
                JToken propertyValue = jsonObject.SelectToken("data.attributes.columns");
                JArray jColumns = (JArray)propertyValue;

                var data = mapColumns(jColumns, dctRowvalues);

                string endPoint = "/api/rest/v1.0/adt/" + tableId + "?force=true";
                string response = makeWebApiCall(HttpMethod.Post, endPoint, "Post Single Row to ADT", data);
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw new Exception(ex.Message);
            }
        }

        private static dynamic mapColumns(JArray ADTcolumns, Dictionary<string, string> dctCol, string id = null, string action = null)
        {
            dynamic postObject = new System.Dynamic.ExpandoObject();
            postObject.data = new System.Dynamic.ExpandoObject();

            postObject.data = makeRowObject(ADTcolumns, dctCol, id, action);

            return postObject;

        }

        internal static Dictionary<string, string> GetLayoutPages(string snbExpId)
        {
            string layout = SNBLayout.getLayout(destURL, apiKey, snbExpId);
            ExperimentLayout jresp = System.Text.Json.JsonSerializer.Deserialize<ExperimentLayout>(layout);
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (Common.Models.Data s in jresp.data)
            {
                result.Add(s.attributes.name, s.id);
            }
            return result;
        }

        private static dynamic mapColumns(JArray ADTcolumns, Dictionary<string, object> dctCol, string id = null, string action = null)
        {
            dynamic postObject = new System.Dynamic.ExpandoObject();
            postObject.data = new System.Dynamic.ExpandoObject();

            postObject.data = makeRowObject(ADTcolumns, dctCol, id, action);

            return postObject;

        }

        private static dynamic makeRowObject(JArray ADTcolumns, Dictionary<string, object> dctCol, string id = null, string action = null)
        {
            dynamic postObject = new System.Dynamic.ExpandoObject();
            if (!string.IsNullOrEmpty(id))
            {
                postObject.id = id;
            }

            postObject.type = "adtRow";
            postObject.attributes = new System.Dynamic.ExpandoObject();
            if (!string.IsNullOrEmpty(action))
            {
                postObject.attributes.action = action;
            }


            if (action != "delete")
            {
                List<dynamic> lstcell = new List<dynamic>();
                List<string> lstTitles = new List<string>();
                foreach (string key in dctCol.Keys)
                {
                    lstTitles.Add(key);
                }
                //int i = 0;
                foreach (dynamic cols in ADTcolumns)
                {
                    string key = cols.key;
                    string title = cols.title;
                    string type = cols.type;
                    //   Console.WriteLine(" title:" + title + "|");
                    lstTitles.Remove(title);
                    if (!dctCol.ContainsKey(title))
                    {
                        //foreach (string kk in dctCol.Keys)
                        //{
                        //    Console.WriteLine(kk == title);
                        //}
                        // throw new Exception("Title missing :" + title + "|");
                    }
                    else
                    {

                        dynamic c = new System.Dynamic.ExpandoObject();
                        c.key = key;
                        c.content = new System.Dynamic.ExpandoObject();
                        c.content.value = dctCol[title];
                        lstcell.Add(c);


                    }


                }
                if (lstTitles.Count > 0)
                {
                    Console.WriteLine("missing titles");
                    for (int i = 0; i < lstTitles.Count; i++)
                    {
                        Console.WriteLine(lstTitles[i]);

                    }
                    Console.ReadLine();
                }
                postObject.attributes.cells = lstcell;

            }


            return postObject;

        }

        private static dynamic makeRowObject(JArray ADTcolumns, Dictionary<string, string> dctCol, string id = null, string action = null)
        {
            dynamic postObject = new System.Dynamic.ExpandoObject();
            if (!string.IsNullOrEmpty(id))
            {
                postObject.id = id;
            }

            postObject.type = "adtRow";
            postObject.attributes = new System.Dynamic.ExpandoObject();
            if (!string.IsNullOrEmpty(action))
            {
                postObject.attributes.action = action;
            }


            if (action != "delete")
            {
                List<dynamic> lstcell = new List<dynamic>();
                List<string> lstTitles = new List<string>();
                foreach (string key in dctCol.Keys)
                {
                    lstTitles.Add(key);
                }
                //int i = 0;
                foreach (dynamic cols in ADTcolumns)
                {
                    string key = cols.key;
                    string title = cols.title;
                    string type = cols.type;
                    //  Console.WriteLine(" title:" + title + "|");
                    lstTitles.Remove(title);
                    if (!dctCol.ContainsKey(title))
                    {
                        //foreach (string kk in dctCol.Keys)
                        //{
                        //    Console.WriteLine(kk == title);
                        //}
                        // throw new Exception("Title missing :" + title + "|");
                    }
                    else
                    {

                        dynamic c = new System.Dynamic.ExpandoObject();
                        c.key = key;
                        c.content = new System.Dynamic.ExpandoObject();
                        c.content.value = dctCol[title];
                        lstcell.Add(c);
                    }
                }
                if (lstTitles.Count > 0)
                {
                    Console.WriteLine("missing titles");
                    for (int i = 0; i < lstTitles.Count; i++)
                    {
                        Console.WriteLine(lstTitles[i]);

                    }
                    Console.ReadLine();
                }
                postObject.attributes.cells = lstcell;
            }
            return postObject;
        }

        public static string updateReaction(string chemicalDrawingId, string cdxml)
        {
            string body = cdxml;
            string endpoint = "/api/rest/v1.0/chemicaldrawings/" + chemicalDrawingId + "/reaction?force=true";
            using (HttpClient client = new HttpClient())
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, destURL + endpoint))
                {
                    Console.Write("PUT\t| " + endpoint + "\t"); request.Headers.Add("x-api-key", apiKey);
                    request.Content = new StringContent(body, null, "chemical/x-cdxml");
                    using (HttpResponseMessage response = client.SendAsync(request).Result)
                    {
                        Console.Write((int)response.StatusCode + "\n");
                        if (response.IsSuccessStatusCode)
                        {
                            return response.Content.ReadAsStringAsync().Result;
                        }
                        else
                        {
                            string errorMessage = response.Content.ReadAsStringAsync().Result;
                            Console.WriteLine("Reaction Update Failed" + errorMessage);
                            return null;
                        }
                    }
                }
            }

        }

        public static string CreateExpinSNB(string expName, Dictionary<string, string> attributes, string notebookID, string templateId)
        {
            string id = string.Empty;

            StringBuilder sb = new StringBuilder();
            foreach (var attr in attributes)
            {
                // Values from attributes are already SanitizeString(...)'d upstream.
                // Only normalize whitespace here – DO NOT re-escape quotes/backslashes.
                var v = (attr.Value ?? string.Empty).Replace("\n\t", "--");

                if (v == "~")
                {
                    sb.Append("\"").Append(attr.Key).Append("\": [] ,");
                }
                else if (v.StartsWith("~"))
                {
                    // "~A, B, C" -> ["A","B","C"]
                    // No extra quote-escaping here to avoid double-escaping
                    sb.Append("\"").Append(attr.Key).Append("\": [\"")
                      .Append(v.Substring(1).Replace(", ", "\", \""))
                      .Append("\"] ,");
                }
                else
                {
                    sb.Append("\"").Append(attr.Key).Append("\": \"")
                      .Append(v) // no extra Replace("\"","\\\"") here
                      .Append("\" ,");
                }
            }
            string strAttributes = sb.Length != 0 ? sb.ToString().Remove(sb.Length - 1) : "";

            // Build outer JSON
            string json = "{\"data\": {\"type\": \"experiment\", \"attributes\":{  ";

            if (!string.IsNullOrEmpty(expName))
            {
                // expName is not part of attributes, so escape here
                string safeName = expName.Replace("\\", "\\\\").Replace("\"", "\\\"");
                json += "\"name\":\"" + safeName + "\",";
            }

            json += strAttributes + "}, \"relationships\": {";

            if (!string.IsNullOrEmpty(notebookID))
            {
                json += "\"ancestors\": {\"data\": [{\"type\": \"journal\",\"id\": \"" + notebookID + "\"}]}, ";
            }

            json += "\"template\": { \"data\": { \"type\": \"experiment\",\"id\": \"" + templateId + "\"}}}}}";

            // POST
            MessageResponse response = PostContentToService(destURL + "/api/rest/v1.0/entities?force=true", json, apiKey);

            if (response.Status == Constants.StatusCompleted)
            {
                JObject returnNotebook = JObject.Parse(response.Data);
                id = returnNotebook["data"]["id"].ToString();
            }
            else
            {
                throw new Exception("Error creating experiment:" + response.Data);
            }

            return id;
        }

        /* internal static string GetReactionID(string expID)
         {
             string v = getExperimentChildren(expID);
             SampleProperties children = System.Text.Json.JsonSerializer.Deserialize<SampleProperties>(v);
             return children.data.Find(obj => obj.attributes.type == "chemicalDrawing")?.id;
         }
 */
        public static string GetReaction(string chemicalDrawingId)
        {
            string endpoint = "/api/rest/v1.0/stoichiometry/" + chemicalDrawingId;
            using (HttpClient client = new HttpClient())
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, destURL + endpoint))
                {
                    Console.Write("GET\t| " + endpoint + "\t");
                    request.Headers.Add("x-api-key", apiKey);

                    using (HttpResponseMessage response = client.SendAsync(request).Result)
                    {
                        Console.Write((int)response.StatusCode + "\n");

                        if (response.IsSuccessStatusCode)
                        {
                            return response.Content.ReadAsStringAsync().Result;

                        }
                        else
                        {
                            string errorMessage = response.Content.ReadAsStringAsync().Result;
                            throw new Exception("Reaction query Failed" + errorMessage);
                        }
                    }
                }
            }
        }

        public static Dictionary<string, string> GetTemplateAttributes(string templateId)
        {
            Dictionary<string, string> dctAttributes = new Dictionary<string, string>();

            MessageResponse response = GetContentFromService(destURL + "/api/rest/v1.0/entities/" + templateId, apiKey);

            if (response.Status == Constants.StatusCompleted)
            {
                JObject returnTemplate = JObject.Parse(response.Data);
                foreach (var child in returnTemplate["data"]["attributes"]["fields"].Children())
                {
                    if (((Newtonsoft.Json.Linq.JProperty)child).Name != "Name")
                    {
                        dctAttributes.Add(((Newtonsoft.Json.Linq.JProperty)child).Name, ((Newtonsoft.Json.Linq.JProperty)child).Value.SelectToken("value").ToString());
                    }
                }
            }
            else
            {
                Log(response.Data, "GetTemplateAttributes");
            }

            return dctAttributes;


        }

        public static string UploadAttachment(string name, string expId, string type, byte[] byteArray, string pageId = null)
        {
            string gAtachmentId;

            try
            {
                string temp = null;
                //name = name.Replace("/", "~").Replace("#", "%23").Replace(" ", "%20");
                if (name.Length > 50)
                {
                    temp = name;
                    name = "test_test";
                }
                Dictionary<string, string> dctHeaders = new Dictionary<string, string>()
                 {
                     { "content-type", GetMimeType(type, ref name) },
                     { "accept", "application/vnd.api+json" }
                 };
                string url = destURL + "/api/rest/v1.0/entities/" + expId + "/children/" + name + "?force=true";
                if (!string.IsNullOrEmpty(pageId))
                {
                    url += "&pageId=" + pageId;
                }
                MessageResponse response = PostStream(url, dctHeaders, byteArray, apiKey);
                if (response.Status == "Failed")
                {
                    //Console.WriteLine("writing to..." + name + "......"+type);
                    //File.WriteAllBytes(@"c:\ELN\toRita" + Path.DirectorySeparatorChar + name, byteArray);
                    string errorMessage = response.Data;
                    Console.WriteLine(response.ToString());
                    throw new Exception(errorMessage);
                }
                else
                {
                    JObject entityJson = JObject.Parse(response.Data);
                    string attachmentId = entityJson["data"]["id"].ToString();
                    gAtachmentId = attachmentId;
                    if (!String.IsNullOrEmpty(temp))
                    {

                        try
                        {
                            SNBAPI.RenameEntity(attachmentId, temp);
                            return attachmentId;

                        }
                        catch
                        {
                            try
                            {
                                SNBAPI.RenameEntity(attachmentId, temp + "_1");
                                return attachmentId;

                            }
                            catch
                            {
                                try
                                {
                                    SNBAPI.RenameEntity(attachmentId, temp + "_2");
                                    return attachmentId;

                                }
                                catch
                                {
                                    try
                                    {
                                        SNBAPI.RenameEntity(attachmentId, temp + "_3");
                                        return attachmentId;

                                    }
                                    catch
                                    {
                                        try
                                        {
                                            SNBAPI.RenameEntity(attachmentId, temp + "_4");
                                            return attachmentId;

                                        }
                                        catch
                                        {
                                            SNBAPI.RenameEntity(attachmentId, temp + "_5");
                                            return attachmentId;

                                        }
                                    }
                                }
                            }
                        }
                    }

                }
            }
            catch (Exception w)
            {
                Console.WriteLine(w.Message);
                Console.WriteLine(w.StackTrace);
                Console.WriteLine("file..." + name + "......" + type);
                //File.WriteAllBytes(@"c:\ELN\toRita" + Path.DirectorySeparatorChar + name, byteArray);
                throw;
            }
            return gAtachmentId;
        }

        public static string UploadAttachment(string name, string expId, string type, string base64)
        {
            if (String.IsNullOrEmpty(base64))
                return "Null";
            byte[] byteArray = System.Convert.FromBase64String(base64);
            return UploadAttachment(name, expId, type, byteArray);
        }

        public static string getExperimentChildren(string experimentId)
        {

            string endpoint = "/api/rest/v1.0/entities/" + experimentId + "/children?page[offset]=0&page[limit]=100";

            using (HttpClient client = new HttpClient())
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, destURL + endpoint))
                {
                    Console.Write("GET\t| " + endpoint + "\t");
                    request.Headers.Add("x-api-key", apiKey);

                    using (HttpResponseMessage response = client.SendAsync(request).Result)
                    {
                        Console.Write((int)response.StatusCode + "\n");

                        if (response.IsSuccessStatusCode)
                        {
                            return response.Content.ReadAsStringAsync().Result;

                        }
                        else
                        {
                            string errorMessage = response.Content.ReadAsStringAsync().Result;
                            Console.WriteLine(errorMessage);
                            throw new Exception(errorMessage);
                        }


                    }

                }
            }
        }

        public static Dictionary<string, string> GetExperimentChildren(string experimentId)
        {
            string endpoint = $"/api/rest/v1.0/entities/{experimentId}/children?page[offset]=0&page[limit]=100";
            Dictionary<string, string> experimentSections = new Dictionary<string, string>();

            using (HttpClient client = new HttpClient())
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, destURL + endpoint))
                {
                    Console.WriteLine($"GET\t| {endpoint}");
                    request.Headers.Add("x-api-key", apiKey);

                    using (HttpResponseMessage response = client.SendAsync(request).Result)
                    {
                        Console.WriteLine($"📡 Response Code: {(int)response.StatusCode}");

                        if (response.IsSuccessStatusCode)
                        {
                            string responseBody = response.Content.ReadAsStringAsync().Result;
                            try
                            {
                                // Parse JSON response
                                JObject jsonObject = JObject.Parse(responseBody);
                                JArray items = (JArray)jsonObject["data"];

                                if (items != null)
                                {
                                    foreach (var item in items)
                                    {
                                        string childID = item["id"]?.ToString();
                                        string childName = item["attributes"]?["name"]?.ToString();
                                        string entityType = item["type"]?.ToString(); // Check if it’s an attachment

                                        if (!string.IsNullOrEmpty(childID) && !string.IsNullOrEmpty(childName))
                                        {
                                            experimentSections[childName] = childID;
                                            Console.WriteLine($"✅ Found Section: {childName} (ID: {childID}, Type: {entityType})");
                                        }

                                        // Log if an attachment is found
                                        if (entityType == "attachment")
                                        {
                                            Console.WriteLine($"📎 Attachment Found: {childName} (ID: {childID})");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Error parsing experiment children: {ex.Message}");
                            }

                            return experimentSections;
                        }
                        else
                        {
                            string errorMessage = response.Content.ReadAsStringAsync().Result;
                            Console.WriteLine($"❌ API Error: {errorMessage}");
                            throw new Exception(errorMessage);
                        }
                    }
                }
            }
        }

        public static string MakeApiRequest(string url, string method, string payload = null)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(new HttpMethod(method), url);

                if (!string.IsNullOrEmpty(payload))
                {
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                }

                var response = client.SendAsync(request).Result;
                return response.Content.ReadAsStringAsync().Result;
            }
        }

        public static string GetPRORZOwnerId(string apiKey, string prorzOwnerEmail)
        {
            string apiUrl = "/api/rest/v1.0/users?enabled=true&page%5Boffset%5D=0&page%5Blimit%5D=500";

            // Use the existing method for GET requests
            MessageResponse response = GetContentFromService(destURL + apiUrl, apiKey);

            if (response.Status == Constants.StatusCompleted)
            {
                JObject data = JObject.Parse(response.Data);

                foreach (var user in data["data"])
                {
                    string userEmail = user["attributes"]["email"].ToString();
                    if (userEmail.Equals(prorzOwnerEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        return user["id"].ToString(); // ✅ Return correct User ID
                    }
                }

                Console.WriteLine($"⚠️ No user found for email: {prorzOwnerEmail}");
            }
            else
            {
                Console.WriteLine($"❌ API Error: {response.Data}");
            }

            return null; // Return null if no user is found
        }

        public static UserDetails GetUserDetails(string userId, string apiKey, string tenantUrl)
        {
            string endpoint = $"{tenantUrl}/api/rest/v1.0/users/{userId}";

            try
            {
                MessageResponse response = SendGetRequest(endpoint, apiKey);

                if (response.Status == Constants.StatusCompleted)
                {
                    JObject jsonResponse = JObject.Parse(response.Data);
                    var user = jsonResponse["data"];

                    if (user != null)
                    {
                        return new UserDetails
                        {
                            userId = user["id"]?.ToString(),
                            email = user["attributes"]?["email"]?.ToString(),
                            firstName = user["attributes"]?["firstName"]?.ToString(),
                            lastName = user["attributes"]?["lastName"]?.ToString()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching user details for {userId}: {ex.Message}");
            }

            return null;
        }




        #region helper methods
        public static void SetupConnectionToSNB(string tenantUrl, string xapikey)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            destURL = tenantUrl;
            apiKey = xapikey;
        }

        public static MessageResponse PostContentToService(string url, string documentJSON, string apiKey)
        {

            MessageResponse messageResponse = new MessageResponse(Constants.StatusCompleted, string.Empty);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    StringContent strContent = new StringContent(documentJSON);
                    strContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
                    client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                    HttpResponseMessage postDocument = client.PostAsync(url, strContent).Result;

                    if (postDocument.StatusCode == HttpStatusCode.OK || postDocument.StatusCode == HttpStatusCode.Created || postDocument.StatusCode == HttpStatusCode.Created || postDocument.StatusCode == HttpStatusCode.Accepted)
                    {
                        messageResponse.Data = postDocument.Content.ReadAsStringAsync().Result;
                        return messageResponse;
                    }
                    else
                    {
                        messageResponse.Status = "Failed";
                        messageResponse.Data = postDocument.Content.ReadAsStringAsync().Result;
                        //Common.LogHelper.AddLineToLog(messageResponse.Data);
                        return messageResponse;
                    }
                }
            }

            catch (TimeoutException timeEx)
            {
                messageResponse.Data = timeEx.ToString();
                messageResponse.Status = "Failed";
                //Common.LogHelper.AddLineToLog("Error: '" + messageResponse.Data);
                Thread.Sleep(1000);
                Console.WriteLine("Timeout occurred retrying...");

                return PostContentToService(url, documentJSON, apiKey);

            }
            catch (Exception ex)
            {
                messageResponse.Data = ex.ToString();
                messageResponse.Status = "Failed";
                //Common.LogHelper.AddLineToLog("Error: '" + messageResponse.Data);
                return messageResponse;
            }
        }

        public static MessageResponse PATCHContentToService(string url, string documentJSON, string apiKey)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            MessageResponse messageResponse = new MessageResponse(Constants.StatusCompleted, string.Empty);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response;
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                    request.Content = new StringContent(documentJSON, System.Text.Encoding.UTF8, "application/vnd.api+json");
                    response = client.SendAsync(request).Result;


                    if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Accepted)
                    {
                        messageResponse.Data = response.Content.ReadAsStringAsync().Result;
                        return messageResponse;
                    }
                    else
                    {
                        messageResponse.Status = "Failed";
                        messageResponse.Data = response.Content.ReadAsStringAsync().Result;
                        //Common.LogHelper.AddLineToLog(messageResponse.Data);
                        return messageResponse;
                    }
                }
            }

            catch (TimeoutException timeEx)
            {
                messageResponse.Data = timeEx.ToString();
                messageResponse.Status = "Failed";
                //Common.LogHelper.AddLineToLog("Error: '" + messageResponse.Data);
                Thread.Sleep(1000);
                Console.WriteLine("Timeout occurred retrying...");

                return PATCHContentToService(url, documentJSON, apiKey);
            }
            catch (Exception ex)
            {
                messageResponse.Data = ex.ToString();
                messageResponse.Status = "Failed";
                //Common.LogHelper.AddLineToLog("Error: '" + messageResponse.Data);
                return messageResponse;
            }
        }

        public static async Task<MessageResponse> SendPatchRequest(string url, string documentJSON, string apiKey)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            MessageResponse messageResponse = new MessageResponse(Constants.StatusFailed, string.Empty);

            if (string.IsNullOrEmpty(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                Console.WriteLine("❌ Error: Invalid or empty URL provided.");
                messageResponse.Data = "Invalid URL";
                return messageResponse;
            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Patch, url);
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                    request.Content = new StringContent(documentJSON, Encoding.UTF8, "application/vnd.api+json");

                    HttpResponseMessage response = await client.SendAsync(request);

                    messageResponse.Data = await response.Content.ReadAsStringAsync();
                    messageResponse.Status = response.IsSuccessStatusCode ? Constants.StatusCompleted : "Failed";

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"✅ Successfully updated data at {url}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ PATCH request failed: {response.StatusCode} - {messageResponse.Data}");
                    }

                    return messageResponse;
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"❌ HTTP Request Error: {httpEx.Message}");
                messageResponse.Data = httpEx.ToString();
            }
            catch (TimeoutException timeEx)
            {
                Console.WriteLine("❌ Timeout occurred while making PATCH request.");
                messageResponse.Data = timeEx.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Unexpected error: {ex.Message}");
                messageResponse.Data = ex.ToString();
            }

            return messageResponse;
        }

        public static string GetExperimentShareId(string expID, string apiKey, string tenantUrl)
        {
            string endpoint = $"{tenantUrl}/api/rest/v1.0/entities/{expID}/shares";

            try
            {
                MessageResponse response = SNBAPI.SendGetRequest(endpoint, apiKey);

                if (response.Status == Constants.StatusCompleted && !string.IsNullOrEmpty(response.Data))
                {
                    JObject jsonResponse = JObject.Parse(response.Data);
                    JToken firstShare = jsonResponse["data"]?.FirstOrDefault();

                    if (firstShare != null && firstShare["id"] != null)
                    {
                        return firstShare["id"].ToString(); // ✅ Extract the first available shareId
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching shareId for experiment {expID}: {ex.Message}");
            }

            return string.Empty; // Return empty string if no valid shareId is found
        }

        public static MessageResponse SendGetRequest(string url, string apiKey)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            MessageResponse messageResponse = new MessageResponse(Constants.StatusCompleted, string.Empty);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

                    HttpResponseMessage response = client.SendAsync(request).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        messageResponse.Data = response.Content.ReadAsStringAsync().Result;
                    }
                    else
                    {
                        messageResponse.Status = "Failed";
                        messageResponse.Data = response.Content.ReadAsStringAsync().Result;
                    }
                }
            }
            catch (Exception ex)
            {
                messageResponse.Status = "Failed";
                messageResponse.Data = ex.ToString();
            }

            return messageResponse;
        }





        private static MessageResponse GetContentFromService(string url, string apiKey)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            MessageResponse messageResponse = new MessageResponse(Constants.StatusCompleted, string.Empty);

            try
            {
                using (HttpClient client = new HttpClient())
                {

                    HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
                    webRequest.Headers.Add("x-api-key", apiKey);
                    webRequest.Accept = "application/vnd.api+json";
                    webRequest.ContentType = "application/vnd.api+json";

                    webRequest.Method = "GET";

                    HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse();


                    if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Accepted)
                    {
                        string result = string.Empty;
                        using (var streamReader = new StreamReader(response.GetResponseStream()))
                        {
                            result = streamReader.ReadToEnd();
                        }
                        messageResponse.Data = result;
                        return messageResponse;
                    }
                    else
                    {
                        messageResponse.Status = "Failed";
                        messageResponse.Data = response.ToString();
                        //Common.LogHelper.AddLineToLog(messageResponse.Data);
                        return messageResponse;
                    }
                }
            }
            catch (TimeoutException timeEx)
            {
                messageResponse.Data = timeEx.ToString();
                messageResponse.Status = "Failed";
                //Common.LogHelper.AddLineToLog("Error: '" + messageResponse.Data);
                Thread.Sleep(1000);
                Console.WriteLine("Timeout occurred retrying...");

                return GetContentFromService(url, apiKey);
            }
            catch (Exception ex)
            {
                messageResponse.Data = ex.ToString();
                messageResponse.Status = "Failed";
                //  Common.LogHelper.AddLineToLog("Error: '" + messageResponse.Data);
                throw ex;
            }
        }

        public static string closeExperimentWithoutSigning(string experimentId, string reason)
        {
            string body = "{\"data\":{\"attributes\":{\"reason\":\"" + reason + "\"}}}";
            string endpoint = "/api/rest/v1.0/entities/" + experimentId + "/reviews/close";
            try
            {
                using (HttpClient client = new HttpClient())
                {

                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, destURL + endpoint))
                    {
                        Console.Write("POST\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);

                        request.Content = new StringContent(body, null, "application/vnd.api+json");


                        using (HttpResponseMessage response = client.SendAsync(request).Result)
                        {
                            Console.Write((int)response.StatusCode + "\n");

                            if (response.IsSuccessStatusCode)
                            {
                                return response.Content.ReadAsStringAsync().Result;

                            }
                            else
                            {
                                string errorMessage = response.Content.ReadAsStringAsync().Result;
                                Console.WriteLine("Experiment Closing Failed" + errorMessage);
                                throw new Exception(errorMessage);
                            }


                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Experiment Closing Failed\nCritical\n" + ex.Message);
                throw ex;
            }
        }

        private static void cleanup(Dictionary<string, string> data)
        {
            bool removing = true;
            while (removing)
            {
                removing = false;
                foreach (string key in data.Keys)
                {
                    if (data[key] == null || String.IsNullOrWhiteSpace(data[key]))
                    {
                        data.Remove(key);
                        // Console.WriteLine(key + " removed");
                        removing = true;
                        break;
                    }
                    else
                    {
                        // Console.WriteLine(key + "||" + data[key] + "|");
                    }
                }
            }
        }

/*        public static string updateStoichCols(string chemicalDrawingId, string target, List<string> columns)
        {
            string endpoint = "/api/rest/v1.0/stoichiometry/" + chemicalDrawingId + "/columns/" + target + "?force=true";
            try
            {

                dynamic baseObj = new System.Dynamic.ExpandoObject();
                baseObj.data = new System.Dynamic.ExpandoObject();
                baseObj.data.attributes = new System.Dynamic.ExpandoObject();
                List<System.Dynamic.ExpandoObject> tempList = new List<System.Dynamic.ExpandoObject>();
                foreach (string col in columns)
                {
                    dynamic tempObj = new System.Dynamic.ExpandoObject();
                    tempObj.title = col;
                    tempList.Add(tempObj);
                }
                ((IDictionary<String, Object>)baseObj.data.attributes).Add(target, tempList);

                using (HttpClient client = new HttpClient())
                {

                    using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PATCH"), destURL + endpoint))
                    {
                        Console.Write("PATCH\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);
                        request.Content = new StringContent(JsonSerializer.Serialize(baseObj), null, "application/vnd.api+json");
                        using (HttpResponseMessage response = client.SendAsync(request).Result)
                        {
                            Console.Write((int)response.StatusCode + "\n");

                            if (response.IsSuccessStatusCode)
                            {
                                return response.Content.ReadAsStringAsync().Result;
                            }
                            else
                            {
                                string errorMessage = response.Content.ReadAsStringAsync().Result;
                                Console.WriteLine("Updating Stoich Columns Failed" + errorMessage);
                                throw new Exception("Updating Stoich Columns Failed" + errorMessage); ;
                            }


                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Updating Stoich Columns Failed\nCritical\n" + ex.Message);
                throw ex;
            }
        }
*/
        private static MessageResponse PostStream(string url, Dictionary<string, string> headers, byte[] body, string apiKey)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            MessageResponse messageResponse = new MessageResponse(Constants.StatusCompleted, string.Empty);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    using (var content = new ByteArrayContent(body))
                    {
                        foreach (string headerName in headers.Keys)
                        {
                            switch (headerName)
                            {
                                case "content-type":
                                    content.Headers.ContentType = new MediaTypeHeaderValue(headers[headerName]);
                                    break;
                                case "accept":
                                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(headers[headerName]));
                                    break;

                                default:
                                    break;
                            }
                        }
                        client.Timeout = TimeSpan.FromSeconds(300);
                        HttpResponseMessage postDocument = client.PostAsync(url, content).Result;


                        if (postDocument.StatusCode == HttpStatusCode.OK || postDocument.StatusCode == HttpStatusCode.Created || postDocument.StatusCode == HttpStatusCode.Created || postDocument.StatusCode == HttpStatusCode.Accepted)
                        {
                            messageResponse.Data = postDocument.Content.ReadAsStringAsync().Result;
                            return messageResponse;
                        }
                        else
                        {
                            //Common.LogHelper.AddLineToLog(postDocument.ToString());
                            //Common.LogHelper.AddLineToLog(messageResponse.Data);
                            throw new Exception(messageResponse.Data);
                        }
                    }
                }
            }
            catch (TimeoutException timeEx)
            {
                messageResponse.Data = timeEx.ToString();
                messageResponse.Status = "Failed";
                //Common.LogHelper.AddLineToLog("Error: '" + messageResponse.Data);
                Thread.Sleep(1000);
                Console.WriteLine("Timeout occurred retrying...");

                return PostStream(url, headers, body, apiKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine(messageResponse.Data);
                messageResponse.Data = ex.ToString();
                messageResponse.Status = "Failed";
                throw new Exception("Error: '" + messageResponse.Data);
            }
        }

        private static void Log(string text, string functionName)
        {
            if (String.IsNullOrEmpty(functionName))
            {
                //Common.LogHelper.Add(text, logsFolders, true);
            }
            else
            {
                //Common.LogHelper.Add("Error on function: " + functionName, logsFolders, true);
                //Common.LogHelper.Add(text, logsFolders, true);
            }
        }

        private static string GetMimeType(string extension, ref string name)
        {
            extension = extension.Replace("..", ".");
            switch (extension.ToLower())
            {
                case ".gif":
                    return "image/gif";
                case ".png":
                    return "image/png";
                case ".r":
                    return "text/plain";
                case ".gb":
                    return "application/genbank";
                case ".fa":
                    return "application/fasta";
                case ".support":
                    return "application/octet-stream";
                case ".geneious":
                    return "application/octet-stream";
                case ".fasta":
                    return "application/fasta";
                case ".svg":
                    return "image/svg+xml";
                case ".tif":
                case ".tiff":
                    return "image/tiff";
                case ".emf":
                    return "application/emf";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".cdxml":
                    return "chemical/x-cdxml";
                case ".rxn":
                    return "chemical/x-mdl-rxnfile";
                case ".cdx":
                    return "chemical/x-cdx";
                case ".pdf":
                    return "application/pdf";
                case ".xlsx":
                    return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".xlsm":
                    return "application/vnd.ms-excel.sheet.macroEnabled.12";
                case ".doc":
                    return "application/msword";
                case ".docx":
                    return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case ".xls":
                    return "application/vnd.ms-excel";
                case ".csv":
                    return "text/csv";
                case ".pptx":
                    return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                case ".ppt":
                    return "application/vnd.ms-powerpoint";

                case ".html":
                case ".htm":
                    return "text/html";
                case ".bmp":
                    return "image/bmp";
                case ".rtf":
                case ".txt":
                    return "text/plain";
                case ".mts":
                    return "video/MP2T";
                case ".eml":
                    return "message/rfc822";
                case ".clc":
                    return "application/x-clc";
                case ".eds":
                    return "application/x-eds";
                case ".vsdx":
                    return "application/vnd.ms-visio.drawing";
                case ".pcrd":
                    return "application/octet-stream";
                case ".gbk":
                case ".mnova":
                case ".msg":
                case ".dts":
                case ".xps":
                case ".mov":
                case ".dna":
                case ".zip":
                case ".ble":
                case ".imp":
                case ".ltv":
                case ".xlt":
                case ".zdp":
                    return "application/octet-stream";
                ////////////////////
                case ".fastq": return "application/fastq";
                case ".fq": return "application/fastq"; // short form
                case ".fastq.gz":
                case ".fq.gz": return "application/gzip";  // compressed FASTQ
                case ".fastq.zip": return "application/zip"; // zipped FASTQ

                case ".fa.gz":
                case ".fasta.gz": return "application/gzip"; // compressed FASTA

                case ".tar.gz":
                case ".tgz": return "application/gzip"; // tarball (outer type)

                case ".gff3": return "text/x-gff3";
                case ".bed": return "text/x-bed";

                case ".prism": return "application/octet-stream"; // GraphPad Prism legacy
                case ".pzfx": return "application/xml";          // GraphPad Prism XML

                case ".czi": return "image/x-czi";              // Zeiss microscope image
                case ".heic": return "image/heic";

                case ".gz": return "application/gzip";
                case ".7z": return "application/x-7z-compressed";

                case ".json": return "application/json";
                case ".pptm": return "application/vnd.ms-powerpoint.presentation.macroEnabled.12";
                case ".ps": return "application/postscript";

                case ".docm": return "application/vnd.ms-word.document.macroEnabled.12";
                case ".xlsb": return "application/vnd.ms-excel.sheet.binary.macroEnabled.12";

                case ".sgn": return "application/octet-stream";

                case ".mp4":
                    return "video/mp4";
                case ".slk":
                    return "application/vnd.ms-excel";
                case ".jfif":
                    return "image/jpeg";



                default:
                    Console.WriteLine("Extension missed " + extension);
                    //Console.ReadLine();
                    if (!name.EndsWith(extension))
                        name = name + extension;
                    Log("WARNING ________________________________Extension missed" + extension, "GetMimeType");
                    return "application/octet-stream";
            }
        }
        public static void UpdateExpinSNB(string expID, Dictionary<string, string> attributes)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var attr in attributes)
            {
                sb.Append("\"" + attr.Key + "\": \"" + attr.Value.Replace("\n\t", "--").Replace("\"", "\\\"") + "\",");
            }
            string strAttributes = sb.Length != 0 ? sb.ToString().Remove(sb.ToString().Length - 1) : "";

            string json = "{\"data\": {\"type\": \"experiment\", \"id\": \"" + expID + "\", \"attributes\":{ ";
            json += strAttributes + "}}}";

            string endpoint = "/api/rest/v1.0/entities/" + expID + "?force=true";

            try
            {
                MessageResponse response = PATCHContentToService(destURL + endpoint, json, apiKey);
                if (response.Status != Constants.StatusCompleted)
                {
                    throw new Exception("Error updating experiment:" + response.Data);
                }
                else
                {
                    Console.WriteLine($"Experiment with ID {expID} updated successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while updating experiment {expID}: {ex.Message}");
                throw;
            }
        }

        public static string GetExperimentByName(string experimentName)
        {
            string url = $"{destURL}/api/v1.0/search/combined?source=connected&enrichUsers=true";
            string requestBody = $"{{\"query\": {{ \"name\": \"{experimentName}\" }} }}";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = client.PostAsync(url, content).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string responseData = response.Content.ReadAsStringAsync().Result;
                        JObject jsonResponse = JObject.Parse(responseData);

                        // Navigate to the "meta-highlight-name" field
                        var highlightName = jsonResponse.SelectToken("data[0].meta.highlight.name");

                        // Check if highlight name exists and return it
                        if (highlightName != null)
                        {
                            return highlightName.ToString();
                        }
                        else
                        {
                            Console.WriteLine("Highlight name not found in response.");
                            return null;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error in API request: " + response.ReasonPhrase);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in GetExperimentByName: " + ex.Message);
                return null;
            }
        }

        /*        private static string addToStoich(string chemicalDrawingId, string table, string structure, string dataType = "cdxml")
                {

                    string endpoint = "/api/rest/v1.0/chemicaldrawings/" + chemicalDrawingId + "/reaction/" + table + "?force=true";
                    try
                    {
                        dynamic baseObj = new System.Dynamic.ExpandoObject();
                        baseObj.data = new System.Dynamic.ExpandoObject();
                        baseObj.data.attributes = new System.Dynamic.ExpandoObject();
                        baseObj.data.attributes.dataType = dataType; baseObj.data.attributes.data = structure;
                        using (HttpClient client = new HttpClient())
                        {
                            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, destURL + endpoint))
                            {
                                Console.Write("POST\t| " + endpoint + "\t");
                                request.Headers.Add("x-api-key", apiKey);
                                request.Content = new StringContent(JsonSerializer.Serialize(baseObj), null, "application/vnd.api+json");
                                using (HttpResponseMessage response = client.SendAsync(request).Result)
                                {
                                    Console.Write((int)response.StatusCode + "\n"); if (response.IsSuccessStatusCode)
                                    {
                                        return response.Content.ReadAsStringAsync().Result;
                                    }
                                    else
                                    {
                                        string errorMessage = response.Content.ReadAsStringAsync().Result;
                                        Console.WriteLine("Add to Stoich Table Failed" + errorMessage);
                                        throw new Exception(errorMessage);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Add to Stoich Table Failed\nCritical\n" + ex.Message);
                        throw ex;
                    }
                }
        */
        static void LogSkippedAttachment(string logPath, string fileName, string reason)
        {
            string logLine = $"{fileName}\t{reason}";
            File.AppendAllText(logPath, logLine + Environment.NewLine);
        }


        public static void ReplaceAttachment(string entityID, byte[] fileBytes, string contentType)
        {
            string url = $"{destURL}/api/rest/v1.0/entities/{entityID}/attachment?force=true"; // Add force=true to avoid digest requirement
            string localPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string skippedLogPath = Path.Combine(localPath, "SkippedAttachments.txt");
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("x-api-key", apiKey);

                    using (var content = new ByteArrayContent(fileBytes))
                    {
                        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                        HttpResponseMessage response = client.PutAsync(url, content).Result;

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorMessage = response.Content.ReadAsStringAsync().Result;
                            LogSkippedAttachment(skippedLogPath, entityID, $"API Error - {errorMessage}");
                            throw new Exception($"Error replacing attachment: {errorMessage}");
                        }
                        else
                        {
                            Console.WriteLine($"✅ Successfully replaced attachment for entity {entityID}");
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error while replacing attachment for entity {entityID}: {ex.Message}");
                throw;
            }
        }
        public static async Task<List<string>> GetAttributeOptionsAsync(string tenantUrl, string apiKey, int attributeId)
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri(tenantUrl);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

            string url = $"/api/rest/v1.0/attributes/attribute:{attributeId}?statistics=false";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Status code: {response.StatusCode}");
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            dynamic json = JsonConvert.DeserializeObject(jsonString);

            var options = new List<string>();

            try
            {
                foreach (var opt in json.data.attributes.options)
                {
                    options.Add((string)opt.ToString());
                }
            }
            catch (Exception)
            {
                throw new Exception($"Could not parse options for attribute:{attributeId}. Raw response: {jsonString}");
            }

            return options;
        }

        public static async Task ReplaceAttachmentChunkedAsync(
    string entityId,
    byte[] content,
    string mediaType = "text/html",
    int chunkBytes = 300_000)
        {
            // If it's small, use your current raw writer for speed.
            if (content.Length <= 500_000)
            {
                await ReplaceAttachmentRawAsync(entityId, content, mediaType);
                return;
            }

            // Stream the source in chunks (transport-level split; final stored value is one big HTML)
            var url = $"{destURL}/api/rest/v1.0/entities/{entityId}/content?force=true";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

            long offset = 0;
            while (offset < content.LongLength)
            {
                int take = (int)Math.Min(chunkBytes, content.LongLength - offset);
                var slice = new ArraySegment<byte>(content, (int)offset, take).ToArray();

                using var req = new HttpRequestMessage(HttpMethod.Patch, url);
                req.Headers.TryAddWithoutValidation("X-Append", "true");                   // hint for append semantics
                req.Content = new ByteArrayContent(slice);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                req.Content.Headers.TryAddWithoutValidation("Content-Range",
                    $"bytes {offset}-{offset + take - 1}/{content.Length}");

                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    throw new Exception($"Chunk upload failed at {offset} ({(int)resp.StatusCode}). {body}");
                }

                offset += take;
            }

            // Optional finalize/commit no-op — ignore failure if tenant doesn't support it
            using var finalize = new HttpRequestMessage(HttpMethod.Post, url);
            finalize.Headers.TryAddWithoutValidation("X-Commit", "true");
            try { await client.SendAsync(finalize); } catch { /* ignore */ }
        }

        // Small writer that mirrors your existing ReplaceAttachment behavior
        private static async Task ReplaceAttachmentRawAsync(string entityId, byte[] content, string mediaType)
        {
            var url = $"{destURL}/api/rest/v1.0/entities/{entityId}/content?force=true";
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
            req.Content = new ByteArrayContent(content);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"ReplaceAttachmentRaw failed: {(int)resp.StatusCode} {body}");
            }
        }



        public class AttributeResponse
        {
            public AttributeData data { get; set; }
        }

        public class AttributeData
        {
            public AttributeAttributes attributes { get; set; }
        }

        public class AttributeAttributes
        {
            public List<string> options { get; set; }
        }



        public class UserDetails
        {
            public string userId { get; set; }
            public string email { get; set; }
            public string firstName { get; set; }
            public string lastName { get; set; }
        }


        #endregion
    }
}
