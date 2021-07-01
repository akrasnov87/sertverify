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
            Log("processing: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));

            using (ApplicationContext db = new ApplicationContext())
            {
                var query = (from f in db.Files
                            join d in db.Documents on f.f_document equals d.id
                            join u in db.Users on d.f_user equals u.id
                            where !u.sn_delete && !u.b_disabled && !d.sn_delete && string.IsNullOrEmpty(f.c_gosuslugi_key) && f.c_type == "sert" && !f.sn_delete
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
                            byte[] bytes = file.ba_data;
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
                                                        Log("Сертификат подтвержден " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));

                                                        file.b_verify = true;
                                                        file.c_gosuslugi_key = getKey(result.Text);

                                                        db.Update(file);
                                                        db.SaveChanges();
                                                        continue;

                                                    } else
                                                    {
                                                        file.c_notice = "ФИО не совпадает.";
                                                        Log(file.c_notice);
                                                    }
                                                } else
                                                {
                                                    file.c_notice = "Дата рождения не совпадает.";
                                                    Log(file.c_notice);
                                                }

                                                //file.c_gosuslugi_key = Guid.Empty.ToString();
                                                db.Update(file);
                                                db.SaveChanges();
                                                continue;
                                            } else
                                            {
                                                file.c_notice = "Документ неподтвержден, как PDF-сертификат о вакцинации.";
                                                file.c_gosuslugi_key = Guid.Empty.ToString();
                                                db.Update(file);
                                                db.SaveChanges();
                                            }
                                        }
                                    } else
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
                        catch (Exception e)
                        {
                            file.c_notice = "Возможно документ не является PDF-сертификатом.";
                            file.c_gosuslugi_key = Guid.Empty.ToString();
                            db.Update(file);
                            db.SaveChanges();

                            Log("[ERR]: " + e.ToString());
                        }
                    }
                }
            }

            Log("finished " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));

            SendReportMail();
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
            string output = char.ToUpper(name.Trim()[0]).ToString();
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

        private void SendMail(MailMessage mail)
        {
            try
            {
                SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587);
                // логин и пароль
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new NetworkCredential("mysmtp1987@gmail.com", "Bussine$Perfect");
                smtp.EnableSsl = true;
                smtp.Send(mail);
            }
            catch (Exception e)
            {
                Log("[ERR:]" + e.ToString());
            }
        }

        private void SendReportMail()
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
