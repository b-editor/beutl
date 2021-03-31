using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using OpenTK.Audio.OpenAL;

namespace BEditor.Models
{
    public class OpenALInstaller
    {
        public event EventHandler? StartInstall;
        public event EventHandler? Installed;
        public event AsyncCompletedEventHandler? DownloadCompleted;
        public event DownloadProgressChangedEventHandler? DownloadProgressChanged;

        public static bool IsInstalled()
        {
            try
            {
                AL.Get(ALGetFloat.DopplerFactor);

                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        public async Task Install()
        {
            //const string url = "https://www.openal.org/downloads/oalinst.zip";
            const string url = "file:///E:/yuuto/Downloads/oalinst.zip";
            StartInstall?.Invoke(this, EventArgs.Empty);

            using var client = new WebClient();

            var tmp = Path.GetTempFileName();
            client.DownloadFileCompleted += Client_DownloadFileCompleted;
            client.DownloadProgressChanged += Client_DownloadProgressChanged;

            await client.DownloadFileTaskAsync(url, tmp);

            await using (var stream = new FileStream(tmp, FileMode.Open))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var exe = zip.Entries.FirstOrDefault(i => Path.GetExtension(i.FullName) is ".exe");
                if (exe is not null)
                {
                    var exetmp = ".\\oalinstall.exe";
                    await using (var deststream = new FileStream(exetmp, FileMode.Create))
                    await using (var srcstream = exe.Open())
                    {
                        await srcstream.CopyToAsync(deststream);
                    }

                    var proc = Process.Start(new ProcessStartInfo(Path.GetFullPath(exetmp))
                    {
                        Verb = "RunAs",
                        UseShellExecute = true
                    });

                    await proc!.WaitForExitAsync();
                }
            }

            File.Delete(tmp);

            Installed?.Invoke(this, EventArgs.Empty);
            client.DownloadFileCompleted -= Client_DownloadFileCompleted;
            client.DownloadProgressChanged -= Client_DownloadProgressChanged;
        }

        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadProgressChanged?.Invoke(sender, e);
        }

        private void Client_DownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            DownloadCompleted?.Invoke(sender, e);
        }
    }
}
