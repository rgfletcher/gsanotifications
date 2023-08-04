using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Configuration;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using GSANotifications.Models;
using System;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using GSANotifications.RuleHandlers;
using Microsoft.Azure.Cosmos.Linq;

namespace GSANotifications
{
    public class Notification
    {
        private readonly ILogger<List<Notification>> _logger;

        public Notification(ILogger<List<Notification>> log)
        {
            _logger = log;
        }

        [FunctionName("listItems")]
        [OpenApiOperation(operationId: "ListItems", tags: new[] { "list-items" })]
        [OpenApiParameter(name: "searchterm", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "The **searchterm** parameter")]
        [OpenApiParameter(name: "id", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The **id** of Target or Handler parameter")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string), Description = "The OK response")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "No notifications found")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid searchterm")]
        public static async Task<IActionResult> ListItems(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            [CosmosDB(databaseName:"gsanotifications-cosmosdb", "Notifications",  Connection = "CosmosdbConnectionString")]CosmosClient cosmosClient, 
            ILogger logger)
        {
            logger.LogInformation("C# HTTP trigger function list items.");

            var searchterm = req.Query["searchterm"].ToString();
            var id = req.Query["id"].ToString();
            if (string.IsNullOrWhiteSpace(searchterm))
            {
                return (ActionResult)new BadRequestResult();
            }
            else if ((searchterm == "cbAdmin" || searchterm == "certSpecialist") && string.IsNullOrWhiteSpace(id))
            {
                return (ActionResult)new BadRequestResult();
            }
            else if ((searchterm != "cbAdmin" && searchterm != "certSpecialist" && searchterm != "cbSpecialist"))
            {
                return (ActionResult)new BadRequestResult();
            }

            Container container = cosmosClient.GetDatabase("gsanotifications-cosmosdb").GetContainer("Notifications");

            logger.LogInformation($"Searching for: {searchterm}");

            QueryDefinition queryDefinition = null;
            
            if (searchterm == "cbAdmin")
            {
                queryDefinition = new QueryDefinition(
                "SELECT * FROM items i WHERE i.TargetAccountId = @id")
                .WithParameter("@id", id);
            }
            else if (searchterm == "certSpecialist")
            {
                queryDefinition = new QueryDefinition(
                    "SELECT * From items i WHERE i.HandlerContactId = @id")
                    .WithParameter("@id", id);
            }
            else if (searchterm == "cbSpecialist")
            {
                queryDefinition = new QueryDefinition(
                    "SELECT * FROM items");
            }
            else
            {
                return (ActionResult)new BadRequestResult();
            }
            List<NotificationDocument> items = new List<NotificationDocument>();
            using (FeedIterator<NotificationDocument> resultSet = container.GetItemQueryIterator<NotificationDocument>(queryDefinition))
            {
                while (resultSet.HasMoreResults)
                {
                    FeedResponse<NotificationDocument> response = await resultSet.ReadNextAsync();
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        items = response.ToList<NotificationDocument>();
                    }                    
                    if (items.Count > 0)
                    {
                        foreach(var item in items)
                        {
                            logger.LogInformation(item.TargetAccountName + " " + item.AuditName + " " + item.RegardingAccountName);
                        }
                    }
                }
            }
            var data = string.Empty;
            if (items.Count > 0)
            {
                data = JsonConvert.SerializeObject(items, Formatting.Indented);
                return new OkObjectResult(data);
            }
            else
            {
                return new NoContentResult();
            }            
        }

        [FunctionName("change")]
        [OpenApiOperation(operationId: "Change", tags: new[] { "change-request" })]
        [OpenApiRequestBody(contentType: "application/json", bodyType:typeof(NotificationChangeRequest), Description = "The requested change")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Description = "The OK response")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid change request")]
        public static async Task<HttpStatusCode> Change(
            [HttpTrigger(AuthorizationLevel.Anonymous,  "post", Route = null)]HttpRequest req,
            [CosmosDB(databaseName:"gsanotifications-cosmosdb", "Notifications",  Connection = "CosmosdbConnectionString")]CosmosClient cosmosClient, 
            ILogger logger)
        {
            HttpStatusCode statusCode = HttpStatusCode.OK;
            logger.LogInformation("C# HTTP trigger change request.");
            Container cosmosContainer = cosmosClient.GetContainer("gsanotifications-cosmosdb", "Notifications");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            NotificationChangeRequest changeRequest = JsonConvert.DeserializeObject<NotificationChangeRequest>(requestBody); 

            switch (changeRequest.Action)
            {
                case NotificationChangeRequest.revokeAuditAssignment:
                    {
                        // Update notification with action taken and user contact id
                        statusCode = await ActionTaken(changeRequest, "CB Specialist revoked.", cosmosContainer, logger, false, true);
                        break;
                    }
                case NotificationChangeRequest.acceptedAuditAssignment:
                    {
                        statusCode = await ActionTaken(changeRequest, "User accepted audit.", cosmosContainer, logger, true);
                        break;
                    }
                case NotificationChangeRequest.declinedAuditAssignment:
                    {
                        statusCode = await ActionTaken(changeRequest, "User declined audit.", cosmosContainer, logger, true);
                        break;
                    }
                case NotificationChangeRequest.acknowledged:
                    {
                        statusCode = await ActionTaken(changeRequest, "User acknowledged action.", cosmosContainer, logger, true);
                        break;
                    }
                default:
                    break;
            }

            return statusCode;
        }

        private static async Task<HttpStatusCode> ActionTaken(NotificationChangeRequest request, string actionDescription, Container cosmosContainer, 
                                        ILogger logger, bool deactivate = false, bool requiresAck = false)
        {
            HttpStatusCode statusCode = HttpStatusCode.OK;
            if (!string.IsNullOrEmpty(actionDescription))
            {
                try
                {
                    NotificationDocument notificationDocument = null;
                    var items = cosmosContainer.GetItemLinqQueryable<NotificationDocument>(true)
                        .Where(m => m.id == request.NotificationId) // <-- the clause is still here!
                        .ToFeedIterator();

                    while (items.HasMoreResults)
                    {
                        var item = items.ReadNextAsync().Result;
                        notificationDocument = item.First();
                    }

                    if (notificationDocument != null)
                    {
                        if (request.Action == NotificationChangeRequest.acknowledged)
                        {
                            notificationDocument.Acknowledged = true;
                            notificationDocument.DateAcknowledged = DateTime.UtcNow;
                            statusCode = await notificationDocument.Deactivate(cosmosContainer, logger, Rules.Acknowledged);
                        }
                        else
                        {

                            if (deactivate)
                            {
                                notificationDocument.ActionTaken = actionDescription;
                                notificationDocument.ModifiedOn = DateTime.UtcNow.ToString();
                                notificationDocument.ModifiedBy = request.RequestedBy;
                                notificationDocument.AcknowledgeRequired = requiresAck;
                                if (!requiresAck)
                                {
                                    statusCode = await notificationDocument.Deactivate(cosmosContainer, logger, actionDescription);
                                }
                            }
                            else
                            {
                                try
                                {
                                    ItemResponse<NotificationDocument> response = await cosmosContainer.PatchItemAsync<NotificationDocument>(
                                            id: request.NotificationId,
                                            partitionKey: new PartitionKey($"{request.NotificationId}"),
                                            patchOperations: new[] {
                                                PatchOperation.Replace("/ActionTaken", actionDescription),
                                                PatchOperation.Replace("/ModifiedOn", DateTime.UtcNow.ToString()),
                                                PatchOperation.Replace("/ModifiedBy", request.RequestedBy),
                                                PatchOperation.Replace("/AcknowledgeRequired", requiresAck)
                                            }
                                        );

                                    NotificationDocument updated = response.Resource;
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError($"Patch failed on {request.NotificationId} " + ex.Message);
                                    statusCode = HttpStatusCode.InternalServerError;
                                }
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"{request.Action} not taken for {request.NotificationId} notification " + ex.Message);
                    statusCode = HttpStatusCode.InternalServerError;
                }
            }
            return statusCode;
        }

    }
}

