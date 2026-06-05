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
        public async Task<int> GetPatientRecallsCount(int? userId, int patientId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var patientRecallListUrl = $"{baseUrl}/{GetRecallsListUrl()}";
                var patient = await GetPatient(patientId);
                if (patient?._id != null)
                {
                    patientRecallListUrl += $"?patient={Uri.EscapeDataString(patient._id)}";
                }
                if (!userId.HasValue)
                {
                    throw new ArgumentNullException($"The request to Luma API {patientRecallListUrl} will be skipped. No user in session");
                }
                LumaUser lumaUser = await GetLumaUserByPPlusId(userId.Value, cancellationTokenSource);
                var userAccessToken = await GetUserAccessToken(cancellationTokenSource, lumaUser.Id);
                if (userAccessToken == null)
                {
                    logger.WriteInfo($"GetPatientRecallsCount: user access token unavailable. Returning 0 for patientId {patientId}.");
                    return 0;
                }
                var patientRecalls = patientRecallBO.GetAll()
                    .Where(p => p.PatientId == patientId)
                    .ToList();

                try
                {
                    var lumaRecallsList = await apiHelper.HttpGetAsync<LumaListResponse<LumaRecall>>(
                        patientRecallListUrl,
                        "application/json",
                        $"{userAccessToken.Type} {userAccessToken.Value}",
                        cancellationTokenSource.Token);

                    var matchingRecallsCount = lumaRecallsList.Response
                        .Count(lumaRecall => patientRecalls.Any(pr => pr.LumaRecallId == lumaRecall.Id));

                    return matchingRecallsCount;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {patientRecallListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {patientRecallListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return 0;
        }
    }
}
