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
        public async Task<LumaListResponse<LumaGroup>> GetGroupsList(int page, int pageSize, string searchText, string facilities)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var groupsListUrl = $"{baseUrlV1}/{GetGroupsListUrl()}?usersCount=true";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetGroupsList: API access token unavailable. Returning null for groups list.");
                    return null;
                }
                try
                {
                    var lumaGroupsList = await apiHelper.HttpGetAsync<LumaListResponse<LumaGroup>>(
                        groupsListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (!string.IsNullOrEmpty(searchText))
                    {
                        lumaGroupsList.Response.RemoveAll(x => x.Name.IndexOf(searchText, 0, StringComparison.OrdinalIgnoreCase) == -1);
                    }
                    if (!string.IsNullOrEmpty(facilities))
                    {
                        var facilityIds = facilities.Split(',');
                        var lumaFacilities = await GetLumaFacilities();
                        lumaGroupsList.Response = lumaGroupsList.Response.Where(x =>
                            lumaFacilities.Response != null &&
                            x.Facilities != null &&
                            x.Facilities.Any(fx => facilityIds.Contains(lumaFacilities.Response.FirstOrDefault(fl => fx == fl.Id)?.ExternalRawSource?.Id))).ToList();
                    }

                    return new LumaListResponse<LumaGroup>
                    {
                        Page = page,
                        PageSize = pageSize,
                        Response = lumaGroupsList.Response.Skip(pageSize * page).Take(pageSize).ToList(),
                        Size = lumaGroupsList.Response != null ? lumaGroupsList.Response.Count : 0
                    };
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {groupsListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {groupsListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }
    }
}
