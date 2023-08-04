using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using GSANotifications.Models;
using GSANotifications.RuleHandlers;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GSANotifications
{
    public class TimerFunctions
    {
        [FunctionName("updateFacilitiesSpecialistsCache")]
        public static void updateFacilitiesSpecialistsCache([TimerTrigger("0 0 0 * * *")] TimerInfo myTimer, 
                ILogger log,             
                [CosmosDB(databaseName:"gsanotifications-cosmosdb", "SpecialistFacilityCache",  Connection = "CosmosdbConnectionString")]IAsyncCollector<dynamic> documentsOut)
        {  
            HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://bapcertificationforms.org/notification/facilityspecialist");
            httpRequestMessage.Headers.Add("APIKey", "QkFQX3Nlb2Fqa3Rjenhqa2xtc2VmZHBnYmhqcGFoaWJmbWpycmxtbHV4ZHlnZm5ibnN4a2ZqY2xsZmtqZm16Yg==");
            using (var httpClient = new HttpClient())
            {
                var httpResponse = httpClient.SendAsync(httpRequestMessage);

                if (httpResponse.IsCompletedSuccessfully)
                {
                    var content = httpResponse.Result.Content.ReadAsStringAsync().Result;
                    try
                    {
                        var n = new {
                        id = "1",
                        mapping = content  
                        };
                        Task taskSaveSingleItem = documentsOut.AddAsync(n);
                        taskSaveSingleItem.Wait();
                        log.LogInformation("saved specialist facility mapping ", content);
                    }
                    catch (System.Exception ex)
                    {
                        log.LogWarning("trouble saving facility specialist map json document", ex);
                    }
                } 
                else
                {
                    log.LogError("Unable to update mapping of specialists to facilities");
                }  
            }
        }        
            
        [FunctionName("updateNotifications")]
        public static async Task updateNotifications([TimerTrigger("0 */15 * * * *")] TimerInfo myTimer, 
                ILogger logger)        
        {
            var connectionString = Environment.GetEnvironmentVariable("CosmosdbConnectionString");
            try
            {
                //Create gsanotifications-cosmosdb if doesn't exist
                CosmosClient cosmosClient = new CosmosClient(connectionString, 
                new CosmosClientOptions()
                {
                    ApplicationRegion = Regions.EastUS,
                });

                Database cosmosdb = await cosmosClient.CreateDatabaseIfNotExistsAsync(id: "gsanotifications-cosmosdb");
                Container notificationContainer = cosmosClient.GetContainer(databaseId: "gsanotifications-cosmosdb", containerId: "Notifications");
                var query = new QueryDefinition("select * from Items");
                using FeedIterator<NotificationDocument> feed = notificationContainer.GetItemQueryIterator<NotificationDocument>(query);
                List<NotificationDocument> notificationDocuments = new();
                while (feed.HasMoreResults)
                {   
                    FeedResponse<NotificationDocument> response = await feed.ReadNextAsync();
                    if (response.StatusCode == HttpStatusCode.OK && response.Count > 0)
                    {
                        foreach (NotificationDocument item in response)
                        {
                            notificationDocuments.Add(item);
                        }
                    }
                }

                #region Handle accounts that might have become inactive            
                Container facilityspecialistContainer = cosmosClient.GetContainer(databaseId: "gsanotifications-cosmosdb", containerId: "SpecialistFacilityCache");
                FacilityItem facilityItem = await facilityspecialistContainer.ReadItemAsync<FacilityItem>(id: "1",new PartitionKey("1"));
                var facilityOwners = JsonConvert.DeserializeObject<List<FacilityOwner>>(facilityItem.mapping);
                // Deactivate notifications whose facility is not "active"
                if (notificationDocuments.Count > 0 && facilityOwners != null && facilityOwners.Count > 0)
                {
                    foreach (var item in notificationDocuments)
                    {
                        var activeFacility = facilityOwners.First(x => x.FacilityAccountId == item.RegardingAccountId);
                        item.HandlerName = activeFacility.SpecialistName;
                    }
                }
                #endregion

                // Process rules

                var inProcess_AssignedNotAccepted = notificationDocuments.FindAll(x => x.RuleNumber == RuleHandlers.Rules.AssignedNotAccepted); 
                var newNotificationDocuments = await AssignedNotAccepted.Update(notificationContainer, logger, inProcess_AssignedNotAccepted);

                // Add new notifications
                if (newNotificationDocuments is not null && newNotificationDocuments.Count > 0)
                {
                    foreach(var item in newNotificationDocuments)
                    {
                        await notificationContainer.CreateItemAsync<NotificationDocument>(item, new PartitionKey(item.id));
                    }
                }
            }
            catch(Exception ex)
            {
                logger.LogCritical(ex.Message);
            }
        }
    }
}