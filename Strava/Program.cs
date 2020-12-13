using Newtonsoft.Json;
using Strava.Api;
using Strava.Authentication;
using Strava.Clients;
using Strava.Upload;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Strava
{
    class Program
    {
        static string ClientId { get; set; }
        static string ClientSecret { get; set; }
        static string FolderWithTcx { get; set; }

        //You can also provide it here, if you don't want to mess around with UI
        static string AuthCode = string.Empty;

        static void Main(string[] args)
        {
            #region settings

            if (!ReadClientSettings())
            {
                //prompt and save to file
                ClientId = ShowInputBox("Provide your Strava Client ID", "Configuration");
                ClientSecret = ShowInputBox("Provide your Strava Client Secret", "Configuration");
                FolderWithTcx = ShowInputBox("Provide path to folder with TCX files", "Configuration");

                if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
                {
                    Environment.Exit(0);
                }
                SetSetting(GetPropertyName(() => ClientId), ClientId);
                SetSetting(GetPropertyName(() => ClientSecret), ClientSecret);
                SetSetting(GetPropertyName(() => FolderWithTcx), FolderWithTcx);
            }
            Console.WriteLine("Configuration values - OK");
            #endregion

            #region authentication

            System.Diagnostics.Process.Start(
                $"https://www.strava.com/oauth/mobile/authorize?client_id={ClientId}&redirect_uri=http%3A%2F%2Flocalhost&response_type=code&approval_prompt=auto&scope=activity%3Awrite%2Cread&state=test");

            if (string.IsNullOrEmpty(AuthCode))
            {
                AuthCode = ShowInputBox("Provide your Strava Auth Code","Configuration");
            }

            #endregion

            string[] files = Directory.GetFiles(FolderWithTcx, "*.tcx");
            Console.WriteLine($"Found {files.Count()} TCX files to process.");

            HttpClient httpClient = new HttpClient();

            var values = new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "client_secret", ClientSecret },
                { "code", AuthCode },
                { "grant_type", "authorization_code" }
           };

            try
            {
                var content = new FormUrlEncodedContent(values);
                var response = httpClient.PostAsync("https://www.strava.com/api/v3/oauth/token", content).Result;
                var responseString = response.Content.ReadAsStringAsync().Result;
                dynamic objects = JsonConvert.DeserializeObject(responseString);
                string token = objects["access_token"];
                if (!string.IsNullOrEmpty(token))
                    Console.WriteLine("Authentication - OK");
                else Console.WriteLine("Authentication - FAILURE");

                StaticAuthentication auth = new StaticAuthentication(token);
                StravaClient client = new StravaClient(auth);

                Console.WriteLine("If you see nothing below for a longer period - try again after 15 minutes.");

                foreach (var item in files)
                {
                    var task =
                        Task.Run(async () =>
                        {
                            UploadStatus status = await client.Uploads.UploadActivityAsync(item, DataFormat.Tcx);
                            Thread.Sleep(2000);
                            UploadStatusCheck check = new UploadStatusCheck(token, status.Id.ToString());

                            check.UploadChecked += delegate (object o, UploadStatusCheckedEventArgs args2)
                            {
                                Console.WriteLine($"{args2.Status} {item} || Limit {Limits.Limit.ShortTerm} {Limits.Usage.ShortTerm}");
                                if (args2.Status == CurrentUploadStatus.Ready)
                                {
                                    File.Delete(item);
                                }
                            };

                            check.Start();
                        });

                    task.Wait();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
                Console.Read();
            }
        }

        private static bool ReadClientSettings()
        {
            ClientId = GetSetting(GetPropertyName(() => ClientId));
            ClientSecret = GetSetting(GetPropertyName(() => ClientSecret));
            FolderWithTcx = GetSetting(GetPropertyName(() => FolderWithTcx));

            if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret) || string.IsNullOrEmpty(FolderWithTcx))
                return false;

            return true;
        }

        private static string GetSetting(string key)
        {
            return ConfigurationManager.AppSettings[key];
        }

        private static void SetSetting(string key, string value)
        {
            Configuration configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            configuration.AppSettings.Settings[key].Value = value;
            configuration.Save(ConfigurationSaveMode.Full, true);
            ConfigurationManager.RefreshSection("appSettings");
        }

        private static string GetPropertyName(Expression<Func<string>> expr)
        {
            var member = expr.Body as MemberExpression;
            if (member == null)
                throw new InvalidOperationException("Expression is not a member access expression.");
            var property = member.Member as PropertyInfo;
            if (property == null)
                throw new InvalidOperationException("Member in expression is not a property.");
            return property.Name;
        }

        private static string ShowInputBox(string prompt, string title)
        {
            return Microsoft.VisualBasic.Interaction.InputBox(prompt,
                           title,
                           string.Empty,
                           0,
                           0);
        }
    }
}
