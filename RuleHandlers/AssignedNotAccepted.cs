using System;
using System.Collections.Generic;
using System.Data;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using GSANotifications.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.Layouts;
using Microsoft.Azure.Functions.Worker.Extensions.Abstractions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GSANotifications.RuleHandlers
{
    public static class AssignedNotAccepted
    {
        // private static readonly ILogger _logger;
        // private static readonly IHttpClientFactory _httpClientFactory;
        
        // static AssignedNotAccepted(ILogger logger, IHttpClientFactory httpClientFactory)
        // {   
        //     _logger = logger;
        //     _httpClientFactory = httpClientFactory;
        // }

        public static async Task<List<NotificationDocument>> RetrieveNotificationsAsync(ILogger logger)
        {
            List<NotificationDocument> incoming = new List<NotificationDocument>();
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, $"http://bapcertificationforms.org/notification/assignednotaccepted");
                httpRequestMessage.Headers.Add("APIKey", "QkFQX3Nlb2Fqa3Rjenhqa2xtc2VmZHBnYmhqcGFoaWJmbWpycmxtbHV4ZHlnZm5ibnN4a2ZqY2xsZmtqZm16Yg==");
                var httpClient = new HttpClient();

                var taskhttpResponseMessage = httpClient.SendAsync(httpRequestMessage);
                taskhttpResponseMessage.Wait();
                if (taskhttpResponseMessage.Result.IsSuccessStatusCode)
                {
                    using (var contentStream = await taskhttpResponseMessage.Result.Content.ReadAsStreamAsync())
                    {
                        if (contentStream != null)
                        {
                            incoming = 
                                JsonSerializer.Deserialize<List<NotificationDocument>>(contentStream);
                        }
                    }
                }
                else
                {
                    logger.LogError("Assign not accepted api issue .. " + taskhttpResponseMessage.Status);
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical("Assign not accepted not working.." + ex.Message);
            }
            return incoming;
        }

        public static async Task<List<NotificationDocument>> Update(Container cosmosContainer, ILogger logger, 
                     List<NotificationDocument> inprocess)
        {            
            var valueTask = RetrieveNotificationsAsync(logger);
            var fromApi = valueTask.Result;

            // Produce single list of violations that need to be stored
            if (inprocess.Any())
            {
                // Find oness no longer meeting criteria and remove from inprocess
                foreach (var i in inprocess)
                {
                    if (!fromApi.Any(x => x.AuditName == i.AuditName))
                    {
                        await i.Deactivate(cosmosContainer, logger, Rules.CriteriaNoLongerMet);
                        inprocess.Remove(i);
                    }
                    else
                    {
                        var f = fromApi.FirstOrDefault(x =>  x.AuditName == i.AuditName);
                        if (f != null)
                        { 
                            fromApi.Remove(f);  // returning list of new notifications only
                        }
                    }
                }
            }

            return (List<NotificationDocument>)fromApi;
        }
    }    
}

