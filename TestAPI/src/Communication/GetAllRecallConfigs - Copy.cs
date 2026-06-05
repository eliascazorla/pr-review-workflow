using DevExpress.Utils.Extensions;
using Hl7.Fhir.Support;
using Nextech.Log.Interfaces;
using PracticePlus.Admin.Client;
using SqlHelpers.LinqToSql;
using SupraMed.BusinessLayer.Exceptions;
using SupraMed.BusinessLayer.Interfaces;
using SupraMed.BusinessLayer.Models.Communication;
using SupraMed.BusinessLayer.Models.Communication.DTOs;
using SupraMed.BusinessLayer.Services.Interfaces.Communication;
using SupraMed.Core.ApiClients;
using SupraMed.Core.Caching;
using SupraMed.DataModels;
using SupraMed.DataModels.DataObjects;
using SupraMed.DataModels.DataObjects.Communication;
using SupraMed.Utility.Helpers;
using SupraMed.ViewModels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Dynamic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Helpers;
using static SupraMed.DataModels.Enumerations.Security;

namespace SupraMed.BusinessLayer.Services.Communication
{
    public partial class CommunicationService : ICommunicationService
    {
        public async Task<LumaListResponse<LumaRecallConfig>> GetAllRecallConfigs()
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var recallConfigsListUrl = $"{baseUrl}/{GetRecallConfigsListUrl()}";
                var apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetAllRecallConfigs: API access token unavailable. Returning null.");
                    return null;
                }
                try
                {
                    var lumaRecallConfigs = await apiHelper.HttpGetAsync<LumaListResponse<LumaRecallConfig>>(
                        recallConfigsListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return lumaRecallConfigs;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {recallConfigsListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {recallConfigsListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }
    }
}
