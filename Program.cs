using ceTe.DynamicPDF.Rasterizer;
using Newtonsoft.Json;
using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ZXing;
using System.Linq;
using System.Collections.Generic;


namespace SertCheck
{
    class Program
    {
        static string dir = "temp";
        static void Main(string[] args)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }

            Directory.CreateDirectory(dir);

            Program program = new Program();

            program.Run();
        }

        private void Run()
        {
            using (ApplicationContext db = new ApplicationContext())
            {
                var query = (from f in db.Files
                            join d in db.Documents on f.f_document equals d.id
                            where f.b_verify == false && f.ba_pdf != null && !f.sn_delete
                            select new { 
                                d.id,
                                d.c_first_name,
                                d.c_last_name,
                                d.c_middle_name,
                                d.d_birthday,
                                f_file = f.id
                            }).ToArray();

                foreach(var item in query)
                {
                    SertCheck.Models.File file = db.Files.Where(t => t.id == item.f_file).SingleOrDefault();
                    if (file != null)
                    {
                        try
                        {
                            byte[] bytes = file.ba_pdf;
                            using (InputPdf inputPdf = new InputPdf(bytes))
                            {
                                PdfRasterizer rasterizer = new PdfRasterizer(inputPdf, 1, 1);
                                rasterizer.Draw("temp/" + item.id + ".bmp", ImageFormat.Bmp, ImageSize.Dpi96);

                                var reader = new BarcodeReaderGeneric();

                                Bitmap image = (Bitmap)Image.FromFile("temp/" + item.id + ".bmp");

                                using (image)
                                {
                                    LuminanceSource source;
                                    source = new ZXing.BitmapLuminanceSource(image);
                                    //decode text from LuminanceSource
                                    Result result = reader.Decode(source);
                                    if (result != null && !string.IsNullOrEmpty(result.Text))
                                    {
                                        string url = getVerifyUrl(getKey(result.Text));
                                        using (HttpClient client = new HttpClient())
                                        {
                                            var data = StreamWithNewtonsoftJson(url, client).GetAwaiter().GetResult();
                                            if (data != null)
                                            {
                                                string birthdate = data.birthdate;
                                                string fio = data.fio;

                                                if (item.d_birthday.HasValue &&
                                                    birthdate == item.d_birthday.Value.ToString("dd.MM.yyyy"))
                                                {
                                                    if (getEncodeName(item.c_first_name) + " " + getEncodeName(item.c_last_name) + " " + getEncodeName(item.c_middle_name) == fio)
                                                    {
                                                        Log("Сертификат подтвержден");

                                                        file.b_verify = true;
                                                        file.c_gosuslugi_key = getKey(result.Text);
                                                        db.Update(file);
                                                        db.SaveChanges();
                                                        continue;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log("[ERR]: " + e.ToString());
                        }
                    }
                }
            }
        }

        private void Log(string txt)
        {
            Console.WriteLine(txt);
            string fileName = "log-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
            using (StreamWriter sw = File.AppendText(dir + "/" + fileName))
            {
                sw.WriteLine(txt);
            }
        }

        private string getEncodeName(string name)
        {
            string output = char.ToUpper(name[0]).ToString();
            for(int i = 1; i < name.Length; i++)
            {
                output += "*";
            }
            return output;
        }

        private async Task<dynamic> StreamWithNewtonsoftJson(string uri, HttpClient httpClient)
        {
            using var httpResponse = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

            httpResponse.EnsureSuccessStatusCode(); // throws if not 200-299

            if (httpResponse.Content is object 
                && httpResponse.Content.Headers.ContentType != null 
                && httpResponse.Content.Headers.ContentType.MediaType == "application/json")
            {
                var contentStream = await httpResponse.Content.ReadAsStreamAsync();

                using var streamReader = new StreamReader(contentStream);
                using var jsonReader = new JsonTextReader(streamReader);

                JsonSerializer serializer = new JsonSerializer();

                try
                {
                    return serializer.Deserialize(jsonReader);
                }
                catch (JsonReaderException)
                {
                    Log("[ERR]: " + "Invalid JSON.");
                }
            }
            else
            {
                Log("[ERR]: " + "HTTP Response was invalid and cannot be deserialised.");
            }

            return null;
        }

        private string getVerifyUrl(string key)
        {
            return "https://www.gosuslugi.ru/api/vaccine/v1/cert/verify/" + key;
        }

        private string getKey(string url)
        {
            int i = url.LastIndexOf("/");
            string key = url.Substring(i + 1, url.Length - 1 - i);
            return key;
        }
    }
}
