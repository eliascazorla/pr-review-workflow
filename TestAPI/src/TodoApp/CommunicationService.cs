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
    public class CommunicationService : ICommunicationService
    {
        private readonly IApiHelper apiHelper;
        private readonly INextechLogger logger;
        private readonly IPatientBO patientBo;
        private readonly IClientSubscriptionBO clientSubscriptionBO;
        private readonly IPracticePlusAdminClient practicePlusAdminClient;
        private readonly ICacheProvider cacheProvider;
        private readonly IUsersBo usersBo;
        private readonly IRolesBO rolesBo;
        private readonly IPracticeSettingBO practiceSettingBo;
        private readonly IAppointmentResourceBO appointmentResourceBo;
        private readonly IProviderBO providerBO;
        private readonly IFacilityBO facilityBO;
        private readonly IPatientRecallBO patientRecallBO;
        private readonly IUserPermissionAuthorizationService authorizationManager;
        private readonly IUserContext userContext;

        private static readonly string baseUrl = AppSettingsHelper.Get("Api.Luma.BaseUrl", "https://api.teal.isbuildingluma.com/api/v2");
        private static readonly string baseUrlV1 = baseUrl.Replace("/v2", "");
        private static readonly string embedUrl = AppSettingsHelper.Get("Api.LumaEmbed.BaseUrl", "https://embedded.teal.isbuildingluma.com/cloak");
        private static readonly string embeddedFeatureUrl = AppSettingsHelper.Get("Api.LumaEmbed.EmbeddedFeatureBaseUrl", "https://next.teal.isbuildingluma.com/embed");
        private static readonly int cacheDurationEnv = AppSettingsHelper.Get("ClientSubscriptionCacheDurationMinutes", 15);

        public const int requestTimeoutMins = 2;
        public const double defaultTokenExpirationTimeSeconds = 300;

        public static string GetAccessTokenEndpoint() => $"auth/token";
        public static string GetUserAccessTokenCacheKey(string userId) => $"LumaUserAccessToken-{HttpUtility.UrlEncode(userId)}";
        public static string GetApiAccessTokenCacheKey(string accountNumber) => $"LumaApiAccessToken-{HttpUtility.UrlEncode(accountNumber)}";
        public static string ChatActivitiesUrl() => $"chatActivities";
        public static string GetOpenMessagesUrl() => $"chatActivities?status=assigned&status=unassigned";
        public static string GetPatientChatUrl(string patientLumaId) => $"hub?embeddedPatientId={HttpUtility.UrlEncode(patientLumaId)}";
        public static string GetPatientListUrl() => $"patients";
        public static string GetGroupsListUrl() => $"groups";
        public static string GetGroupsInvitesUrl() => $"groupInvites";
        public static string GetRecallsListUrl() => $"recalls";
        public static string GetProviderUrl() => $"providers";
        public static string GetRecallConfigsListUrl() => $"followups?model=recall";
        public static string GetFollowUpsUrl() => $"followups";
        public static string GetUsersUrl() => $"users";
        public static string GetUserByEmailUrl(string userEmail) => $"{GetUsersUrl()}?email={HttpUtility.UrlEncode(userEmail)}";
        public static string GetUserByIdUrl(string userId) => $"{GetUsersUrl()}/{HttpUtility.UrlEncode(userId)}";
        public static string GetMasterUserUrl() => $"{GetUsersUrl()}?master=true";
        public static string GetSettingsByIdUrl(string settingId) => $"settings/{HttpUtility.UrlEncode(settingId)}";
        public static string GetSettingsUrl() => $"settings";
        public static string GetFacilitiesListUrl() => $"facilities";
        public static string GetCommunicationHistoryUrl() => $"messages";
        public static string GetAppointmentTypeUrl() => $"appointmentTypes";
        public static string GetRemindersUrl() => $"reminders";

        public CommunicationService(IApiHelper apiHelper, INextechLogger logger,
            IPatientBO patientBo, IClientSubscriptionBO clientSubscriptionBO,
            IPracticePlusAdminClient practicePlusAdminClient, ICacheProvider cacheProvider,
            IUsersBo usersBo, IRolesBO rolesBo,
            IPracticeSettingBO practiceSettingBo,
            IAppointmentResourceBO appointmentResourceBo,
            IProviderBO providerBO,
            IFacilityBO facilityBO,
            IPatientRecallBO patientRecallBO,
            IUserPermissionAuthorizationService authorizationManager,
            IUserContext userContext)
        {
            this.apiHelper = apiHelper;
            this.logger = logger;
            this.patientBo = patientBo;
            this.clientSubscriptionBO = clientSubscriptionBO;
            this.practicePlusAdminClient = practicePlusAdminClient;
            this.cacheProvider = cacheProvider;
            this.usersBo = usersBo;
            this.rolesBo = rolesBo;
            this.practiceSettingBo = practiceSettingBo;
            this.appointmentResourceBo = appointmentResourceBo;
            this.providerBO = providerBO;
            this.facilityBO = facilityBO;
            this.patientRecallBO = patientRecallBO;
            this.authorizationManager = authorizationManager;
            this.userContext = userContext;
        }

        private int ClientSubscriptionCacheDurationMinutes
        {
            get
            {
                string clientSubscriptionCacheDurationMinutes = ConfigurationManager.AppSettings["ClientSubscriptionCacheDurationMinutes"];
                if (int.TryParse(clientSubscriptionCacheDurationMinutes, out int timeToLive))
                {
                    return timeToLive;
                }
                else
                {
                    return 15;
                }
            }
        }

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

        public async Task<bool> ChatActivitiesPermission()
        {
            var enabledProviders = await NextechCommunicationsEnabledProvidersExist();
            return enabledProviders && authorizationManager.IsAuthorized(userContext, PermissionContext.CollaborationHub, Permissions.Update);
        }

        public async Task<int> GetUnreadChatActivitiesCount(int userId)
        {

            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                LumaUser lumaUser = await GetLumaUserByPPlusId(userId, cancellationTokenSource);
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetUnreadChatActivitiesCount: API access token unavailable. Returning 0 for userId {userId}.");
                    return 0;
                }
                try
                {
                    var unreadCount = await apiHelper.HttpGetAsync<LumaUnreadMessagesResponse>(
                        $"{baseUrl}/{ChatActivitiesUrl()}/unread?assignee={HttpUtility.UrlEncode(lumaUser.Id)}",
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);
                    return unreadCount.Count;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError("The request to Luma API to get unread messages count has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API to get unread messages count failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return 0;
        }

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

        public async Task<LumaGroup> DeleteGroup(string id)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var groupDeleteUrl = $"{baseUrl}/{GetGroupsListUrl()}/{HttpUtility.UrlEncode(id)}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"DeleteGroup: API access token unavailable. Skipping delete for group {id}.");
                    return null;
                }
                try
                {
                    var lumaGroups = await apiHelper.HttpDeleteAsync<LumaGroup>(
                        groupDeleteUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return lumaGroups;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {groupDeleteUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {groupDeleteUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public async Task<LumaListResponse<LumaRecall>> GetRecallsList(int page, int pageSize)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var recallsListUrl = $"{baseUrl}/{GetRecallsListUrl()}";
                var apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetRecallsList: API access token unavailable. Returning null.");
                    return null;
                }
                try
                {
                    var lumaRecallsList = await apiHelper.HttpGetAsync<LumaListResponse<LumaRecall>>(
                        recallsListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return new LumaListResponse<LumaRecall>
                    {
                        Page = page,
                        PageSize = pageSize,
                        Response = lumaRecallsList.Response.Skip(pageSize * page).Take(pageSize).ToList(),
                        Size = lumaRecallsList.Response?.Count ?? 0
                    };
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {recallsListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {recallsListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public async Task<LumaListResponse<LumaRecallConfig>> GetRecallConfigsList(int page, int pageSize)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var recallConfigsListUrl = $"{baseUrl}/{GetRecallConfigsListUrl()}";
                var apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetRecallConfigsList: API access token unavailable. Returning null.");
                    return null;
                }
                try
                {
                    var lumaRecallsList = await apiHelper.HttpGetAsync<LumaListResponse<LumaRecallConfig>>(
                        recallConfigsListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return new LumaListResponse<LumaRecallConfig>
                    {
                        Page = page,
                        PageSize = pageSize,
                        Response = lumaRecallsList.Response.OrderByDescending(r => r.CreatedAt).Skip(pageSize * page).Take(pageSize).ToList(),
                        Size = lumaRecallsList.Response?.Count ?? 0
                    };
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

        public async Task<LumaListResponse<LumaRecall>> GetPatientRecalls(int? userId, int? patientId, int page, int pageSize, string sortColumn = "duedate", bool isDescending = true,
            string recallType = "",
            string status = "",
            string providers = "",
            string locations = "",
            string appointmentTypes = "")
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                if (!userId.HasValue)
                {
                    throw new InvalidOperationException($"The requests to Luma API to get recalls information will be skipped. No user in session");
                }
                LumaUser lumaUser = await GetLumaUserByPPlusId(userId.Value, cancellationTokenSource);
                var userAccessToken = await GetUserAccessToken(cancellationTokenSource, lumaUser.Id);

                var activeStatusCd = EnumHelper.ToIdString(PatientRecall.StatusEnum.Active);
                var patientRecallsQuery = patientRecallBO.GetAll()
                    .Where(pr => pr.LumaRecallId != null && pr.StatusCd.Equals(activeStatusCd));

                if (patientId.HasValue)
                {
                    patientRecallsQuery = patientRecallsQuery.Where(p => p.PatientId == patientId.Value);

                    var lumaPatient = await GetPatient(patientId.Value);
                    if (lumaPatient?._id == null)
                    {
                        throw new InvalidOperationException($"The requests to Luma API to get recalls information will be skipped. Patient not found. Patient id: {patientId.Value}");
                    }
                }

                if (!string.IsNullOrEmpty(providers))
                {
                    var providerIds = providers.Split(',')
                        .Select(p => p.Trim())
                        .ToArray();
                    patientRecallsQuery = patientRecallsQuery
                        .Include(pr => pr.AppointmentResource)
                        .Where(pr => pr.AppointmentResource != null &&
                                     pr.AppointmentResource.ProviderId.HasValue &&
                                     providerIds.Contains(pr.AppointmentResource.ProviderId.Value.ToString()));
                }

                if (!string.IsNullOrEmpty(locations))
                {
                    var locationIds = locations.Split(',')
                        .Select(l => l.Trim())
                        .ToArray();

                    patientRecallsQuery = patientRecallsQuery
                        .Where(pr => locationIds.Contains(pr.FacilityId.ToString()));
                }

                if (!string.IsNullOrEmpty(appointmentTypes))
                {
                    var appointmentTypesIds = appointmentTypes.Split(',')
                        .Select(a => a.Trim())
                        .ToArray();

                    patientRecallsQuery = patientRecallsQuery
                        .Where(pr => appointmentTypesIds.Contains(pr.AppointmentTypeId.ToString()));
                }

                var patientRecalls = patientRecallsQuery
                    .Include(pr => pr.Patient)
                    .Include(pr => pr.RecallReason)
                    .Include(pr => pr.AppointmentResource);

                if (!string.IsNullOrEmpty(status))
                {
                    // Materialize the query before filtering on the computed Status property,
                    // which is not translatable to SQL.
                    patientRecalls = patientRecalls.ToList()
                        .Where(pr => status.Equals(pr.Status))
                        .AsQueryable();
                }

                try
                {
                    List<PatientRecall> pagedRecalls;
                    if (sortColumn.Contains("date"))
                    {
                        // Change value for the sort column because the models have different name for the date property
                        // PatientRecall -> DueDate
                        // LumaRecall -> Date
                        pagedRecalls = LinqToSqlExtensions.OrderBy(patientRecalls, "duedate", !isDescending)
                        .Skip(pageSize * page).Take(pageSize).ToList();
                    }
                    else if (sortColumn == "status")
                    {
                        if (isDescending)
                            pagedRecalls = patientRecalls.ToList().OrderByDescending(pr => pr.Status)
                                .Skip(pageSize * page).Take(pageSize).ToList();
                        else
                            pagedRecalls = patientRecalls.ToList().OrderBy(pr => pr.Status)
                                .Skip(pageSize * page).Take(pageSize).ToList();
                    }
                    else
                    {
                        pagedRecalls = patientRecalls.Skip(pageSize * page).Take(pageSize).ToList();
                    }

                    await SetRecallTypeAndFilter(pagedRecalls, recallType);

                    List<LumaRecall> filteredRecalls = (await Task.WhenAll(
                        pagedRecalls.Select(async patientRecall =>
                        {
                            var patientRecallUrl = $"{baseUrl}/{GetRecallsListUrl()}/{patientRecall.LumaRecallId}";
                            try
                            {
                                var lumaRecall = await apiHelper.HttpGetAsync<LumaRecall>(
                                    patientRecallUrl,
                                    "application/json",
                                    $"{userAccessToken.Type} {userAccessToken.Value}",
                                    cancellationTokenSource.Token);
                                return new LumaRecall
                                {
                                    Id = lumaRecall.Id,
                                    Date = lumaRecall.Date,
                                    AppointmentType = lumaRecall.AppointmentType,
                                    Provider = lumaRecall.Provider,
                                    Facility = lumaRecall.Facility,
                                    Status = lumaRecall.Status,
                                    Patient = lumaRecall.Patient,
                                    PatientName = $"{patientRecall.Patient.FirstName} {patientRecall.Patient.LastName}",
                                    Note = patientRecall.Note,
                                    PatientPhone = patientRecall.Patient.ContactInformation?.PreferredMethod != EnumHelper.ToIdString(DataModels.Enumerations.ContactMethodCode.Email) ?
                                            patientRecall.Patient.ContactInformation?.PreferredInformation ?? "" : ""
                                };
                            }
                            catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                            {
                                logger.WriteError($"The request to Luma API {patientRecallUrl} has timed out. The server did not respond within the expected time frame.", ex);
                                throw;
                            }
                            catch (Exception ex)
                            {
                                logger.WriteError($"The request to Luma API {patientRecallUrl} failed. Exception message: {ex.Message}.", ex);
                                throw;
                            }
                        }).ToList()
                    )).ToList();

                    await SetScheduledDate(filteredRecalls);
                    var allRecalls = patientRecalls.ToList();
                    var size = allRecalls.Count;

                    return new LumaListResponse<LumaRecall>
                    {
                        Page = page,
                        PageSize = pageSize,
                        Response = SetupPatientRecallListResponse(filteredRecalls, allRecalls),
                        Size = size
                    };
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API to get recalls information has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API to get recalls information failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<List<PatientRecall>> SetRecallTypeAndFilter(List<PatientRecall> mergedList, string recallType)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var recallConfigs = (await GetAllRecallConfigs())?.Response;
                await FillOutRecallsLumaData(mergedList);

                foreach (var recall in mergedList)
                {
                    if (RecallMatchesConfig(recall, recallConfigs))
                        recall.RecallType = "Automated";
                }
                if (!string.IsNullOrEmpty(recallType))
                {
                    return mergedList.Where(pr => pr.RecallType.Equals(recallType)).ToList();
                }
                return mergedList;
            }
            return null;
        }

        private async Task FillOutRecallsLumaData(List<PatientRecall> recallsList)
        {
            var lumaFacilities = await GetLumaFacilities();
            var appointmentTypeIds = recallsList.GroupBy(pr => pr.AppointmentTypeId).Select(gr => gr.Key).ToList();
            var lumaAppointmentTypes = await Task.WhenAll(appointmentTypeIds.Select(async t => await GetLumaAppointmentType(t)));
            var resources = recallsList.GroupBy(pr => pr.AppointmentResourceId)
                .Select(gr => new { gr.Key, Value = appointmentResourceBo.GetAppointmentResource(gr.Key) }).ToList();
            var lumaProviders = await Task.WhenAll(resources.Select(r => GetLumaProviderByResourceId(r.Key)));

            foreach (var recall in recallsList)
            {
                recall.LumaFacilityId = lumaFacilities.Response.Where(f => f.ExternalRawSource.Id.Equals(recall.FacilityId.ToString())).FirstOrDefault()?.Id;
                recall.LumaProviderId = lumaProviders.Where(p => p.ExternalId.Value
                    .Equals(resources.Where(r => r.Key == recall.AppointmentResourceId).First().Value.ProviderId.ToString())).FirstOrDefault()?.Id;
                recall.LumaAppointmentTypeId = lumaAppointmentTypes.Where(t => t.ExternalId.Value.Equals(recall.AppointmentTypeId.ToString())).FirstOrDefault()?.Id;
            }
        }


        private async Task SetScheduledDate(List<LumaRecall> mergedList)
        {
            foreach (var recall in mergedList)
            {
                if (recall.RecallType == "Automated")
                {
                    var validStatuses = new[] { "scheduled", "sent", "delivered" };
                    var patientScheduledReminders = await GetLumaPatientScheduledRecalls(recall.Patient, recall.Id);
                    // Select nearest scheduled reminder first, nearest sent reminder if no scheduled reminders exist, or null if no reminders exist
                    var foundRecall = patientScheduledReminders?.Response
                            .Where(s => validStatuses.Contains(s.Status))
                            .OrderByDescending(sr => sr.Status == "scheduled")
                            .ThenBy(sr => sr.Status == "scheduled" ? sr.SendAt : DateTime.MinValue)
                            .ThenByDescending(sr => sr.Status == "sent" || sr.Status == "delivered" ? sr.SendAt : DateTime.MinValue)
                        .FirstOrDefault();
                    recall.ScheduleDate = recall.Status != "scheduled" ? foundRecall?.SendAt.ToString() : null;
                }
            }
        }

        private bool RecallMatchesConfig(PatientRecall recall, List<LumaRecallConfig> configs)
        {
            if (configs.IsNullOrEmpty())
            {
                return false;
            }

            return configs.Where(config => config.Enabled).Any(config =>
                    // Return true if any of the configs have no filters since it will match all recalls
                    config.Filters.IsNullOrEmpty() ||
                (
                    // All "not equal" filters must match
                    config.Filters.Where(f => f.Comparison == "ne").All(f =>
                    {
                        switch (f.Field)
                        {
                            case "type":
                                return !f.Value.Equals(recall.LumaAppointmentTypeId);
                            case "facility":
                                return !f.Value.Equals(recall.LumaFacilityId);
                            case "provider":
                                return !f.Value.Equals(recall.LumaProviderId);
                            default:
                                return true;
                        }
                    })
                    &&
                    // For each field that has "equals" filters, at least one must match
                    new[] { "type", "facility", "provider" }.All(field =>
                        !config.Filters.Exists(f => f.Comparison == "eq" && f.Field == field) ||
                        config.Filters.Where(f => f.Comparison == "eq" && f.Field == field).Any(f =>
                        {
                            switch (f.Field)
                            {
                                case "type":
                                    return f.Value.Equals(recall.AppointmentType);
                                case "facility":
                                    return f.Value.Equals(recall.Facility);
                                case "provider":
                                    return f.Value.Equals(recall.AppointmentResource?.ProviderId);
                                default:
                                    return false;
                            }
                        })
                    )
                )
            );
        }

        public async Task<LumaRecall> CreatePatientRecall(PatientRecall patientRecall)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var apiAccessToken = await GetApiAccessToken(cancellationTokenSource);

                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"CreatePatientRecall: API access token unavailable. Returning null.");
                    return null;
                }
                // Setting dueDate time to client's noon and converting it to utc to send to Luma.
                var dueDate = patientRecall.DueDate.Value;
                var utcDueDate = TimeZoneInfo.ConvertTimeToUtc(new DateTime(dueDate.Year, dueDate.Month, dueDate.Day, 12, 0, 0, DateTimeKind.Unspecified), clientSubscriptionBO.GetClientTimeZone());

                var recallsListUrl = $"{baseUrl}/{GetRecallsListUrl()}";
                var lumaRecall = await CreateRecallPayload(patientRecall.PatientId, patientRecall.FacilityId, patientRecall.AppointmentResourceId, patientRecall.AppointmentTypeId, utcDueDate, "pending", "upload");
                try
                {
                    var createdRecall = await apiHelper.HttpPostAsync<object, LumaRecall>(
                        lumaRecall,
                        recallsListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return createdRecall;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {recallsListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {recallsListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<LumaProviderDto> GetLumaProviderByResourceId(int appointmentResourceId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var appointmentResource = appointmentResourceBo.GetAppointmentResource(appointmentResourceId);
                if (appointmentResource == null)
                {
                    return null;
                }
                var providersListUrl = $"{baseUrl}/{GetProviderUrl()}?externalId.value={appointmentResource.ProviderId}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetLumaProviderByResourceId: API access token unavailable. Returning null for appointmentResourceId {appointmentResourceId}.");
                    return null;
                }
                try
                {
                    var lumaProvidersList = await apiHelper.HttpGetAsync<LumaListResponse<LumaProviderDto>>(
                        providersListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (lumaProvidersList.Response.Count == 0)
                    {
                        return null;
                    }

                    return lumaProvidersList.Response.FirstOrDefault();
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {providersListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {providersListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<LumaAppointmentType> GetLumaAppointmentType(int? appointmentTypeId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var appointmentTypesListUrl = $"{baseUrl}/{GetAppointmentTypeUrl()}?externalId.value={appointmentTypeId}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetLumaAppointmentType: API access token unavailable. Returning null for appointmentTypeId {appointmentTypeId}.");
                    return null;
                }
                try
                {
                    var lumaAppointmentTypeList = await apiHelper.HttpGetAsync<LumaListResponse<LumaAppointmentType>>(
                        appointmentTypesListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (lumaAppointmentTypeList.Response.Count == 0)
                    {
                        return null;
                    }

                    return lumaAppointmentTypeList.Response.FirstOrDefault();
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {appointmentTypesListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {appointmentTypesListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private List<LumaRecall> SetupPatientRecallListResponse(
            List<LumaRecall> filteredLumaRecalls,
            IReadOnlyCollection<PatientRecall> patientRecalls)
        {
            foreach (var recall in filteredLumaRecalls)
            {
                var matchingPatientRecall = patientRecalls.FirstOrDefault(pr => pr.LumaRecallId == recall.Id);

                if (matchingPatientRecall != null)
                {
                    var providerName = GetProviderName(matchingPatientRecall.AppointmentResourceId);
                    recall.Provider = $"{providerName.FirstName} {providerName.LastName}";
                    var locationName = GetLocationName(matchingPatientRecall.FacilityId);
                    recall.Facility = locationName.Name;
                    recall.InternalId = matchingPatientRecall.Id;
                    recall.InternalAppointmentTypeId = matchingPatientRecall.AppointmentTypeId;
                    recall.AppointmentTypeName = matchingPatientRecall.AppointmentType?.AppointmentTypeName;
                    recall.AppointmentResourceId = matchingPatientRecall.AppointmentResourceId;
                    recall.FacilityId = matchingPatientRecall.FacilityId;
                    recall.ConsultReasonCd = matchingPatientRecall.ConsultReasonCd;
                    recall.Note = matchingPatientRecall.Note;

                    if (recall.Status == "scheduled")
                    {
                        recall.Status = "Scheduled";
                    }
                    else
                    {
                        recall.Status = "Not Scheduled";
                    }

                    if (recall.Status == "active")
                        recall.Status = "Not Scheduled";
                }
            }

            return filteredLumaRecalls;
        }

        private async Task<LumaListResponse<LumaReminder>> GetLumaPatientScheduledRecalls(string patientId, string recallId = "")
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var patientRemindersListUrl = $"{baseUrl}/{GetRemindersUrl()}?patient={patientId}";

                if (!string.IsNullOrEmpty(recallId))
                {
                    patientRemindersListUrl += $"&recall={recallId}";
                }

                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetLumaPatientScheduledRecalls: API access token unavailable. Returning null for patientId {patientId}.");
                    return null;
                }
                try
                {
                    var lumaPatientReminder = await apiHelper.HttpGetAsync<LumaListResponse<LumaReminder>>(
                        patientRemindersListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return lumaPatientReminder;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {patientRemindersListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    return null;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {patientRemindersListUrl} failed. Exception message: {ex.Message}.", ex);
                    return null;
                }
            }
            return null;
        }

        private LumaProviderDto GetProviderName(int appointmentResourceId)
        {
            var appointmentResource = appointmentResourceBo.GetAppointmentResource(appointmentResourceId);
            if (appointmentResource.ProviderId == null) return null;
            var provider = providerBO.GetProviderById((int)appointmentResource.ProviderId);
            var user = usersBo.GetUserById(provider.UserId);
            return new LumaProviderDto
            {
                FirstName = user.FirstName,
                LastName = user.LastName
            };

        }

        private LumaLocationDto GetLocationName(int locationId)
        {
            var location = facilityBO.GetFacility(locationId);
            return new LumaLocationDto
            {
                Name = location.Name
            };
        }

        public async Task<string> GetPatientChatIframeUrl(int patientId, int userId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var patient = patientBo.GetById(patientId);
                var patientListUrl = $"{baseUrl}/{GetPatientListUrl()}?externalId.value={patient.PatientUID}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetPatientChatIframeUrl: API access token unavailable. Returning null for patientId {patientId}.");
                    return null;
                }
                try
                {
                    var lumaPatientList = await apiHelper.HttpGetAsync<LumaListResponse<LumaPatient>>(
                        patientListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (lumaPatientList.Size > 0)
                    {
                        var lumaPatient = lumaPatientList.Response[0];
                        return await GetLumaIframeUrl(GetPatientChatUrl(HttpUtility.UrlEncode(lumaPatient?._id)), userId);
                    }

                    // If no matching patient is found return empty URL
                    return "";
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {patientListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {patientListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return "";
        }

        public async Task<LumaPatient> GetPatient(int patientId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var patient = patientBo.GetById(patientId);
                var patientListUrl = $"{baseUrl}/{GetPatientListUrl()}?externalId.value={patient.PatientUID}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetPatient: API access token unavailable. Returning null for patientId {patientId}.");
                    return null;
                }
                try
                {
                    var lumaPatientList = await apiHelper.HttpGetAsync<LumaListResponse<LumaPatient>>(
                        patientListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (lumaPatientList.Response.Count == 0)
                    {
                        // Try secondaryExternalId.value if no results for externalId.value
                        var secondaryPatientListUrl = $"{baseUrl}/{GetPatientListUrl()}?secondaryExternalId.value={patient.PatientUID}";
                        lumaPatientList = await apiHelper.HttpGetAsync<LumaListResponse<LumaPatient>>(
                            secondaryPatientListUrl,
                            "application/json",
                            $"{apiAccessToken.Type} {apiAccessToken.Value}",
                            cancellationTokenSource.Token);

                        if (lumaPatientList.Response.Count == 0)
                        {
                            return null;
                        }
                    }
                    return lumaPatientList.Response[0];
                }

                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {patientListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {patientListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public async Task<CommunicationSettingsDTO> GetCommunicationSettings()
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetCommunicationSettings: API access token unavailable. Returning null.");
                    return null;
                }

                var lumaMasterUser = await GetLumaMasterUser(cancellationTokenSource);
                var lumaSettingApiUrl = $"{baseUrl}/{GetSettingsByIdUrl(lumaMasterUser.Setting)}";
                try
                {
                    var lumaSetting = await apiHelper.HttpGetAsync<LumaSetting>(
                        lumaSettingApiUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );

                    var communicationSettings = lumaSetting.Settings.Communication;
                    return new CommunicationSettingsDTO
                    {
                        MinLengthToReopenChat = communicationSettings.MinLengthToReopenChat,
                        Id = lumaSetting.Id,
                        ReopenChatsForUnclassifiedReplies = communicationSettings.ReopenChatsForUnclassifiedReplies,
                        ReopenChatsWithStatusUnassigned = communicationSettings.ReopenChatsWithStatusUnassigned,
                        SendAfterHoursMessageForInboundChats = communicationSettings.SendAfterHoursMessageForInboundChats,
                        ExpirePatientDataEnabled = communicationSettings.ExpirePatientData.Enabled,
                        ExpirePatientDataMaxAgeInDays = communicationSettings.ExpirePatientData.MaxAgeInDays,
                        OfficeHoursBegin = communicationSettings.OfficeHours.BeginAt,
                        OfficeHoursEnd = communicationSettings.OfficeHours.EndAt,
                        ChatAssignmentOnProfileSend = communicationSettings.ChatAssignmentOnProfileSend,
                        ChatAssignmentOnHubSend = communicationSettings.ChatAssignmentOnHubSend,
                        SortMessagesBy = communicationSettings.SortMessagesBy,
                        DefaultChatCommunicationSecurityLevel = communicationSettings.DefaultChatCommunicationSecurity.Level,
                        DefaultChatVisibility = communicationSettings.DefaultChatVisibility,
                        BlockedContacts = string.Join(";", communicationSettings.BlockedContacts)
                    };
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {lumaSettingApiUrl} GET has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {lumaSettingApiUrl} GET failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public async Task<CommunicationSettingsDTO> UpdateCommunicationSettings(CommunicationSettingsDTO communicationSettings, int userId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"UpdateCommunicationSettings: API access token unavailable. Returning null for userId: {userId}.");
                    return null;
                }
                var lumaUser = await GetLumaUserByPPlusId(userId, cancellationTokenSource);
                var lumaSettingApiUrl = $"{baseUrl}/{GetSettingsByIdUrl(communicationSettings.Id)}";
                try
                {
                    var updatedSettings = new LumaSetting
                    {
                        Id = communicationSettings.Id,
                        Settings = new LumaSettingValues
                        {
                            Communication = new LumaCommunicationSetting
                            {
                                MinLengthToReopenChat = communicationSettings.MinLengthToReopenChat,
                                ReopenChatsForUnclassifiedReplies = communicationSettings.ReopenChatsForUnclassifiedReplies,
                                ReopenChatsWithStatusUnassigned = communicationSettings.ReopenChatsWithStatusUnassigned,
                                SendAfterHoursMessageForInboundChats = communicationSettings.SendAfterHoursMessageForInboundChats,
                                ExpirePatientData = new ExpirePatientData
                                {
                                    Enabled = communicationSettings.ExpirePatientDataEnabled,
                                    MaxAgeInDays = communicationSettings.ExpirePatientDataMaxAgeInDays
                                },
                                ChatAssignmentOnHubSend = communicationSettings.ChatAssignmentOnHubSend,
                                ChatAssignmentOnProfileSend = communicationSettings.ChatAssignmentOnProfileSend,
                                DefaultChatCommunicationSecurity = new DefaultChatCommunicationSecurity
                                {
                                    Level = communicationSettings.DefaultChatCommunicationSecurityLevel,
                                    AcknowledgedBy = lumaUser.Id,
                                    AcknowledgedAt = DateTime.UtcNow.ToString("O")
                                },
                                DefaultChatVisibility = communicationSettings.DefaultChatVisibility,
                                OfficeHours = new OfficeHours { BeginAt = communicationSettings.OfficeHoursBegin, EndAt = communicationSettings.OfficeHoursEnd },
                                SortMessagesBy = communicationSettings.SortMessagesBy,
                                BlockedContacts = communicationSettings.BlockedContacts != null ? communicationSettings.BlockedContacts.Split(';') : new string[] { }
                            }
                        }

                    };

                    await apiHelper.HttpPutAsync<LumaSetting, LumaSetting>(
                        updatedSettings,
                        lumaSettingApiUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );

                    return communicationSettings;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {lumaSettingApiUrl} PUT has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {lumaSettingApiUrl} PUT failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public static string GetLumaContactType(PreferredContactInfoVM contactInfoVM)
        {
            if (contactInfoVM?.MethodCd == DataModels.Enumerations.ContactMethodCode.Email)
            {
                return "email";
            }
            else
            {
                if (contactInfoVM.TextMessageEnabled)
                {
                    return "sms";
                }
                else
                {
                    return "voice";
                }
            }
        }

        public async Task UpdateLumaPatientDemographicInfo(PatientVM patientVM)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var patient = patientBo.GetById(patientVM.Id);
                var lumaPatientTemp = await GetPatient(patient.Id);
                if (patient != null && lumaPatientTemp != null)
                {
                    var patientListUrl = $"{baseUrl}/{GetPatientListUrl()}/{lumaPatientTemp._id}";
                    AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                    if (apiAccessToken == null || apiAccessToken.Value == "-1")
                    {
                        logger.WriteInfo($"UpdateLumaPatientDemographicInfo: API access token unavailable. Returning.");
                        return;
                    }

                    dynamic lumaPatient = new ExpandoObject();
                    lumaPatient.name = patientVM?.FullName;
                    lumaPatient.firstname = patientVM?.FirstName;
                    lumaPatient.lastname = patientVM?.LastName;
                    lumaPatient.email = patientVM?.ContactInformation?.Email ?? "";


                    // Unable to use patientVM.ContactInformation.PreferredPhone because email is put in here if it's the primary method
                    if (patientVM.ContactInformation.PrimaryPreferredContactInfo?.MethodCd != DataModels.Enumerations.ContactMethodCode.Email)
                    {
                        lumaPatient.displayPhone = patientVM?.ContactInformation.PrimaryPreferredContactInfo.Information;
                    }
                    else if (patientVM.ContactInformation.BackupPreferredContactInfo != null &&
                        patientVM.ContactInformation.BackupPreferredContactInfo?.MethodCd != DataModels.Enumerations.ContactMethodCode.Email)
                    {
                        lumaPatient.displayPhone = patientVM?.ContactInformation.BackupPreferredContactInfo.Information;
                    }
                    else
                    {
                        lumaPatient.displayPhone = "";
                    }

                    if (patientVM.DOB.HasValue)
                    {
                        lumaPatient.dateOfBirth = new
                        {
                            year = patientVM.DOB.Value.Year,
                            month = patientVM.DOB.Value.Month,
                            day = patientVM.DOB.Value.Day
                        };
                    }
                    else
                    {
                        lumaPatient.dateOfBirth = new
                        {
                            year = 0,
                            month = 0,
                            day = 0
                        };
                    }

                    lumaPatient.contact = new List<object>
                {
                    new
                    {
                        type = GetLumaContactType(patientVM.ContactInformation.PrimaryPreferredContactInfo),
                        value = patientVM?.ContactInformation.PrimaryPreferredContactInfo?.Information,
                        active = true,
                        archived = false,
                        archivedReason = "none"
                    }
                };

                    if (patientVM.ContactInformation.BackupPreferredContactInfo != null)
                    {
                        lumaPatient.contact.Add(
                            new
                            {
                                type = GetLumaContactType(patientVM.ContactInformation.BackupPreferredContactInfo),
                                value = patientVM?.ContactInformation.BackupPreferredContactInfo?.Information,
                                active = true,
                                archived = false,
                                archivedReason = "none"
                            }
                        );
                    }

                    try
                    {
                        await apiHelper.HttpPutAsync<object, LumaPatient>(
                            lumaPatient,
                            patientListUrl,
                            "application/json",
                            $"{apiAccessToken.Type} {apiAccessToken.Value}",
                            cancellationTokenSource.Token
                        );
                    }
                    catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                    {
                        logger.WriteError($"The request to Luma API {patientListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.WriteError($"The request to Luma API {patientListUrl} failed. Exception message: {ex.Message}.", ex);
                        throw;
                    }
                }
            }
        }

        public string CreateLumaUserEmail(string userEmail, string accountNumber)
        {
            var emailPrefix = userEmail.Split('@')[0];
            var emailDomain = userEmail.Split('@')[1];
            return $"{emailPrefix}+{accountNumber?.ToLowerInvariant()}@{emailDomain}";
        }

        public async Task CreateOrUpdateUser(Users mappedUser, List<int> roleIds)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                try
                {
                    var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                    var lumaUser = await GetLumaUserByEmail(cancellationTokenSource, mappedUser.Email);
                    if (lumaUser == null)
                    {
                        await CreateLumaUser(mappedUser, roleIds, cancellationTokenSource);
                        return;
                    }
                    await UpdateLumaUser(lumaUser, mappedUser, roleIds, cancellationTokenSource);
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API to UPDATE user {mappedUser.Id} has failed. Exception message: {ex.Message}.", ex);
                }
            }
        }

        private async Task UpdateLumaUser(LumaUser lumaUser, Users mappedUser, List<int> roleIds, CancellationTokenSource cancellationTokenSource)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var clientSubscription = clientSubscriptionBO.GetClientSubscription();
                mappedUser.AccountNumber = clientSubscription.PracticeId;
                var updateUserApiUrl = $"{baseUrl}/{GetUserByIdUrl(lumaUser.Id)}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"UpdateLumaUser: API access token unavailable. Returning.");
                    return;
                }

                PatientCommunicationRole patientCommunicationRole = (PatientCommunicationRole)mappedUser.PatientCommunicationRoleId;
                dynamic updatedLumaUser = new ExpandoObject();
                updatedLumaUser.email = CreateLumaUserEmail(mappedUser.Email, mappedUser.AccountNumber);
                updatedLumaUser.displayPhone = mappedUser.MobilePhone;
                updatedLumaUser.roles = new string[] { patientCommunicationRole.ToString().ToLower() };
                updatedLumaUser.groups = mappedUser.PatientCommunicationGroups?.Where(g => !g.ToLower().Contains("all"));
                updatedLumaUser.active = mappedUser.UserStatusCd == "A";
                updatedLumaUser.salesforceData = new
                {
                    provisioning = GetUserProvisioning(roleIds)
                };
                if (!lumaUser.IsMaster)
                {
                    updatedLumaUser.name = $"{mappedUser.FirstName} {mappedUser.LastName}";
                    updatedLumaUser.firstname = mappedUser.FirstName;
                    updatedLumaUser.lastname = mappedUser.LastName;
                }

                try
                {
                    await apiHelper.HttpPutAsync<object, LumaUser>(
                        updatedLumaUser,
                        updateUserApiUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {updateUserApiUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {updateUserApiUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
        }

        private async Task<LumaUser> CreateLumaUser(Users mappedUser, List<int> roleIds, CancellationTokenSource cancellationTokenSource)
        {
            if (!await NextechCommunicationsEnabledProvidersExist())
                return null;

            var clientSubscription = clientSubscriptionBO.GetClientSubscription();
            mappedUser.AccountNumber = clientSubscription.PracticeId;

            var groupInvitesUrl = $"{baseUrlV1}/{GetGroupsInvitesUrl()}";
            AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
            if (apiAccessToken == null || apiAccessToken.Value == "-1")
            {
                logger.WriteInfo($"CreateLumaUser: API access token unavailable. Returning.");
                return null;
            }
            if (mappedUser?.PatientCommunicationRoleId == null)
            {
                logger.WriteInfo($"CreateLumaUser: PatientCommunicationRoleId is null for user Id='{mappedUser?.Id}', AccountNumber='{mappedUser?.AccountNumber}'. Returning.");
                return null;
            }

            // Validate that the roleId is a valid enum value
            if (!Enum.IsDefined(typeof(PatientCommunicationRole), mappedUser.PatientCommunicationRoleId))
            {
                logger.WriteError($"CreateLumaUser: Invalid PatientCommunicationRoleId '{mappedUser.PatientCommunicationRoleId}' for user Id='{mappedUser?.Id}', AccountNumber='{mappedUser?.AccountNumber}'. Value is outside valid enum range. Returning.");
                return null;
            }

            PatientCommunicationRole patientCommunicationRole = (PatientCommunicationRole)mappedUser.PatientCommunicationRoleId;

            // Build groups list: add user's groups (excluding "all" entries), or fetch and add default group if none assigned
            var groupsList = new List<string>();
            if (mappedUser.PatientCommunicationGroups != null)
            {
                groupsList.AddRange(
                    mappedUser.PatientCommunicationGroups
                        .Where(g => !string.IsNullOrWhiteSpace(g))
                        .Select(g => g.Trim())
                        .Where(g => !g.Equals("all", StringComparison.OrdinalIgnoreCase))
                );
            }

            if (groupsList.Count == 0)
            {
                try
                {
                    var groupsUrl = $"{baseUrlV1}/{GetGroupsListUrl()}?name={HttpUtility.UrlEncode("Default")}";
                    var groupsResponse = await apiHelper.HttpGetAsync<LumaListResponse<LumaGroup>>(
                        groupsUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (groupsResponse?.Response != null && groupsResponse.Response.Count > 0 && !string.IsNullOrEmpty(groupsResponse.Response[0].Id))
                    {
                        groupsList.Add(groupsResponse.Response[0].Id);
                    }
                    else
                    {
                        logger.WriteInfo($"Failed to find 'Default' group in Luma for Account {mappedUser.AccountNumber}");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    logger.WriteError($"Failed to retrieve 'Default' group from Luma: {ex.Message}", ex);
                    return null;
                }
            }

            var invitePayload = new
            {
                firstname = mappedUser.FirstName,
                lastname = mappedUser.LastName,
                email = CreateLumaUserEmail(mappedUser.Email, mappedUser.AccountNumber), // Create Luma-specific email
                displayPhone = mappedUser.MobilePhone,
                groups = groupsList,
                roles = new[] { patientCommunicationRole.ToString().ToLower() }
        };

            // Declare inviteId and token here so they're in scope for the PUT call below
            string inviteId = null;
            object token = null;

            try
            {
                // 1) POST to create a group invite
                await apiHelper.HttpPostAsync<object, dynamic>(
                    invitePayload,
                    groupInvitesUrl,
                    "application/json",
                    $"{apiAccessToken.Type} {apiAccessToken.Value}",
                    cancellationTokenSource.Token);

            }
            catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
            {
                logger.WriteError($"The request Post call to Luma API {groupInvitesUrl} has timed out. The server did not respond within the expected time frame.", ex);
                return null;
            }
            catch (Exception ex)
            {
                logger.WriteError($"The request Post call to Luma API {groupInvitesUrl} failed. Exception message: {ex.Message}.", ex);
                return null;
            }

            // 1.5) GET the group invite to retrieve the token using the email
            var getInviteUrl = $"{groupInvitesUrl}?email={HttpUtility.UrlEncode(CreateLumaUserEmail(mappedUser.Email, mappedUser.AccountNumber))}";
            try
            {
                var getInviteResponse = await apiHelper.HttpGetAsync<LumaListResponse<LumaGroupInvites>>(
                    getInviteUrl,
                    "application/json",
                    $"{apiAccessToken.Type} {apiAccessToken.Value}",
                    cancellationTokenSource.Token);

                if (getInviteResponse == null)
                    return null;

                //Log details about the invite response(mask token for security)
                try
                {
                    var invite = getInviteResponse.Response?.FirstOrDefault();
                    var inviteToken = invite?.Token;
                    // Find the invite with a token in the response
                    inviteId = invite?.InvitedUser;
                    var tokenPreview = !string.IsNullOrEmpty(inviteToken) && inviteToken.Length > 6
                        ? $"{inviteToken.Substring(0, 3)}...{inviteToken.Substring(inviteToken.Length - 3)}"
                        : inviteToken;

                    token = new
                    {
                        token = inviteToken
                    };

                    logger.WriteInfo($"Group invite GET response for invitedUser={invite?.InvitedUser}, hasToken={(inviteToken != null)}, tokenPreview={tokenPreview}");
                }
                catch (Exception logEx)
                {
                    logger.WriteError($"Failed to log group invite response details: {logEx.Message}", logEx);
                }
            }
            catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
            {
                logger.WriteError($"The request Get call to Luma API {groupInvitesUrl} has timed out. The server did not respond within the expected time frame.", ex);
                return null;
            }
            catch (Exception ex)
            {
                logger.WriteError($"The request Get call to Luma API {groupInvitesUrl} failed. Exception message: {ex.Message}.", ex);
                return null;
            }


            // 2) PUT to confirm the invite (this creates the user on Luma)
            if (string.IsNullOrEmpty(inviteId) || token == null)
            {
                return null;
            }

            var putUrl = $"{groupInvitesUrl}/{HttpUtility.UrlEncode(inviteId)}";
            LumaUser createdUser;

            try
            {
                // The PUT is expected to return the created Luma user
                createdUser = await apiHelper.HttpPutAsync<object, LumaUser>(
                    token,
                    putUrl,
                    "application/json",
                    $"{apiAccessToken.Type} {apiAccessToken.Value}",
                    cancellationTokenSource.Token);
            }
            catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
            {
                logger.WriteError($"The request Put call to Luma API {groupInvitesUrl} has timed out. The server did not respond within the expected time frame.", ex);
                return null;
            }
            catch (Exception ex)
            {
                logger.WriteError($"The request Put call to Luma API {groupInvitesUrl} failed. Exception message: {ex.Message}.", ex);
                return null;
            }

            // 3) Assign provisioning based on roles
            var provisioningPayload = new
            {
                salesforceData = new
                {
                    provisioning = GetUserProvisioning(roleIds)
                }
            };
            var updateUserApiUrl = $"{baseUrl}/{GetUserByIdUrl(createdUser.Id)}";
            try
            {
                await apiHelper.HttpPutAsync<object, LumaUser>(
                    provisioningPayload,
                    updateUserApiUrl,
                    "application/json",
                    $"{apiAccessToken.Type} {apiAccessToken.Value}",
                    cancellationTokenSource.Token
                );
            }
            catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
            {
                logger.WriteError($"The request to Luma API {updateUserApiUrl} has timed out. The server did not respond within the expected time frame.", ex);
                return null;
            }
            catch (Exception ex)
            {
                logger.WriteError($"The request to Luma API {updateUserApiUrl} failed. Exception message: {ex.Message}.", ex);
                return null;
            }

            return createdUser;
        }

        private string[] GetUserProvisioning(ICollection<int> roleIds)
        {
            var provisioning = new List<string>();
            var rolePermissions = rolesBo.GetAllRolePermissions()
                .Where(permission => roleIds.Contains(permission.RoleId))
                .ToList();

            if (rolePermissions.Any(permission =>
                    EnumHelper.AreEqual(permission.PermissionContext.Context, PermissionContext.CollaborationHub) &&
                    ((Permissions)permission.PermissionFlags).HasFlag(Permissions.Update)))
            {
                provisioning.Add("chat");
            }

            if (rolePermissions.Any(permission =>
                    EnumHelper.AreEqual(permission.PermissionContext.Context, PermissionContext.Broadcast) &&
                    ((Permissions)permission.PermissionFlags).HasFlag(Permissions.Update)))
            {
                provisioning.Add("broadcast");
            }

            if (rolePermissions.Any(permission =>
                    EnumHelper.AreEqual(permission.PermissionContext.Context, PermissionContext.PatientRecalls) &&
                    ((Permissions)permission.PermissionFlags).HasFlag(Permissions.Read)))
            {
                provisioning.Add("recall");
            }

            return provisioning.ToArray();
        }
        private async Task<LumaUser> GetLumaUserByEmail(CancellationTokenSource cancellationTokenSource, string userEmail, string selectProperties = null)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {

                var clientSubscription = clientSubscriptionBO.GetClientSubscription();
                var accountNumber = clientSubscription.PracticeId;

                var lumaUserApiUrl = $"{baseUrl}/{GetUserByEmailUrl(CreateLumaUserEmail(userEmail,accountNumber))}";
                if (!string.IsNullOrEmpty(selectProperties))
                {
                    lumaUserApiUrl += $"&_select={selectProperties}";
                }
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetLumaUserByEmail: API access token unavailable. Returning for email: {userEmail}.");
                    return null;
                }
                try
                {
                    var users = await apiHelper.HttpGetAsync<LumaListResponse<LumaUser>>(
                        lumaUserApiUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );
                    if (users.Response.Count > 0)
                    {
                        return users.Response?.FirstOrDefault();
                    }
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {lumaUserApiUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {lumaUserApiUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }

                lumaUserApiUrl = $"{baseUrl}/{GetUserByEmailUrl(userEmail)}";
                try
                {
                    var users = await apiHelper.HttpGetAsync<LumaListResponse<LumaUser>>(
                        lumaUserApiUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );
                    return users.Response?.FirstOrDefault();
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {lumaUserApiUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {lumaUserApiUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public async Task<bool> ValidateGroup(CreateEditLumaGroupDTO group)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var groupsList = await GetGroupsList();
                return groupsList.Response.All(g => g.Name != group.Name);
            }
            return false;
        }

        public async Task<LumaGroup> CreateEditLumaGroup(CreateEditLumaGroupDTO group, bool isEdit)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var createGroupUrl = $"{baseUrl}/{GetGroupsListUrl()}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"CreateEditLumaGroup: API access token unavailable. Returning.");
                    return null;
                }

                try
                {
                    var facilitiesResponse = await GetLumaFacilities();
                    var existingFacilityIds = facilitiesResponse.Response
                        .Where(f => f.ExternalRawSource != null && f.ExternalRawSource.Id != null)
                        .ToDictionary(f => f.ExternalRawSource.Id, f => f.Id);

                    var validFacilityIds = group.Facilities.Select(f => f.ToString()).Where(facilityId => existingFacilityIds.ContainsKey(facilityId)).Select(facilityId => existingFacilityIds[facilityId]).ToArray();

                    var createGroupRequest = new
                    {
                        name = group.Name,
                        facilities = validFacilityIds
                    };

                    LumaGroup response;

                    if (isEdit)
                    {
                        var editGroupUrl = $"{baseUrl}/{GetGroupsListUrl()}/{group.Id}";
                        response = await apiHelper.HttpPutAsync<object, LumaGroup>(
                           createGroupRequest,
                           editGroupUrl,
                           "application/json",
                           $"{apiAccessToken.Type} {apiAccessToken.Value}",
                           cancellationTokenSource.Token);
                    }
                    else
                    {
                        response = await apiHelper.HttpPostAsync<object, LumaGroup>(
                            createGroupRequest,
                            createGroupUrl,
                            "application/json",
                            $"{apiAccessToken.Type} {apiAccessToken.Value}",
                            cancellationTokenSource.Token);
                    }

                    return response;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {createGroupUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {createGroupUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<LumaListResponse<LumaFacility>> GetLumaFacilities()
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var facilitiesListUrl = $"{baseUrl}/{GetFacilitiesListUrl()}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetLumaFacilities: API access token unavailable. Returning null.");
                    return null;
                }
                try
                {
                    var lumaFacilitiesList = await apiHelper.HttpGetAsync<LumaListResponse<LumaFacility>>(
                        facilitiesListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return lumaFacilitiesList;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {facilitiesListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {facilitiesListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<LumaFacility> GetLumaFacility(int facilityId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var facilitiesListUrl = $"{baseUrl}/{GetFacilitiesListUrl()}?externalId.value={facilityId}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetLumaFacility: API access token unavailable. Returning null for facilityId {facilityId}.");
                    return null;
                }
                try
                {
                    var lumaFacilitiesList = await apiHelper.HttpGetAsync<LumaListResponse<LumaFacility>>(
                        facilitiesListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    if (lumaFacilitiesList.Response.Count == 0)
                    {
                        return null;
                    }

                    return lumaFacilitiesList.Response.FirstOrDefault();
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {facilitiesListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {facilitiesListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<LumaListResponse<LumaGroup>> GetGroupsList()
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var groupsListUrl = $"{baseUrl}/{GetGroupsListUrl()}";
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

                    return new LumaListResponse<LumaGroup>
                    {
                        Response = lumaGroupsList.Response
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

        public async Task<CreateEditLumaGroupDTO> GetGroup(string id)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var groupsListUrl = $"{baseUrl}/{GetGroupsListUrl()}/{id}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetGroup: API access token unavailable. Returning.");
                    return null;
                }
                try
                {
                    var lumaGroup = await apiHelper.HttpGetAsync<LumaGroup>(
                        groupsListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    var groupResponse = new CreateEditLumaGroupDTO()
                    {
                        Name = lumaGroup.Name,
                        Facilities = await GetLFacilitiesExternalIds(lumaGroup.Facilities)
                    };

                    return groupResponse;
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

        private async Task<int[]> GetLFacilitiesExternalIds(ICollection<string> facilitiesIds)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                using (var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds))
                {
                    try
                    {
                        var facilities = await GetLumaFacilities();

                        var matchingExternalIds = facilities.Response
                            .Where(facility => facilitiesIds.Contains(facility.Id))
                            .Select(facility => int.Parse(facility.ExternalRawSource.Id))
                            .ToArray();

                        return matchingExternalIds;
                    }
                    catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                    {
                        logger.WriteError($"The request to Luma API has timed out. The server did not respond within the expected time frame.", ex);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.WriteError($"The request to Luma API failed. Exception message: {ex.Message}.", ex);
                        throw;
                    }
                }
            }
            return null;
        }

        private async Task<AccessTokenResponse> GetUserAccessToken(CancellationTokenSource cancellationTokenSource, string lumaUserId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                if (!cacheProvider.Exists(GetUserAccessTokenCacheKey(lumaUserId)))
                {
                    double expirationTimeSeconds;
                    var userAccessTokenUrl = $"{baseUrl}/{GetAccessTokenEndpoint()}/{lumaUserId}";
                    AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                    // If we couldn't obtain an API access token, abort and return null
                    if (apiAccessToken == null || apiAccessToken.Value == "-1")
                    {
                        logger.WriteInfo($"GetUserAccessToken: API access token unavailable.");
                        return null;
                    }
                    try
                    {
                        var response = await apiHelper.HttpPutAsync<object, AccessTokenResponse>(
                            new { },
                            userAccessTokenUrl,
                            "application/json",
                            $"{apiAccessToken.Type} {apiAccessToken.Value}",
                            cancellationTokenSource.Token);
                        if (double.TryParse(response.ExpiresIn, out expirationTimeSeconds))
                        {
                            expirationTimeSeconds = expirationTimeSeconds - 600 < defaultTokenExpirationTimeSeconds ? defaultTokenExpirationTimeSeconds : expirationTimeSeconds - 600;
                        }
                        else
                        {
                            expirationTimeSeconds = defaultTokenExpirationTimeSeconds;
                        }
                        cacheProvider.Set(GetUserAccessTokenCacheKey(lumaUserId), response, TimeSpan.FromSeconds(expirationTimeSeconds));
                        return response;
                    }
                    catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                    {
                        logger.WriteError($"The request to Luma API {userAccessTokenUrl} has timed out. The server did not respond within the expected time frame.", ex);
                        return null;
                    }
                    catch (Exception ex)
                    {
                        logger.WriteError($"The request to Luma API {userAccessTokenUrl} failed. Exception message: {ex.Message}.", ex);
                        return null;
                    }
                }
                else
                {
                    return cacheProvider.Get<AccessTokenResponse>(GetUserAccessTokenCacheKey(lumaUserId));
                }
            }
            return null;
        }

        private async Task<AccessTokenResponse> GetApiAccessToken(CancellationTokenSource cancellationTokenSource)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var clientSubscription = clientSubscriptionBO.GetClientSubscription();
                var accountNumber = clientSubscription.PracticeId;
                if (!cacheProvider.Exists(GetApiAccessTokenCacheKey(accountNumber)))
                {
                    try
                    {
                        var apiAccessTokenResponse = (dynamic)await practicePlusAdminClient.GetNextechCommunicationsToken(accountNumber);
                        var accessToken = new AccessTokenResponse
                        {
                            Type = apiAccessTokenResponse.tokenType,
                            Value = apiAccessTokenResponse.accessToken
                        };
                        cacheProvider.Set(GetApiAccessTokenCacheKey(accountNumber), accessToken, TimeSpan.FromSeconds(defaultTokenExpirationTimeSeconds));
                        return accessToken;
                    }
                    catch (Exception ex)
                    {
                        logger.WriteError($"The request to get api access token failed. Exception message: {ex.Message}. Returning fallback access token value '-1'.", ex);
                        // Return a sentinel token indicating failure to acquire a real token.
                        var accessToken = new AccessTokenResponse
                        {
                            Type = string.Empty,
                            Value = "-1"
                        };
                        cacheProvider.Set(GetApiAccessTokenCacheKey(accountNumber), accessToken, TimeSpan.FromSeconds(defaultTokenExpirationTimeSeconds));
                        cancellationTokenSource.Cancel();
                        return accessToken;
                    }
                }
                else
                {
                    return cacheProvider.Get<AccessTokenResponse>(GetApiAccessTokenCacheKey(accountNumber));
                }
            }
            return null;
        }

        private async Task<LumaUser> GetLumaUserByPPlusId(int userId, CancellationTokenSource cancellationTokenSource)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                try
                {
                    var user = usersBo.GetUserById(userId) ?? throw new ArgumentNullException($"Practice Plus user not found, user id: {userId}");
                    user.Roles = usersBo.GetUserRolesByUserId(userId);
                    var lumaUser = await GetLumaUserByEmail(cancellationTokenSource, user.Email);
                    if (lumaUser == null)
                    {
                        // If we can't find the user with the original email, try checking with a new email
                        lumaUser = await CreateLumaUser(user, user.Roles?.Select(r => r.RoleId).ToList(), cancellationTokenSource);
                        if (lumaUser == null)
                        {
                            logger.WriteInfo($"The request to Luma API to get the current user failed.");
                            return null;
                        }
                        logger.WriteInfo($"Luma user created successfully, email: {CreateLumaUserEmail(user.Email, user.AccountNumber)}.");
                    }
                    return lumaUser;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError("The request to Luma API to get the current user has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API to get the current user failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<LumaUser> GetLumaMasterUser(CancellationTokenSource cancellationTokenSource)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var getMasterUserUrl = $"{baseUrl}/{GetMasterUserUrl()}";
                var apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetLumaMasterUser: API access token unavailable.");
                    return null;
                }
                try
                {
                    var masterUser = await apiHelper.HttpGetAsync<LumaListResponse<LumaUser>>(
                        getMasterUserUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );
                    return masterUser.Response?.FirstOrDefault();
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {getMasterUserUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {getMasterUserUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public async Task<List<string>> GetUserGroups(string email)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                try
                {
                    var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                    var user = await GetLumaUserByEmail(cancellationTokenSource, email, "groups");
                    if (user == null)
                    {
                        return new List<string>();
                    }
                    return user.Groups;
                }
                catch
                {
                    return new List<string>();
                }
            }
            return null;
        }

        public async Task<List<LumaMessage>> GetCommunicationHistory(int patientId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var lumaPatient = await GetPatient(patientId);

                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var communicationHistoryUrl = $"{baseUrl}/{GetCommunicationHistoryUrl()}?patient={lumaPatient._id}&visibility=public";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"GetCommunicationHistory: API access token unavailable.");
                    return null;
                }
                try
                {
                    var lumaMessagesResponse = await apiHelper.HttpGetAsync<LumaListResponse<LumaMessage>>(
                        communicationHistoryUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);
                    return lumaMessagesResponse.Response;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {communicationHistoryUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {communicationHistoryUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        public async Task<bool> NextechCommunicationsEnabledProvidersExist()
        {
            try
            {
                var clientSubscription = clientSubscriptionBO.GetClientSubscription();
                var accountNumber = clientSubscription.PracticeId;
                var cacheKey = $"NextechCommunicationsEnabledProviders-{accountNumber}";
                List<string> enabledProviders;

                enabledProviders = cacheProvider.Get<List<string>>(cacheKey);

                if (enabledProviders == null)
                {
                    enabledProviders = (await practiceSettingBo.GetNextechCommunicationsEnabledProviders(accountNumber))
                        .ToList();

                    cacheProvider.SetAbsoluteExpiration(cacheKey, enabledProviders, TimeSpan.FromMinutes(ClientSubscriptionCacheDurationMinutes));
                }

                return enabledProviders.Any();
            }
            catch (Exception ex)
            {
                logger.WriteError($"The request to get nextech communications enabled providers failed. Exception message: {ex.Message}.", ex);
                throw;
            }
        }



        public bool NextechCommunicationsEnabledProvidersExistSync()
        {
            try
            {
                return AsyncHelper.RunSync(NextechCommunicationsEnabledProvidersExist);
            }
            catch (Exception ex)
            {
                logger.WriteError($"The request to get nextech communications enabled providers failed. Exception message: {ex.Message}.", ex);
                throw;
            }
        }

        public async Task<bool> DeleteRecallConfig(string id)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var recallTemplateDeleteUrl = $"{baseUrl}/{GetFollowUpsUrl()}/{HttpUtility.UrlEncode(id)}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"DeleteRecallConfig: API access token unavailable.");
                    return false;
                }
                try
                {
                    _ = await apiHelper.HttpDeleteAsync<LumaRecallConfig>(
                        recallTemplateDeleteUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token);

                    return true;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {recallTemplateDeleteUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {recallTemplateDeleteUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return false;
        }

        public async Task SetLumaRecallStatus(string lumaRecallId, string status)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                logger.WriteInfo($"Entered SetLumaRecallStatus: Setting lumaRecallId: {lumaRecallId} to status: {status}");

                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var recallListUrl = $"{baseUrl}/{GetRecallsListUrl()}/{lumaRecallId}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"SetLumaRecallStatus: API access token unavailable.");
                    return;
                }

                dynamic lumaRecall = new ExpandoObject();
                lumaRecall.status = status;

                try
                {
                    await apiHelper.HttpPutAsync<object, LumaPatient>(
                        lumaRecall,
                        recallListUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {recallListUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {recallListUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
        }

        public async Task<bool> DeleteLumaPatientRecall(string lumaPatientRecallId)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var patientRecallUrl = $"{baseUrl}/{GetRecallsListUrl()}/{lumaPatientRecallId}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"DeleteLumaPatientRecall: API access token unavailable.");
                    return false;
                }

                try
                {
                    await apiHelper.HttpDeleteAsync<LumaRecall>(
                        patientRecallUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );
                    return true;
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {patientRecallUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    return false;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {patientRecallUrl} failed. Exception message: {ex.Message}.", ex);
                    return false;
                }
            }
            return false;
        }

        public async Task<LumaRecall> UpdatePatientRecall(PatientRecall patientRecall)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var cancellationTokenSource = new CancellationTokenSource((int)TimeSpan.FromMinutes(requestTimeoutMins).TotalMilliseconds);
                var patientRecallUrl = $"{baseUrl}/{GetRecallsListUrl()}/{patientRecall.LumaRecallId}";
                AccessTokenResponse apiAccessToken = await GetApiAccessToken(cancellationTokenSource);
                if (apiAccessToken == null || apiAccessToken.Value == "-1")
                {
                    logger.WriteInfo($"UpdatePatientRecall: API access token unavailable.");
                    return null;
                }

                // Setting dueDate time to client's noon and converting it to utc to send to Luma.
                var dueDate = patientRecall.DueDate.Value;
                var utcDueDate = TimeZoneInfo.ConvertTimeToUtc(new DateTime(dueDate.Year, dueDate.Month, dueDate.Day, 12, 0, 0, DateTimeKind.Unspecified), clientSubscriptionBO.GetClientTimeZone());

                var lumaRecall = await CreateRecallPayload(patientRecall.PatientId, patientRecall.FacilityId, patientRecall.AppointmentResourceId, patientRecall.AppointmentTypeId, utcDueDate);

                try
                {
                    return await apiHelper.HttpPutAsync<object, LumaRecall>(
                        lumaRecall,
                        patientRecallUrl,
                        "application/json",
                        $"{apiAccessToken.Type} {apiAccessToken.Value}",
                        cancellationTokenSource.Token
                    );
                }
                catch (TaskCanceledException ex) when (cancellationTokenSource.IsCancellationRequested)
                {
                    logger.WriteError($"The request to Luma API {patientRecallUrl} has timed out. The server did not respond within the expected time frame.", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.WriteError($"The request to Luma API {patientRecallUrl} failed. Exception message: {ex.Message}.", ex);
                    throw;
                }
            }
            return null;
        }

        private async Task<DynamicJsonObject> CreateRecallPayload(int patientId, int facilityId, int appointmentResourceId, int? appointmentTypeId, DateTime utcDueDate, string status = null, string source = null)
        {
            if (await NextechCommunicationsEnabledProvidersExist())
            {
                var patient = await GetPatient(patientId) ?? throw new LumaIntegrationException("Luma patient recall failed to be updated. Reason: Patient not found.");
                var facility = await GetLumaFacility(facilityId) ?? throw new LumaIntegrationException("Luma patient recall failed to be updated. Reason: Facility not found.");
                var provider = await GetLumaProviderByResourceId(appointmentResourceId) ?? throw new LumaIntegrationException("Luma patient recall failed to be updated. Reason: Provider not found.");
                var appointmentType = await GetLumaAppointmentType(appointmentTypeId) ?? throw new LumaIntegrationException("Luma patient recall failed to be updated. Reason: Appointment type not found.");

                dynamic recall = new ExpandoObject();
                recall.patient = patient._id;
                recall.provider = provider.Id;
                recall.facility = facility.Id;
                recall.type = appointmentType.Id;
                recall.date = utcDueDate.ToString("yyyy-MM-dd HH:mm:ss");

                if (!string.IsNullOrEmpty(status))
                {
                    recall.status = status;
                }
                if (!string.IsNullOrEmpty(source))
                {
                    recall.source = source;
                }

                return new DynamicJsonObject(recall);
            }
            return null;
        }
    }
}
