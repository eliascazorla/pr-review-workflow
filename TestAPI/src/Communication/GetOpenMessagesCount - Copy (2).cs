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
        public async Task<int> GetOpenMessagesCount(int patientId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                try
                {
                    var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                    var patient = patientBo.GetById(patientId);
                    var patientListUrl = $"{baseUrl}/{GetPatientListUrl()}?externalId.value={patient.PatientUID}";
                    var apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                    if (apiAccessToken == null || apiAccessToken.Value == "-1")
                    {
                        logger.WriteInfo($"GetOpenMessagesCount: API access token unavailable. Returning 0 for patientId {patientId}.");
                        return 0;
                    }

                    var lumaPatientList = await apiHelper.HttpGetAsync<LumaListResponse<LumaPatient>>(
                        patientListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (lumaPatientList.Size <= 0) return 0;

                    var lumaPatient = lumaPatientList.Response[0];

                    var requestUrl = $"{baseUrl}/{GetOpenMessagesUrl()}&patient={HttpUtility.UrlEncode(lumaPatient._id)}";

                    var response = await apiHelper.HttpGetAsync<LumaListResponse<object>>(
                        requestUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    var result = response.Size;

                    return result;
                }
                catch (Exception e)
                {
                    logger.WriteError($"Request error: {e.Message}");
                    return 0;
                }
            }
            return 0;
        }
    }
}
