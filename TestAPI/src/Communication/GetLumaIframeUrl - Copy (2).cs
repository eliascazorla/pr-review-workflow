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
        public async Task<string> GetLumaIframeUrl(string feature, int userId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                LumaUser lumaUser = await GetLumaUserByPPlusId(userId, cancellationTokenSource);

                if (lumaUser == null)
                    return null;

                try
                {
                    var content = new
                    {
                        url = $"{embeddedFeatureUrl}/{feature}",
                        access_token = (await GetUserAccessToken(cancellationTokenSource, lumaUser.Id))?.Value
                    };
                    if (content.access_token == null)
                    {
                        logger.WriteInfo($"GetLumaIframeUrl: user access token unavailable. Skipping iframe request for feature {feature}.");
                        return string.Empty;
                    }
                    var response = await apiHelper.HttpPostAsync<object, IframeUrlResponse>(
                        content,
                        embedUrl,
                        "application/json",
                        string.Empty,
                        cancellationTokenSource.Token);

                    return response.Url;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {embedUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {embedUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return string.Empty;
        }
    }
}
