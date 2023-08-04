using System;
using GSANotifications.RuleHandlers;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using System.IO;
using Azure.Identity;
using System.Threading.Tasks;
using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace GSANotifications.Models
{
    public class NotificationDocument
    {
        public const string Category_AuditProgress = "AuditProgress";
        public string id { get; set; }
        public string Category { get; set; } = Category_AuditProgress;
        public string KPIType { get; set; }
        public string TargetAccountId { get; set; }  // cb
        public string TargetAccountName { get; set; }
        public string RegardingAccountId { get; set; } // facility
        public string RegardingAccountName { get; set; }        
        public string HandlerContactId { get; set; }
        public string HandlerName { get; set; }
        public string ApplicationName { get; set; }
        public string AuditName { get; set; }
        public string RuleNumber { get; set; }
        public string RuleDescription { get; set; }
        public DateTime? CriteriaDate { get; set; }
        public DateTime? DateCautionEntered { get; set; }
        public DateTime? DateUrgentEntered { get; set; }
        public string InternalNote { get; set; }
        public string ExternalNote { get; set; }
        public bool AcknowledgeRequired { get; set; } = false;
        public DateTime? DateAcknowledged { get; set; }
        public bool Acknowledged { get; set; } = false;
        public string ActionTaken { get; set; }
        public string CreatedOn { get; set; }
        public string ModifiedOn { get; set; }
        public string ModifiedBy { get; set; }

        public async Task<HttpStatusCode> Deactivate(Container cosmosContainer, ILogger logger, string reason = Rules.CriteriaNoLongerMet)
        {
            HttpStatusCode wasBlobbed = HttpStatusCode.OK;

            // Remove from cosmosdb and store in blob storage

            try
            {
                var response = await cosmosContainer.DeleteItemAsync<NotificationDocument>(id, new PartitionKey(id));
                wasBlobbed = response.StatusCode;

                string containerEndpoint = "https://notificationblobaccount.blob.core.windows.net/Notifications";
                var blobContainerClient = new BlobContainerClient(
                        new Uri(containerEndpoint),
                        new DefaultAzureCredential());

                // Create the container and return a container client object
                //BlobClient blob = blobContainerClient.GetBlobClient(RuleNumber + "_" + AuditName);
                string blobName = RuleNumber + "_" + AuditName;
                string jsonToBlob = JsonConvert.SerializeObject(this, Formatting.Indented);

                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonToBlob)))
                {
                    await blobContainerClient.UploadBlobAsync(blobName, ms);
                }
            }
            catch (Exception ex)
            {
                wasBlobbed = HttpStatusCode.InternalServerError;
                logger.LogError(ex, "Failed attempt to deactivate audit: " + AuditName);
            }
            return wasBlobbed;
        }
    }
}