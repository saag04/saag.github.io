using DocuSign.Constants;
using DocuSignPOC2.Models;
using Microsoft.Extensions.Caching.Memory;
using DocuSign.eSign.Client;
using static DocuSign.eSign.Client.Auth.OAuth;
using DocuSign;
using System.Runtime.InteropServices;
using System.Diagnostics;
using DocuSign.eSign.Model;
using static DocuSign.eSign.Client.Auth.OAuth.UserInfo;
using DocuSign.eSign.Api;

namespace DocuSignPOC2.Services.IESignAdmin
{
    public class ESignAdminService : IeSignAdminService
    {
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;
        private readonly DocuSignJWT _docuSignJWT;
        private readonly DocuSignClient _docuSignClient;
        private readonly DocuSign.eSign.Client.Auth.OAuth.UserInfo _userInfo;
        private readonly Account _account;
        private readonly ApiClient _eSignApiClient;
        private readonly DocuSign.Admin.Client.ApiClient _adminApiClient;


        public ESignAdminService(IMemoryCache cache, IConfiguration config)
        {
            _cache = cache;
            _config = config;
            _docuSignJWT = _config.GetRequiredSection("DocuSignJWT").Get<DocuSignJWT>();
            _docuSignClient = new DocuSignClient();
            _docuSignClient.SetOAuthBasePath(_docuSignJWT.AuthServer);
            _eSignApiClient = new ApiClient();
            _adminApiClient = new DocuSign.Admin.Client.ApiClient();
            FetchAdminToken();
            

        }
        public DocuSign.Admin.Api.AccountsApi AdminAccountsApi
        {
            get { return new DocuSign.Admin.Api.AccountsApi(_adminApiClient); }
        }
    
        public ApiClient ESignApiClient
        {
            get { return _eSignApiClient; }
        }

        public DocuSign.Admin.Client.ApiClient AdminApiClient
        {
            get { return _adminApiClient; }
        }


        public GroupsApi GroupsApi
        {
            get { return new GroupsApi(_eSignApiClient); }
        }
        public EnvelopesApi EnvelopesApi
        {
            get { return new EnvelopesApi(_docuSignClient); }
        }

        public DocuSignClient DocuSignClient
        {
            get { return _docuSignClient; }
        }

        public DocuSign.Admin.Api.UsersApi AdminUsersApi
        {
            get { return new DocuSign.Admin.Api.UsersApi(_adminApiClient); }
        }
        public UsersApi UsersApi
        {
            get { return new UsersApi(_eSignApiClient); }
        }
        public AccountsApi AccountsApi
        {
            get { return new AccountsApi(_eSignApiClient); }
        }
        public string ESignAdminOrganizationId
        {
            get => _cache.Get<string>("eSignAdminOrganizationId");
            private set => _cache.Set("eSignAdminOrganizationId", value);
        }
        public string ESignAdminAccountId
        {
            get => _cache.Get<string>("eSignAdminAccountId");
            private set => _cache.Set("eSignAdminAccountId", value);
        }

        public string ESignBaseUrl
        {
            get => _cache.Get<string>("ESignBaseUrl");
            private set => _cache.Set("ESignBaseUrl", value);
        }

        public string ESignAdminToken
        {
            get => FetchAdminToken();
        }

        private string FetchAdminToken()
        {
            string token = string.Empty;

            // if cache doesn't contain 
            // an entry called TOKEN
            // error handling mechanism is mandatory
            if (!_cache.TryGetValue("ESignAdminToken", out token))
            {
                var tokenmodel = this.GetTokenFromApi();

                // keep the value within cache for 
                // given amount of time
                // if value is not accessed within the expiry time
                // delete the entry from the cache
                var options = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(
                              TimeSpan.FromSeconds((double)tokenmodel.ExpiresIn));

                _cache.Set("ESignAdminToken", tokenmodel.Value, options);

                token = tokenmodel.Value;
                RefreshDocuSignClientsAndApiWithToken(token);
            }

            return token;
        }

        private Token? GetTokenFromApi()
        {
            try
            {
                var accessToken = AuthenticateWithJWT("Admin", _docuSignJWT.ClientId, _docuSignJWT.ImpersonatedUserID,
                                                             _docuSignJWT.AuthServer, _docuSignJWT.PrivateKeyFile);
                var token = new Token()
                {
                    Value = accessToken.access_token,
                    ExpiresIn = accessToken.expires_in
                };
                return token;
            }
            catch (ApiException apiExp)
            {
                // Consent for impersonation must be obtained to use JWT Grant
                if (apiExp.Message.Contains("consent_required"))
                {
                    // Caret needed for escaping & in windows URL
                    string caret = "";
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        caret = "^";
                    }

                    // build a URL to provide consent for this Integration Key and this userId
                    //string url = "https://" + docuSignConfiguration.AuthServer + "/oauth/auth?response_type=code" + caret + "&scope=impersonation%20signature" + caret +
                    //    "&client_id=" + docuSignConfiguration.ClientId + caret + "&redirect_uri=" + DevCenterPage;

                    string url = BuildConsentURL();
                    Console.WriteLine($"Consent is required - launching browser (URL is {url})");

                    // Start new browser window for login and consent to this app by DocuSign user
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = false });
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        Process.Start("xdg-open", url);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        Process.Start("open", url);
                    }                   
                }
                return null;
            }
        }

        private static OAuthToken AuthenticateWithJWT(string api, string clientId, string impersonatedUserId, string authServer, string privateKeyFile)
        {
            try
            {
                var docuSignClient = new DocuSignClient();
                var apiType = Enum.Parse<ExamplesAPIType>(api);
                var scopes = new List<string>
                {
                    "signature",
                    "impersonation",
                };
                if (apiType == ExamplesAPIType.Rooms)
                {
                    scopes.AddRange(new List<string>
                {
                    "dtr.rooms.read",
                    "dtr.rooms.write",
                    "dtr.documents.read",
                    "dtr.documents.write",
                    "dtr.profile.read",
                    "dtr.profile.write",
                    "dtr.company.read",
                    "dtr.company.write",
                    "room_forms",
                });
                }

                if (apiType == ExamplesAPIType.Click)
                {
                    scopes.AddRange(new List<string>
                {
                    "click.manage",
                    "click.send",
                });
                }

                if (apiType == ExamplesAPIType.Monitor)
                {
                    scopes.AddRange(new List<string>
                {
                    "signature",
                    "impersonation",
                });
                }

                if (apiType == ExamplesAPIType.Admin)
                {
                    scopes.AddRange(new List<string>
                {
                    "user_read",
                    "user_write",
                    "account_read",
                    "organization_read",
                    "group_read",
                    "permission_read",
                    "identity_provider_read",
                    "domain_read",
            });
                }
                var privateKey = DSHelper.ReadFileContent(DSHelper.PrepareFullPrivateKeyFilePath(privateKeyFile));
                return docuSignClient.RequestJWTUserToken(
                    clientId,
                    impersonatedUserId,
                    authServer,
                    DSHelper.ReadFileContent(DSHelper.PrepareFullPrivateKeyFilePath(privateKeyFile)),
                    1,
                    scopes);
            }
            catch (ApiException ex)
            {
                throw ex;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private string BuildConsentURL()
        {
            var scopes = "signature impersonation";
            var apiType = Enum.Parse<ExamplesAPIType>(_config["ExamplesAPI"]);
            if (apiType == ExamplesAPIType.Rooms)
            {
                scopes += " dtr.rooms.read dtr.rooms.write dtr.documents.read dtr.documents.write "
                + "dtr.profile.read dtr.profile.write dtr.company.read dtr.company.write room_forms";
            }
            else if (apiType == ExamplesAPIType.Click)
            {
                scopes += " click.manage click.send";
            }
            else if (apiType == ExamplesAPIType.Admin)
            {
                scopes += " user_read user_write organization_read account_read group_read permission_read identity_provider_read domain_read";
            }

            return _config["DocuSign:AuthorizationEndpoint"] + "?response_type=code" +
                "&scope=" + scopes +
                "&client_id=" + _config["DocuSignJWT:ClientId"] +
                "&redirect_uri=" + _config["DocuSign:AppUrl"] + "/ds/login?authType=JWT";
        }

        public void RefreshDocuSignClientsAndApiWithToken(string token)
        {
            var userInfo = _docuSignClient.GetUserInfo(token);
            var account = userInfo.Accounts.FirstOrDefault();
            this.ESignAdminOrganizationId = account.Organization.OrganizationId;
            
            this.ESignAdminAccountId = account.AccountId;
            this.ESignBaseUrl = account.BaseUri + "/restapi";
            _docuSignClient.Configuration.DefaultHeader.Add("Authorization", "Bearer " + ESignAdminToken);
            _eSignApiClient.Configuration.DefaultHeader.Add("Authorization", "Bearer " + ESignAdminToken);
            _eSignApiClient.SetBasePath(ESignBaseUrl);           
            _adminApiClient.SetBasePath(_docuSignJWT.AdminApiEndpoint);
            _adminApiClient.Configuration.DefaultHeader.Add("Authorization", "Bearer " + ESignAdminToken);
        }

        public Guid? GetOrganizationId()
        {
            Guid? organizationId = null;            
           
            var org = AdminAccountsApi.GetOrganizations().Organizations;
            if (org == null)
            {
                throw new DocuSign.Admin.Client.ApiException(0, "You must create an organization for this account to be able to use the DocuSign Admin API.");
            }
            else
            {
                organizationId = org.FirstOrDefault().Id;
            }
            return organizationId;
        }
    }
}
