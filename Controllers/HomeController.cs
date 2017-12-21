using System;
using System.Linq;
using pbiApp.Models;
using Microsoft.Rest;
using System.Diagnostics;
using System.Configuration;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.PowerBI.Api.V2;
using System.Collections.Generic;
using Microsoft.PowerBI.Api.V2.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Security;
using System.Net.Http;
using System.IO;
using Newtonsoft.Json;

namespace pbiApp.Controllers
{
    public class AAD
    {
        public string token_type { get; set; }
        public string scope { get; set; }
        public string expires_in { get; set; }
        public string ext_expires_in { get; set; }
        public string expires_on { get; set; }
        public string not_before { get; set; }
        public string resource { get; set; }
        public string access_token { get; set; }
        public string refresh_token { get; set; }
    }

    public class HomeController : Controller
    {
        private IConfiguration _configuration;
        
        // TODO: don't hardcode these, read from app settings value
        private readonly string Username;
        private readonly string Password;
        private readonly string AuthorityUrl;
        private readonly string ResourceUrl;
        private readonly string ClientId;
        private readonly string ApiUrl;
        private readonly string GroupId;
        private readonly string ReportId;

        public HomeController(IConfiguration config){
            _configuration = config;

            // Setup Config values
            Username = _configuration["PowerBI:Username"];
            Password = _configuration["PowerBI:Password"];
            AuthorityUrl = _configuration["PowerBI:AuthorityUrl"];
            ResourceUrl = _configuration["PowerBI:ResourceUrl"];            
            ClientId = _configuration["PowerBI:ClientId"];
            ApiUrl = _configuration["PowerBI:ApiUrl"];
            GroupId = _configuration["PowerBI:GroupId"];
            ReportId = _configuration["PowerBI:ReportId"];
        }

        public async Task<IActionResult> Index()
        {
            Username = config.GetSection("pbiUsername").Value;
            Password = config.GetSection("pbiPassword").Value;
            AuthorityUrl = config.GetSection("authorityUrl").Value;
            ResourceUrl = config.GetSection("resourceUrl").Value;
            ClientId = config.GetSection("clientId").Value;
            ApiUrl = config.GetSection("apiUrl").Value;
            GroupId = config.GetSection("groupId").Value;
            ReportId = config.GetSection("reportId").Value;
        }

        public async Task<IActionResult> Index()
        {
            // TODO: add this dynamically from user logged in
            String username = null;
            String roles = null;

            var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");
            Configuration = builder.Build();

            populateCredentials(Configuration);

            var result = new EmbedConfig();
            try
            {
                result = new EmbedConfig();

                //// Inspired from https://stackoverflow.com/questions/45480532/embed-power-bi-report-in-asp-net-core-website

                var content = new Dictionary<string, string>();
                content["grant_type"] = "password";
                content["resource"] = ResourceUrl;
                content["username"] = Username;
                content["password"] = Password;
                content["client_id"] = ClientId;
                var httpClient = new HttpClient
                {
                    Timeout = new TimeSpan(0, 5, 0),
                    BaseAddress = new Uri(AuthorityUrl)
                };

                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type: application/x-www-form-urlencoded", "application/json");

                if (content == null)
                {
                    content = new Dictionary<string, string>();
                }

                var encodedContent = new FormUrlEncodedContent(content);

                var authResponse = await httpClient.PostAsync(httpClient.BaseAddress, encodedContent);
                var AAD = JsonConvert.DeserializeObject<AAD>(authResponse.Content.ReadAsStringAsync().Result);

                var tokenCredentials = new TokenCredentials(AAD.access_token, "Bearer");

                // Create a Power BI Client object. It will be used to call Power BI APIs.
                using (var client = new PowerBIClient(new Uri(ApiUrl), tokenCredentials))
                {
                    // Get a list of reports.
                    var reports = await client.Reports.GetReportsInGroupAsync(GroupId);

                    Report report;
                    if (string.IsNullOrEmpty(ReportId))
                    {
                        // Get the first report in the group.
                        report = reports.Value.FirstOrDefault();
                    }
                    else
                    {
                        report = reports.Value.FirstOrDefault(r => r.Id == ReportId);
                    }

                    if (report == null)
                    {
                        result.ErrorMessage = "Group has no reports.";
                        return View(result);
                    }

                    var datasets = await client.Datasets.GetDatasetByIdInGroupAsync(GroupId, report.DatasetId);
                    result.IsEffectiveIdentityRequired = datasets.IsEffectiveIdentityRequired;
                    result.IsEffectiveIdentityRolesRequired = datasets.IsEffectiveIdentityRolesRequired;
                    GenerateTokenRequest generateTokenRequestParameters;
                    // This is how you create embed token with effective identities
                    if (!string.IsNullOrEmpty(username))
                    {
                        var rls = new EffectiveIdentity(username, new List<string> { report.DatasetId });
                        if (!string.IsNullOrWhiteSpace(roles))
                        {
                            var rolesList = new List<string>();
                            rolesList.AddRange(roles.Split(','));
                            rls.Roles = rolesList;
                        }
                        // Generate Embed Token with effective identities.
                        generateTokenRequestParameters = new GenerateTokenRequest(accessLevel: "view", identities: new List<EffectiveIdentity> { rls });
                    }
                    else
                    {
                        // Generate Embed Token for reports without effective identities.
                        generateTokenRequestParameters = new GenerateTokenRequest(accessLevel: "view");
                    }

                    var tokenResponse = await client.Reports.GenerateTokenInGroupAsync(GroupId, report.Id, generateTokenRequestParameters);

                    if (tokenResponse == null)
                    {
                        result.ErrorMessage = "Failed to generate embed token.";
                        return View(result);
                    }

                    // Generate Embed Configuration.
                    result.EmbedToken = tokenResponse;
                    result.EmbedUrl = report.EmbedUrl;
                    result.Id = report.Id;

                    return View(result);
                }
            }
            catch (HttpOperationException exc)
            {
                result.ErrorMessage = string.Format("Status: {0} ({1})\r\nResponse: {2}\r\nRequestId: {3}", exc.Response.StatusCode, (int)exc.Response.StatusCode, exc.Response.Content, exc.Response.Headers["RequestId"].FirstOrDefault());
            }
            catch (Exception exc)
            {
                result.ErrorMessage = exc.ToString();
            }

            return View(result);
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
