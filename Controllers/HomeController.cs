﻿using System;
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

namespace pbiApp.Controllers
{
    [Authorize]
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
            // TODO: add this dynamically from user logged in
            String username = null;
            String roles = null;

            var result = new EmbedConfig();
            try
            {
                result = new EmbedConfig();
                // Create a user password cradentials.
                var credential = new UserPasswordCredential(Username, Password);

                // Authenticate using created credentials
                var authenticationContext = new AuthenticationContext(AuthorityUrl);
                var authenticationResult = await authenticationContext.AcquireTokenAsync(ResourceUrl, ClientId, credential);

                if (authenticationResult == null)
                {
                    result.ErrorMessage = "Authentication Failed.";
                    return View(result);
                }

                var tokenCredentials = new TokenCredentials(authenticationResult.AccessToken, "Bearer");

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
