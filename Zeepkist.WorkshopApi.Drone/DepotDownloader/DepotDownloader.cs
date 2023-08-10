using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamKit2;

namespace DepotDownloader;

public class DepotDownloader
{
    internal static ILogger<DepotDownloader> Logger { get; private set; }

    public static void Initialize(ILogger<DepotDownloader> logger)
    {
        Logger = logger;
        DebugLog.Enabled = false;

        AccountSettingsStore.LoadFromFile("account.config");

        if (!InitializeSteam(null, null))
        {
            throw new InvalidOperationException("Unable to login to steam");
        }
    }

    public static void Dispose()
    {
        ContentDownloader.ShutdownSteam3();
    }

    public static async Task<int> Run(string incomingWorkshopId, string dir)
    {
        string[] args = new[]
        {
            "-app",
            "1440670",
            "-pubfile",
            incomingWorkshopId,
            "-dir",
            dir
        };

        #region Common Options

        if (HasParameter(args, "-debug"))
        {
            DebugLog.Enabled = true;
            DebugLog.AddListener((category, message) =>
            {
                Console.WriteLine("[{0}] {1}", category, message);
            });

            var httpEventListener = new HttpDiagnosticEventListener();

            DebugLog.WriteLine("DepotDownloader", "Version: {0}", Assembly.GetExecutingAssembly().GetName().Version);
            DebugLog.WriteLine("DepotDownloader", "Runtime: {0}", RuntimeInformation.FrameworkDescription);
        }

        var username = GetParameter<string>(args, "-username") ?? GetParameter<string>(args, "-user");
        var password = GetParameter<string>(args, "-password") ?? GetParameter<string>(args, "-pass");
        ContentDownloader.Config.RememberPassword = HasParameter(args, "-remember-password");
        ContentDownloader.Config.UseQrCode = HasParameter(args, "-qr");

        ContentDownloader.Config.DownloadManifestOnly = HasParameter(args, "-manifest-only");

        var cellId = GetParameter(args, "-cellid", -1);
        if (cellId == -1)
        {
            cellId = 0;
        }

        ContentDownloader.Config.CellID = cellId;

        var fileList = GetParameter<string>(args, "-filelist");

        if (fileList != null)
        {
            try
            {
                var fileListData = await File.ReadAllTextAsync(fileList);
                var files = fileListData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                ContentDownloader.Config.UsingFileList = true;
                ContentDownloader.Config.FilesToDownload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ContentDownloader.Config.FilesToDownloadRegex = new List<Regex>();

                foreach (var fileEntry in files)
                {
                    if (fileEntry.StartsWith("regex:"))
                    {
                        var rgx = new Regex(fileEntry.Substring(6), RegexOptions.Compiled | RegexOptions.IgnoreCase);
                        ContentDownloader.Config.FilesToDownloadRegex.Add(rgx);
                    }
                    else
                    {
                        ContentDownloader.Config.FilesToDownload.Add(fileEntry.Replace('\\', '/'));
                    }
                }

                Console.WriteLine("Using filelist: '{0}'.", fileList);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Warning: Unable to load filelist: {0}", ex);
            }
        }

        ContentDownloader.Config.InstallDirectory = GetParameter<string>(args, "-dir");

        ContentDownloader.Config.VerifyAll = HasParameter(args, "-verify-all") || HasParameter(args, "-verify_all") ||
                                             HasParameter(args, "-validate");
        ContentDownloader.Config.MaxServers = GetParameter(args, "-max-servers", 20);
        ContentDownloader.Config.MaxDownloads = GetParameter(args, "-max-downloads", 8);
        ContentDownloader.Config.MaxServers =
            Math.Max(ContentDownloader.Config.MaxServers, ContentDownloader.Config.MaxDownloads);
        ContentDownloader.Config.LoginID = HasParameter(args, "-loginid") ? GetParameter<uint>(args, "-loginid") : null;

        #endregion

        var appId = GetParameter(args, "-app", ContentDownloader.INVALID_APP_ID);
        if (appId == ContentDownloader.INVALID_APP_ID)
        {
            Logger.LogCritical("Error: -app not specified!");
            return 1;
        }

        var pubFile = GetParameter(args, "-pubfile", ContentDownloader.INVALID_MANIFEST_ID);
        var ugcId = GetParameter(args, "-ugc", ContentDownloader.INVALID_MANIFEST_ID);
        if (pubFile != ContentDownloader.INVALID_MANIFEST_ID)
        {
            #region Pubfile Downloading

            // if (InitializeSteam(username, password))
            // {
            try
            {
                await ContentDownloader.DownloadPubfileAsync(appId, pubFile).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is ContentDownloaderException
                || ex is OperationCanceledException)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }
            catch (Exception e)
            {
                Logger.LogCritical(e, "Download failed due to an unhandled exception");
                throw;
            }
            // finally
            // {
            // ContentDownloader.ShutdownSteam3();
            // }
            // }
            // else
            // {
            // Console.WriteLine("Error: InitializeSteam failed");
            // return 1;
            // }

            #endregion
        }

        return 0;
    }

    static bool InitializeSteam(string username, string password)
    {
        if (!ContentDownloader.Config.UseQrCode)
        {
            if (username != null && password == null && (!ContentDownloader.Config.RememberPassword ||
                                                         !AccountSettingsStore.Instance.LoginTokens.ContainsKey(
                                                             username)))
            {
                do
                {
                    Console.Write("Enter account password for \"{0}\": ", username);
                    if (Console.IsInputRedirected)
                    {
                        password = Console.ReadLine();
                    }
                    else
                    {
                        // Avoid console echoing of password
                        password = Util.ReadPassword();
                    }

                    Console.WriteLine();
                } while (string.Empty == password);
            }
            else if (username == null)
            {
                Console.WriteLine("No username given. Using anonymous account with dedicated server subscription.");
            }
        }

        return ContentDownloader.InitializeSteam3(username, password);
    }

    static int IndexOfParam(string[] args, string param)
    {
        for (var x = 0; x < args.Length; ++x)
        {
            if (args[x].Equals(param, StringComparison.OrdinalIgnoreCase))
                return x;
        }

        return -1;
    }

    static bool HasParameter(string[] args, string param)
    {
        return IndexOfParam(args, param) > -1;
    }

    static T GetParameter<T>(string[] args, string param, T defaultValue = default(T))
    {
        var index = IndexOfParam(args, param);

        if (index == -1 || index == (args.Length - 1))
            return defaultValue;

        var strParam = args[index + 1];

        var converter = TypeDescriptor.GetConverter(typeof(T));
        if (converter != null)
        {
            return (T)converter.ConvertFromString(strParam);
        }

        return default(T);
    }

    static List<T> GetParameterList<T>(string[] args, string param)
    {
        var list = new List<T>();
        var index = IndexOfParam(args, param);

        if (index == -1 || index == (args.Length - 1))
            return list;

        index++;

        while (index < args.Length)
        {
            var strParam = args[index];

            if (strParam[0] == '-') break;

            var converter = TypeDescriptor.GetConverter(typeof(T));
            if (converter != null)
            {
                list.Add((T)converter.ConvertFromString(strParam));
            }

            index++;
        }

        return list;
    }
}
