using System;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using PnP.Core.Services;
using PnP.Core.Model.SharePoint;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace SimplifiedDelegatedRER
{
    public class TeamsHelper
    {
        // private readonly AzureFunctionSettings azureFunctionSettings;
        private readonly PnPContext _pnpContext;
        private readonly GraphServiceClient _graphClient;
        private readonly ILogger _log;
        public TeamsHelper(PnPContext hubContext, GraphServiceClient graphServiceClient, ILogger log)
        {
            this._pnpContext = hubContext;
            this._graphClient = graphServiceClient;
            this._log = log;
        }

        public string CreateTeams(ProjectRequestInfo info)
        {

            _log.LogInformation("Team creation process started");
            string ProjectTitle, ProjectDescription, ProjectRequestor;

            //Reading data from SharePoint list
            GetProjectRequestDetails(info, out ProjectTitle, out ProjectDescription, out ProjectRequestor);

            //Creating Teams (This is step 1/3)
            info.TeamsId = NewTeams(ProjectTitle, ProjectDescription, ProjectRequestor);

            //Adding Team Members 
            AddTeamMembers(info);

            return info.TeamsId;

        }
        private void GetProjectRequestDetails(ProjectRequestInfo info, out string ProjectTitle, out string ProjectDescription, out string ProjectRequestor)
        {
            IList list = _pnpContext.Web.Lists.GetById(info.RequestListId);
            IListItem requestDetails = list.Items.GetById(info.RequestListItemId,
                    li => li.Title,
                    li => li.All);
            ProjectTitle = requestDetails.Title == null ? string.Empty : requestDetails.Title;
            ProjectDescription = requestDetails["Description"] == null ? string.Empty : requestDetails["Description"].ToString()!;
            ProjectRequestor = _pnpContext.Web.GetUserById(info.RequestorId).UserPrincipalName;
        }
        private string NewTeams(string ProjectTitle, string ProjectDescription, string ProjectRequestor)
        {
            //Required Permission: Microsoft Graph -> Team.Create
            var team = new Team
            {
                Visibility = TeamVisibilityType.Private,
                DisplayName = ProjectTitle,
                Description = ProjectDescription,
                AdditionalData = new Dictionary<string, object>() { { "template@odata.bind", "https://graph.microsoft.com/v1.0/teamsTemplates('standard')" } },
                Members = new TeamMembersCollectionPage()
                {
                    new AadUserConversationMember
                    {
                        Roles = new List<String>(){"owner"},
                        AdditionalData = new Dictionary<string, object>()
                        {
                            {"user@odata.bind", "https://graph.microsoft.com/v1.0/users('" + ProjectRequestor + "')"}
                        }
                    }
                },
            };
            var result = Task.Run(async () => await _graphClient.Teams.Request().AddResponseAsync(team));
            string newTeamId = "";
            if (result.Result.HttpHeaders.TryGetValues("Location", out var locationValues))
            {
                newTeamId = locationValues?.First().Split('\'')[1];
            }
            return newTeamId;
        }
        private async void AddTeamMembers(ProjectRequestInfo info)
        {
            var Members = new List<ConversationMember>();
            IList list = _pnpContext.Web.Lists.GetById(info.RequestListId);
            IListItem requestDetails = list.Items.GetById(info.RequestListItemId,
                    li => li.Title,
                    li => li.All);

            if (requestDetails["Owners"] != null)
            {
                //log.LogInformation("");
                foreach (IFieldUserValue user in (requestDetails["Owners"] as IFieldValueCollection)!.Values)
                {
                    // Get the stored user lookup id value
                    //requried Permission: Microsoft Graph -> User.Read.All
                    var upnUser = _pnpContext.Web.GetUserById(user.LookupId).UserPrincipalName;

                    var objUser = string.Format("https://graph.microsoft.com/v1.0/users('{0}')", upnUser);

                    var TeamUser = new AadUserConversationMember
                    {
                        Roles = new List<String>() { "owner" },
                        AdditionalData = new Dictionary<string, object>()
                               {{"user@odata.bind", objUser}}
                    };
                    Members.Add(TeamUser);
                }
            }
            if (requestDetails["Members"] != null)
            {
                foreach (IFieldUserValue user in (requestDetails["Members"] as IFieldValueCollection)!.Values)
                {
                    // Get the stored user lookup id value
                    //requried Permission: Microsoft Graph -> User.Read.All
                    var upnUser = _pnpContext.Web.GetUserById(user.LookupId).UserPrincipalName;
                    var objUser = string.Format("https://graph.microsoft.com/v1.0/users('{0}')", upnUser);

                    var TeamUser = new AadUserConversationMember
                    {
                        Roles = new List<String>() { },
                        AdditionalData = new Dictionary<string, object>()
                               {{"user@odata.bind", objUser}}
                    };
                    Members.Add(TeamUser);
                }
            }
            //Required Permissions:'TeamMember.ReadWrite.All'
            var response = await _graphClient.Teams[info.TeamsId].Members.Add(Members).Request().PostAsync();
        }
    }
}