using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Drawing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System.Diagnostics;
using System;
using System.IO;
using System.Net.Http.Headers;


namespace DancorSVG2PNG
{
    public static class DancorSVG2PNG
    {
        [FunctionName("DancorSVG2PNG")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, 
            "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // parse query parameter
            string svgURL = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "l", true) == 0)
                .Value;

            if (svgURL == null)
            {
                // Get request body
                dynamic data = req.Content.ReadAsAsync<object>();
                svgURL = data?.svgURL;
            }

            // download file from URL
            var uniqueName = GenerateId() ;
            Directory.CreateDirectory(Path.GetTempPath() + "\\" + uniqueName);
            try {
                using (var client = new WebClient())
                {
                    client.DownloadFile(svgURL, Path.GetTempPath() + uniqueName + "\\" + uniqueName + ".svg");
                }
            }
            catch (Exception e)
            {
                log.Info("Download Fail");
                log.Info(e.Message);
                //return new BadRequestResult();
            }
            
            Process proc = new Process();
            try
            {
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.FileName = Environment.GetEnvironmentVariable("JAVA_HOME") + "java.exe";
                proc.StartInfo.Arguments = "-jar " + context.FunctionAppDirectory + "\\Batik\\batik-rasterizer.jar -d " + Path.GetTempPath() + uniqueName + "\\" + uniqueName + ".png " + Path.GetTempPath() + uniqueName + "\\" + uniqueName + ".svg";
                proc.Start();
                proc.WaitForExit();
                if (proc.HasExited)
                    log.Info(proc.StandardOutput.ReadToEnd());
                log.Info("Batik Success!");
            }
            catch (Exception e)
            {
                log.Info("Batik Fail");
                log.Info(e.Message);
                // damn, no luck, better at least get rid of those images
                cleanup(uniqueName);
                //return new BadRequestResult();
            }

            try
            {
                log.Info(Path.GetTempPath() + uniqueName + ".png");
                ////get Blob reference
                Image imageIn = Image.FromFile(Path.GetTempPath() + uniqueName + "\\" +  uniqueName + ".png");

                //cleanup(uniqueName);

                var result = new HttpResponseMessage(HttpStatusCode.OK);
                result.Content = new ByteArrayContent(ImageToByteArray(imageIn));
                result.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                
                return result;

            }
            catch (Exception e)
            {
                log.Info("Image Upload Fail");
                log.Info(e.Message);
                cleanup(uniqueName);
                //return new BadRequestResult();
            }

            return req.CreateResponse(HttpStatusCode.BadRequest, "Aww man, something went wrong!");
        }

        private static string GenerateId()
        {
            long i = 1;
            foreach (byte b in Guid.NewGuid().ToByteArray())
            {
                i *= ((int)b + 1);
            }
            return string.Format("{0:x}", i - DateTime.Now.Ticks);
        }

        private static byte[] ImageToByteArray(Image image)
        {
            ImageConverter converter = new ImageConverter();
            return (byte[])converter.ConvertTo(image, typeof(byte[]));
        }

        private static void cleanup(string uniqueName)
        {
            // clean up
            if (File.Exists(Path.GetTempPath() + "\\" + uniqueName + "\\" + uniqueName + ".png"))
                File.Delete(Path.GetTempPath() + "\\" + uniqueName + "\\" + uniqueName + ".png");
            if (File.Exists(Path.GetTempPath() + "\\" + uniqueName + "\\" + uniqueName + ".svg"))
                File.Delete(Path.GetTempPath() + "\\" + uniqueName + "\\" + uniqueName + ".svg");
            if (Directory.Exists(Path.GetTempPath() + "\\" + uniqueName))
                Directory.Delete(Path.GetTempPath() + "\\" + uniqueName);
        }

        private static bool uploadImage(Image imageIn, string uniqueName)
        {
            try
            {

            // upload file to blob storage
            string storageConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
            //create a container CloudBlobContainer 
            CloudBlobContainer cloudBlobContainer = cloudBlobClient.GetContainerReference(Environment.GetEnvironmentVariable("AzureWebJobsContainer"));

            byte[] arr;
            using (MemoryStream ms = new MemoryStream())
            {
                imageIn.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                arr = ms.ToArray();
            }

            CloudBlockBlob svgBlob = cloudBlobContainer.GetBlockBlobReference(Path.GetTempPath() + uniqueName + "\\" + uniqueName + ".png");
            svgBlob.Properties.ContentType = "image/png";
            svgBlob.UploadFromByteArray(arr, 0, arr.Length);

                return true;
            }
            catch
            {
                return false;
            }
        }


    }
}
