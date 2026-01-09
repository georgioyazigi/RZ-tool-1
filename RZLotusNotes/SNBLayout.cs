using Common.Models;

namespace spr_live_migration
{
    public class SNBLayout
    {
        public static bool moveEntity(string tenantUrl, string apiKey, string expId, string entityName, string targetPageName, int index = -1)
        {
            try
            {
                string layout = getLayout(tenantUrl, apiKey, expId);
                if (layout == null)
                {
                    throw new Exception("Moving " + entityName + " to " + targetPageName + " failed.\nFailed to get Experiment Layout.");
                }
                ExperimentLayout jresp = System.Text.Json.JsonSerializer.Deserialize<ExperimentLayout>(layout);
                string entityId = null;
                string pageId = null;


                Common.Models.Data thePage = jresp.data.Find(obj => obj.attributes.name == targetPageName);
                if (thePage != null)
                {
                    pageId = thePage.attributes.id;
                }
                Entity theEntity;
                foreach (Common.Models.Data page in jresp.data)
                {
                    theEntity = page.attributes.entities.Find(obj => obj.entityName == entityName);
                    if (theEntity != null)
                    {
                        entityId = theEntity.entityEid;
                        break;
                    }
                }


                if (entityId == null)
                {
                    throw new Exception("Moving " + entityName + " to " + targetPageName + " failed.\nEntity " + entityName + " was not Found.");
                }

                if (pageId == null)
                {
                    string createResp = createPage(tenantUrl, apiKey, expId, targetPageName);
                    if (createResp == null)
                    {
                        throw new Exception("Moving " + entityName + " to " + targetPageName + " failed.\nPage " + targetPageName + " was not found and failed to create.");
                    }
                    CreatePageResp jresultCreation = System.Text.Json.JsonSerializer.Deserialize<CreatePageResp>(createResp);
                    pageId = jresultCreation.data.id;
                    index = -1;

                }
                string finalResponse = updateEntityLocation(tenantUrl, apiKey, entityId, pageId, index);
                if (finalResponse == null)
                {
                    throw new Exception("Moving " + entityName + " to " + targetPageName + " failed.\nFailed to update the entity Location");

                }
                else if (finalResponse == "500")
                {
                    Console.WriteLine("500 Error Fix applying.....");
                    // Program.fixLayout500Issue(expId);
                    finalResponse = updateEntityLocation(tenantUrl, apiKey, entityId, pageId, index);
                    if (finalResponse == null)
                    {
                        throw new Exception("Moving " + entityName + " to " + targetPageName + " failed.\nFailed to update the entity Location");

                    }

                }
                Console.WriteLine("Moving " + entityName + " to " + targetPageName + " Done.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }

        public static string getLayout(string tenantUrl, string apiKey, string expId)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {

                    string endpoint = "/api/rest/v1.0/entities/" + expId + "/layout/pages/";

                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, tenantUrl + endpoint))
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
                                Console.WriteLine("Getting layout pages Failed : " + errorMessage);
                                return null;
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Getting layout pages Failed\nCritical\n" + ex.Message);
                return null;
            }
        }

        public static string createPage(string tenantUrl, string apiKey, string expId, string pageName = "New Page", int index = -1)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string endpoint = "/api/rest/v1.0/entities/" + expId + "/layout/pages" + "?force=true";
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tenantUrl + endpoint))
                    {
                        Console.Write("POST\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);
                        var content = new StringContent($@"{{
                                    ""data"": {{
                                        ""type"": ""layoutPage"",
                                        ""attributes"": {{
                                            ""name"": ""{pageName}"",
                                            ""index"": {index.ToString()}
                                        }}
                                    }}
                                    }}", null, "application/vnd.api+json");


                        request.Content = content;
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
                                Console.WriteLine("Create layout pages Failed : " + errorMessage);
                                if ((int)response.StatusCode != 428)
                                {
                                    return null;
                                }
                                Thread.Sleep(1000);
                                Console.WriteLine("Waiting for 1 second and retrying");
                                return createPage(tenantUrl, apiKey, expId, pageName, index);
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Creation of page failed \nCritical\n" + ex.Message);
                return null;
            }
        }

        public static string getPageLayout(string tenantUrl, string apiKey, string expId, string pageId)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string endpoint = "/api/rest/v1.0/entities/" + expId + "/layout/pages/" + pageId;
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, tenantUrl + endpoint))
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
                                Console.WriteLine("Getting one layout pages Failed : " + errorMessage);
                                return null;
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Getting one layout pages Failed\nCritical\n" + ex.Message);
                return null;
            }
        }

        public static string updatePageLayout(string tenantUrl, string apiKey, string expId, string pageId, string pageName = "New Page", int index = -1)
        {

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string endpoint = "/api/rest/v1.0/entities/" + expId + "/layout/pages/" + pageId + "?force=true";
                    using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PATCH"), tenantUrl + endpoint))
                    {
                        Console.Write("PATCH\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);
                        var content = new StringContent($@"{{
                                    ""data"": {{
                                        ""type"": ""layoutPage"",
                                        ""attributes"": {{
                                            ""name"": ""{pageName}"",
                                            ""index"": {index.ToString()}
                                        }}
                                    }}
                                    }}", null, "application/vnd.api+json");


                        request.Content = content;
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
                                Console.WriteLine("Update layout pages Failed : " + errorMessage);
                                if ((int)response.StatusCode != 428)
                                {
                                    return null;
                                }
                                Thread.Sleep(1000);
                                Console.WriteLine("Waiting for 1 second and retrying");
                                return updatePageLayout(tenantUrl, apiKey, expId, pageId, pageName, index);
                            }


                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Update layout pages Failed\nCritical\n" + ex.Message);
                return null;
            }


        }

        public static string deletePage(string tenantUrl, string apiKey, string expId, string pageId)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string endpoint = "/api/rest/v1.0/entities/" + expId + "/layout/pages/" + pageId + "?force=true";
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, tenantUrl + endpoint))
                    {
                        Console.Write("DELETE\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);
                        using (HttpResponseMessage response = client.SendAsync(request).Result)
                        {
                            Console.Write((int)response.StatusCode + "\n");
                            if (response.IsSuccessStatusCode)
                            {
                                // Here because it's a delete request, we're not expecting any content 
                                return response.Content.ReadAsStringAsync().Result;

                            }
                            else
                            {

                                string errorMessage = response.Content.ReadAsStringAsync().Result;
                                Console.WriteLine("Update layout pages Failed : " + errorMessage);
                                if ((int)response.StatusCode != 428)
                                {
                                    return null;
                                }
                                Thread.Sleep(1000);
                                Console.WriteLine("Waiting for 1 second and retrying");
                                return deletePage(tenantUrl, apiKey, expId, pageId);
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Delete Layout Pages Failed\nCritical\n" + ex.Message);
                return null;
            }
        }

        public static string getEntityLocation(string tenantUrl, string apiKey, string entityId)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string endpoint = "/api/rest/v1.0/entities/" + entityId + "/layout/location";
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, tenantUrl + endpoint))
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
                                Console.WriteLine("Getting Entity Location Failed : " + errorMessage);
                                return null;
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Getting entity location Failed\nCritical\n" + ex.Message);
                return null;
            }
        }

        public static string updateEntityLocation(string tenantUrl, string apiKey, string entityId, string targetPageId, int index = -1)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string endpoint = "/api/rest/v1.0/entities/" + entityId + "/layout/location?force=true";
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, tenantUrl + endpoint))
                    {
                        Console.Write("PUT\t| " + endpoint + "\t");
                        request.Headers.Add("x-api-key", apiKey);
                        var content = new StringContent($@"{{
                                    ""data"": {{
                                        ""type"": ""layoutLocation"",
                                        ""attributes"": {{
                                            ""pageId"": ""{targetPageId}"",
                                            ""index"": {index.ToString()}
                                        }}
                                    }}
                                    }}", null, "application/vnd.api+json");
                        request.Content = content;
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
                                Console.WriteLine("updateEntityLocation layout pages Failed : " + errorMessage);

                                if ((int)response.StatusCode == 500)
                                {
                                    return "500";
                                }

                                if ((int)response.StatusCode != 428)
                                {
                                    return null;
                                }


                                Console.WriteLine("Waiting for 1 second and retrying");
                                Thread.Sleep(1000);
                                return updateEntityLocation(tenantUrl, apiKey, entityId, targetPageId, index);
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Update Entity Location Failed\nCritical\n" + ex.Message);
                return null;
            }
        }
    }
}
