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
using System.Net.Mail;
using System.Net;
using System.Threading;

namespace SertCheck
{
    class Program
    {
        static string dir = "temp";
        static string ignore = "ignore.log";
        static string[] ignors = null;

        static void Main(string[] args)
        {

            if(File.Exists(dir + "/" + ignore))
            {
                ignors = File.ReadAllLines(dir + "/" + ignore);
            }

            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }

            Directory.CreateDirectory(dir);

            Program program = new Program();

            program.Run();

            if (args.Length > 0 && args[0] == "true")
            {
                program.SendReportMail();
            }
        }

        private void Run()
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            Log("processing: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));

            using (ApplicationContext db = new ApplicationContext())
            {
                var query = (from f in db.Files
                             join d in db.Documents on f.f_document equals d.id
                             join u in db.Users on d.f_user equals u.id
                             where !u.sn_delete && !u.b_disabled && !d.sn_delete && string.IsNullOrEmpty(f.c_gosuslugi_key) && f.c_type == "sert" && !f.sn_delete
                             orderby f.dx_created
                             select new { 
                                d.id,
                                d.c_first_name,
                                d.c_last_name,
                                d.c_middle_name,
                                d.d_birthday,
                                f_file = f.id
                            }).ToArray();
                Console.WriteLine(query.Count());

                int idx = 0;
                foreach(var item in query)
                {

                    Thread.Sleep(1000);

                    Log(idx + " [" + item.f_file + "]");

                    ID(item.f_file.ToString());

                    idx++;
                    SertCheck.Models.File file = db.Files.Where(t => t.id == item.f_file).SingleOrDefault();
                    if (file != null)
                    {
                        if (ignors != null && ignors.Count() > 0 && ignors.Last() == item.f_file.ToString())
                        {
                            file.c_notice = "Документ неподтвержден, как PDF-сертификат о вакцинации.";
                            file.c_gosuslugi_key = Guid.Empty.ToString();
                            db.Update(file);
                            db.SaveChanges();
                            continue;
                        }

                        try
                        {
                            byte[] bytes = file.ba_data;
                            using (InputPdf inputPdf = new InputPdf(bytes))
                            {
                                using (PdfRasterizer rasterizer = new PdfRasterizer(inputPdf, 1, 1))
                                {
                                    rasterizer.Draw("temp/" + item.id + ".bmp", ImageFormat.Bmp, ImageSize.Dpi96);

                                    var reader = new BarcodeReaderGeneric();
                                    reader.AutoRotate = true;

                                    Bitmap image = (Bitmap)Image.FromFile("temp/" + item.id + ".bmp");

                                    using (image)
                                    {
                                        LuminanceSource source = new ZXing.BitmapLuminanceSource(image);

                                        //decode text from LuminanceSource
                                        Result result = reader.Decode(source);

                                        if (result != null && !string.IsNullOrEmpty(result.Text))
                                        {
                                            using (HttpClient client = new HttpClient())
                                            {
                                                file.c_url = result.Text;

                                                string url = getVerifyUrl(getKey(result.Text));
                                                var data = StreamWithNewtonsoftJson(url, client).GetAwaiter().GetResult();

                                                /*if (data == null)
                                                {
                                                    url = getVerifyV2Url(getKey(result.Text));
                                                    data = StreamWithNewtonsoftJsonV2(url, client).GetAwaiter().GetResult();
                                                }*/

                                                if (data == null)
                                                {
                                                    url = getVerifyV3Url(getKey(result.Text));
                                                    data = StreamWithNewtonsoftJsonV2(url, client).GetAwaiter().GetResult();
                                                }

                                                if (data != null)
                                                {
                                                    string birthdate = data.birthdate;
                                                    string fio = data.fio;

                                                    if (item.d_birthday.HasValue &&
                                                        birthdate == item.d_birthday.Value.ToString("dd.MM.yyyy"))
                                                    {
                                                        string name = getEncodeName(item.c_first_name).ToLower() + " " + getEncodeName(item.c_last_name).ToLower() + " " + getEncodeName(item.c_middle_name).ToLower();
                                                        if (name.Trim() == fio.ToLower())
                                                        {
                                                            Log("Сертификат подтвержден " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));

                                                            file.b_verify = true;
                                                            file.c_gosuslugi_key = getKey(result.Text);
                                                            file.c_notice = null;
                                                            db.Update(file);
                                                            db.SaveChanges();
                                                            continue;

                                                        }
                                                        else
                                                        {
                                                            file.c_notice = "ФИО не совпадает. \r\n" + getEncodeName(item.c_first_name) + " " + getEncodeName(item.c_last_name) + " " + getEncodeName(item.c_middle_name) + "\r\n" + fio;
                                                            Log(file.c_notice);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        file.c_notice = "Дата рождения не совпадает. \r\n" + birthdate + "\r\n" + item.d_birthday.Value.ToString("dd.MM.yyyy");
                                                        Log(file.c_notice);
                                                    }

                                                    db.Update(file);
                                                    db.SaveChanges();
                                                    continue;
                                                }
                                                else
                                                {
                                                    file.c_notice = "Документ неподтвержден, как PDF-сертификат о вакцинации.";
                                                    db.Update(file);
                                                    db.SaveChanges();
                                                }
                                            }
                                        }
                                        else
                                        {
                                            file.c_notice = "На документе QR-код не найден.";
                                            Log(file.c_notice);
                                            file.c_gosuslugi_key = Guid.Empty.ToString();
                                            db.Update(file);
                                            db.SaveChanges();
                                        }
                                    }
                                }
                            }
                        }
                        catch(ceTe.DynamicPDF.Rasterizer.DocumentLoadException e)
                        {
                            file.c_notice = "Возможно документ не является PDF-сертификатом из-за ошибки в чтении.";
                            file.c_gosuslugi_key = Guid.Empty.ToString();
                            db.Update(file);
                            db.SaveChanges();

                            Log("[ERR]: " + e.ToString());
                        }
                        catch (Exception e)
                        {
                            file.c_notice = "Возможно документ не является PDF-сертификатом.";
                            //file.c_gosuslugi_key = Guid.Empty.ToString();
                            db.Update(file);
                            db.SaveChanges();

                            Log("[ERR]: " + e.ToString());
                        }
                    }
                }
            }

            if (File.Exists(dir + "/" + ignore))
            {
                File.Delete(dir + "/" + ignore);
            }

            Log("finished " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
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

        private void ID(string id)
        {
            Console.WriteLine(id);

            using (StreamWriter sw = File.AppendText(dir + "/" + ignore))
            {
                sw.WriteLine(id);
            }
        }

        private string getEncodeName(string name)
        {
            name = name.Trim();
            string output = char.ToUpper(name[0]).ToString();
            for(int i = 1; i < name.Length; i++)
            {
                output += "*";
            }
            return output;
        }

        private async Task<dynamic> StreamWithNewtonsoftJsonV2(string uri, HttpClient httpClient)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {

                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36 Edg/91.0.864.64");

                using var response = await httpClient.SendAsync(request);

                if (response.Content is object
                    && response.Content.Headers.ContentType != null)
                {
                    using var contentStream = await response.Content.ReadAsStreamAsync();

                    using var streamReader = new StreamReader(contentStream);
                    //string text = streamReader.ReadToEnd();
                    using var jsonReader = new JsonTextReader(streamReader);

                    JsonSerializer serializer = new JsonSerializer();

                    try
                    {
                        dynamic data = serializer.Deserialize(jsonReader);
                        dynamic d = new
                        {
                            fio = data.items[0].attrs[0].value,
                            birthdate = data.items[0].attrs[1].value
                        };
                        return d;
                    }
                    catch (JsonReaderException)
                    {
                        Log("[ERR]: " + "Invalid JSON.");
                    }
                }
                else
                {
                    Log("[ERR]: " + "HTTP Response was invalid and cannot be deserialised. " + uri);
                }

                return null;
            }
        }

        private async Task<dynamic> StreamWithNewtonsoftJson(string uri, HttpClient httpClient)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                request.Headers.Add("Accept", "*/*");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36 Edg/91.0.864.64");

                using var response = await httpClient.SendAsync(request);

                if (response.Content is object
                    && response.Content.Headers.ContentType != null)
                {
                    using var contentStream = await response.Content.ReadAsStreamAsync();

                    using var streamReader = new StreamReader(contentStream);
                    //string text = streamReader.ReadToEnd();
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
                    Log("[ERR]: " + "HTTP Response was invalid and cannot be deserialised. " + uri);
                }

                return null;
            }
        }

        private string getVerifyUrl(string key)
        {
            return "https://www.gosuslugi.ru/api/vaccine/v1/cert/verify/" + key;
        }

        private string getVerifyV2Url(string key)
        {
            return "https://www.gosuslugi.ru/api/covid-cert/v2/cert/check/" + key;
        }

        //https://www.gosuslugi.ru/api/covid-cert/v3/cert/check/9210000026404444?lang=ru&ck=14a9abcd401c067c7cb4fd84ee005ed4
        private string getVerifyV3Url(string key)
        {
            return "https://www.gosuslugi.ru/api/covid-cert/v3/cert/check/" + key;
        }

        private string getKey(string url)
        {
            int i;
            if (url.IndexOf("verify/unrz/") > 0)
            {
                i = url.LastIndexOf("/");
                return "unrz/" + url.Substring(i + 1, url.Length - 1 - i);
            }

            string[] data = url.Split("//");

            if (data.Count() > 2 && url.LastIndexOf("//") > 0)
            {
                i = url.LastIndexOf("//");
            }
            else
            {
                i = url.LastIndexOf("/");
            }
            string key = url.Substring(i + 1, url.Length - 1 - i);
            return key;
        }

        private void SendMail(MailMessage mail)
        {
            try
            {
                using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
                {
                    // логин и пароль
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential("mysmtp1987@gmail.com", "Bussine$Perfect");
                    smtp.EnableSsl = true;
                    smtp.Send(mail);
                }
            }
            catch (Exception e)
            {
                Log("[ERR:]" + e.ToString());
            }
        }

        public void SendReportMail()
        {
            MailAddress from = new MailAddress("mysmtp1987@gmail.com", "АРМ \"Вакцинация\"");
            MailAddress to = new MailAddress("akrasnov87@gmail.com");
            MailMessage mail = new MailMessage(from, to);

            mail.Subject = "Отчет по отправке писем от системы Вакцинация";

            string fileName = "log-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
            if (File.Exists("temp/" + fileName))
            {
                mail.Body = File.ReadAllText("temp/" + fileName);
            }

            // письмо представляет код html
            mail.IsBodyHtml = false;

            SendMail(mail);
        }
    }
}
