

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using GitHub.Runner.Common;
using GitHub.Runner.Sdk;
using GitHub.Services.Common;

namespace GitHub.Runner.Listener
{
    public sealed class InternetCheck : RunnerService, ICheckExtension
    {
        private string _logFile = null;

        public int Order => 10;

        public string CheckName => "Internet Connection";

        public string CheckDescription => "Make sure the actions runner have access to public internet.";

        public string CheckLog => _logFile;

        public string HelpLink => "https://github.com/actions/runner/docs/checks/internetconnection.md";

        public Type ExtensionType => typeof(ICheckExtension);

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _logFile = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Diag), StringUtil.Format("{0}_{1:yyyyMMdd-HHmmss}-utc.log", nameof(InternetCheck), DateTime.UtcNow));
        }

        // check runner access to api.github.com
        public async Task<bool> RunCheck(string url, string pat)
        {
            var result = true;
            var checkTasks = new List<Task<CheckResult>>();
            checkTasks.Add(CheckGitHubDns());
            checkTasks.Add(PingGitHub());
            checkTasks.Add(CheckHttpsRequests("https://api.github.com", "X-GitHub-Request-Id"));

            while (checkTasks.Count > 0)
            {
                var finishedCheckTask = await Task.WhenAny<CheckResult>(checkTasks);
                var finishedCheck = await finishedCheckTask;
                result = result && finishedCheck.Pass;
                await File.AppendAllLinesAsync(_logFile, finishedCheck.Logs);
                checkTasks.Remove(finishedCheckTask);
            }

            await Task.WhenAll(checkTasks);
            return result;
        }

        private async Task<CheckResult> CheckGitHubDns()
        {
            var result = new CheckResult();
            try
            {
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****                                                                                                       ****");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****     Try DNS lookup for api.github.com ");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****                                                                                                       ****");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                IPHostEntry host = await Dns.GetHostEntryAsync("api.github.com");
                foreach (var address in host.AddressList)
                {
                    result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Resolved DNS for api.github.com to '{address}'");
                }

                result.Pass = true;
            }
            catch (Exception ex)
            {
                result.Pass = false;
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Resolved DNS for api.github.com failed with error: {ex}");
            }

            return result;
        }

        private async Task<CheckResult> PingGitHub()
        {
            var result = new CheckResult();
            try
            {
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****                                                                                                       ****");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****     Try ping api.github.com ");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****                                                                                                       ****");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("api.github.com");
                    if (reply.Status == IPStatus.Success)
                    {
                        result.Pass = true;
                        result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Ping api.github.com ({reply.Address}) succeed within to '{reply.RoundtripTime} ms'");
                    }
                    else
                    {
                        result.Pass = false;
                        result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Ping api.github.com ({reply.Address}) failed with '{reply.Status}'");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Pass = false;
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Ping api.github.com failed with error: {ex}");
            }

            return result;
        }

        private async Task<CheckResult> CheckHttpsRequests(string url, string expectedHeader)
        {
            var result = new CheckResult();
            try
            {
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****                                                                                                       ****");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****     Send HTTPS Request to {url} ");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ****                                                                                                       ****");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                using (var _ = new HttpEventSourceListener(result.Logs))
                using (var httpClientHandler = HostContext.CreateHttpClientHandler())
                using (var httpClient = new HttpClient(httpClientHandler))
                {
                    httpClient.DefaultRequestHeaders.UserAgent.AddRange(HostContext.UserAgents);
                    var response = await httpClient.GetAsync(url);

                    result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Http status code: {response.StatusCode}");
                    result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Http response headers: {response.Headers}");

                    var responseContent = await response.Content.ReadAsStringAsync();
                    result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Http response body: {responseContent}");
                    if (response.IsSuccessStatusCode)
                    {
                        if (response.Headers.Contains(expectedHeader))
                        {
                            result.Pass = true;
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Http request 'GET' to {url} succeed");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
                        }
                        else
                        {
                            result.Pass = false;
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Http request 'GET' to {url} succeed but doesn't have expected HTTP Header.");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
                            result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
                        }
                    }
                    else
                    {
                        result.Pass = false;
                        result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                        result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Http request 'GET' to {url} failed with {response.StatusCode}");
                        result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                        result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
                        result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Pass = false;
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} Https request 'GET' to {url} failed with error: {ex}");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ***************************************************************************************************************");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
                result.Logs.Add($"{DateTime.UtcNow.ToString("O")} ");
            }

            return result;
        }
    }
}