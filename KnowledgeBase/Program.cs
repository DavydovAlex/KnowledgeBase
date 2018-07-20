﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using System.Xml.Serialization;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;
using MySql.Data.MySqlClient;
using System.Web;
using System.Net;
using System.Collections.Specialized;
using Newtonsoft.Json;
using System.Net.Http;

namespace KnowledgeBase
{


    public class KnowledgeBaseSaver
    {
        IWebDriver Driver { get; }
        MySqlConnection DB { get; }
        KnowledgeBaseParams Params { get; }
        public KnowledgeBaseSaver(IWebDriver dr,MySqlConnection db, Serializer<KnowledgeBaseParams> kb)
        {
            Driver = dr;
            DB = db;
            Params = kb.Fields;
            
        }
        /// <summary>
        /// Запись файлов в БД
        /// </summary>
        public void AddFiles()
        {
            List<Page> terminatePages=SelectTerminatePages();
            foreach(Page terminatePage in terminatePages)
            {
                Driver.Navigate().GoToUrl(terminatePage.URL);
                IReadOnlyCollection<IWebElement> attachmentItems = Driver.FindElements(Params.Attachements.ToByXPath());
                List<Files> attachmentFiles = new List<Files>();
                foreach (IWebElement attachment in attachmentItems)
                {
                    Files file = new Files();
                    file.PID = terminatePage.Id;
                    file.IsActual = 1;
                    string g = attachment.GetAttribute("onclick");
                    file.URL = Regex.Match(attachment.GetAttribute("onclick"), @"(?<=javascript: PopupSmallWindow\()[\S\s]+?(?=\);)").Value;

                    string[] sizeName = SplitNameSize(attachment.Text);
                    file.Size = sizeName[0];
                    file.Name = sizeName[1];

                    attachment.Click();
                    Thread.Sleep(500);
                    //Проверка на скачивание файла
                    //если в папке 2 элемента, значит загрузка идет
                    int i = 0;
                    while (Directory.GetFiles(Params.TempFolder).Length == 2)
                    {
                        Thread.Sleep(100);
                        i++;
                        if (i == 200)
                        {
                            throw new FileNotFoundException();
                        }
                    }
                    file.File = File.ReadAllBytes(Params.TempFolder + @"\" + file.Name);
                    File.Delete(Params.TempFolder + @"\" + file.Name);
                    Thread.Sleep(1000);
                    i = 0;
                    //Ожидание удаления файла из папки
                    while (Directory.GetFiles(Params.TempFolder).Length != 0)
                    {
                        Thread.Sleep(100);
                        i++;
                        if (i == 2000)
                            throw new FileNotFoundException();
                    }
                    AddFile(file);

                }

            }

        }
        public void CreateWikiCategoreeTree()
        {
            List<Page> nonTerminatePages = SelectNonTerminatePages();
            foreach(Page page in nonTerminatePages)
            {
                if(page.Parent!=0)
                {
                    Page parent = new Page();
                    foreach(Page parentPage in nonTerminatePages)
                    {
                        if(page.Parent==parentPage.Id)
                        {
                            parent = parentPage;
                            break;
                        }
                            
                    }


                }
            }
        }
        /// <summary>
        /// Запись страниц в БД
        /// </summary>
        /// <param name="page"></param>
        public void Traversing(Page page)
        {
            Driver.Navigate().GoToUrl(page.URL);
            IReadOnlyCollection<IWebElement> categories = Driver.FindElements(Params.Category.ToByXPath());
            IReadOnlyCollection<IWebElement> articles = Driver.FindElements(Params.Article.ToByXPath());
            var references = categories.Concat((IEnumerable<IWebElement>)articles);
            List<Page> childPages = new List<Page>();
            page.ChildCount = references.Count();
            if (references.Count<IWebElement>() == 0)//Если нет статей или разделов, значит это конечная страница
            {
                page.IsTerminate = 1;
                try
                {
                    //Проверка на отсутсвие страницы, чтоб дальше не посыпалось
                    try
                    {
                        Driver.FindElement(Params.InfoNotAllowed.ToByXPath());
                        page.InfoNotAllowed = 1;
                        page.Updated = "0001-01-01 00:00:00";
                        throw new Exception();
                    }
                    catch(NoSuchElementException e)
                    {
                        
                    }
                    page.InfoNotAllowed = 0;
                    page.Header = Driver.FindElement(Params.ArticleTitle.ToByXPath()).Text;                   
                    page.InnerText = HttpUtility.HtmlEncode(Driver.FindElement(Params.ArticleContent.ToByXPath()).GetAttribute("innerHTML"));
                    string authorDateInfo = Driver.FindElement(Params.AuthorDateInfo.ToByXPath()).Text;
                    page.Author = Regex.Match(authorDateInfo, @"(?<=Автор[\s]+?)[\S\s]+?(?=[\s]+?на)").Value;
                    string date = Regex.Match(authorDateInfo, @"(?<=[\s]+?на[\s]+?)[\s\S]+").Value;
                    page.Updated = date == String.Empty ? "0001-01-01 00:00:00" : DateTime.Parse(date.Replace('.', '/')).ToString("yyyy-MM-dd HH:mm:00");
                    page.Id = AddPage(page);
                }
                catch(FileNotFoundException e)
                {

                }
                catch (Exception ex)
                {

                }
            }
            else
            {
                page.IsTerminate = 0;
                page.InfoNotAllowed = 0;
                page.Updated = "0001-01-01 00:00:00";
                page.Id = AddPage(page);
                foreach (IWebElement category in references)
                {
                    Page nextPage = new Page();
                    nextPage.URL = category.GetAttribute("href");
                    nextPage.Header = category.Text;
                    childPages.Add(nextPage);
                }
                foreach (Page childPage in childPages)
                {
                    childPage.Parent = page.Id;
                    Traversing(childPage);
                }
            }
        }
        /// <summary>
        /// Разбивает входную строку на пару состоящую из имени и размера файла
        /// </summary>
        /// <param name="str"></param>
        /// <returns>
        /// string[0]-имя
        /// string[1]-размер
        /// </returns>
        public string[] SplitNameSize(string str)
        {
            string[] result = new string[2];
            int k = 0;
            for (int i = str.Length-1; i >= 0; i--)
            {
                if(str[i]=='(')
                {
                    k = i;
                    break;
                }
            }
            result[0] = Regex.Match(str.Substring(k), @"(?<=\()[\S\s]+?(?=\))").Value;
            result[1] = Regex.Match(str.Substring(0,k-1), @"[\S\s]+").Value.Trim(' ');
            return result;
        }
        public void BlobTest()
        {
            string select = "SELECT File FROM files WHERE Id=1;";
            MySqlCommand selectId = DB.CreateCommand();
            selectId.CommandText = select;
            byte[] result = (byte[])selectId.ExecuteScalar();
            File.WriteAllBytes(Params.TempFolder + @"\" + "123.zip",result);
        }



        public void InsertQueryString(string table,NameValueCollection data)
        {
            string query = String.Format("INSERT INTO {0} ",table);
            string columns=" (";
            string values=" (";

            foreach(string column in data.Keys)
            {
                string value = data[column];
                columns += String.Format(" {0},", column);
                values += String.Format(" '{0}',", value);
            }
            columns=columns.Substring(0, columns.Length - 1)+")";
            values = values.Substring(0, values.Length - 1)+")";
            query += columns + " VALUES " + values+";";

        }


        /// <summary>
        /// Запись страницы в БД
        /// </summary>
        /// <param name="page"></param>
        /// <returns></returns>
        public int AddPage(Page page)
        {
            string insert = "INSERT INTO pages(URL,Parent,Header,InnerText,IsTerminate,Updated,Author,InfoNotAllowed,ChildCount) VALUES ('" + page.URL + "','" +
                                                                         page.Parent + "','" +
                                                                         page.Header + "','" +
                                                                         page.InnerText + "','" +
                                                                         page.IsTerminate + "','" +
                                                                         page.Updated + "','"+
                                                                         page.Author+ "','" +
                                                                         page.InfoNotAllowed + "','" +
                                                                         page.ChildCount + "');";
            MySqlScript insertScript = new MySqlScript(DB, insert);
            insertScript.Execute();
            string select= "SELECT Id FROM pages WHERE URL='"+page.URL+"';";
            MySqlCommand selectId = DB.CreateCommand();
            selectId.CommandText = select;
            int result=(Int32)selectId.ExecuteScalar();
            return result;
        }
        /// <summary>
        /// Выбор файлов из БД
        /// </summary>
        /// <returns></returns>
        public List<Files> SelectFiles()
        {
            List<Files> result = new List<Files>();
            MySqlCommand selectFiles = DB.CreateCommand();
            selectFiles.CommandText = "SELECT * FROM files WHERE IsActual=1";
            MySqlDataReader selectedFiles = selectFiles.ExecuteReader();
            while (selectedFiles.Read())
            {
                Files nextPage = new Files
                {
                    Id = selectedFiles.GetInt32(0),
                    PID = selectedFiles.GetInt32(1),
                    Name = selectedFiles.GetString(2),
                    Size = selectedFiles.GetString(3),
                    URL = selectedFiles.GetString(4),
                    IsActual = selectedFiles.GetInt32(5)
                };
                long size = selectedFiles.GetBytes(6, 0, null, 0, 0);
                byte[] buf = new byte[size];
                int bufferSize = 1024;
                long bytesRead = 0;
                int curPos = 0;
                while (bytesRead < size)
                {
                    if(size-bytesRead>=bufferSize)
                        bytesRead += selectedFiles.GetBytes(6, curPos, buf, curPos, bufferSize);
                    else
                        bytesRead += selectedFiles.GetBytes(6, curPos, buf, curPos, (int)(size - bytesRead));
                    curPos += bufferSize;
                }
                nextPage.File = buf;
                result.Add(nextPage);
            }
            selectedFiles.Close();
            return result;
        }
        /// <summary>
        /// Выбор страницы по Id(необходимо продумать при приходе 0 ссылки)
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        public Page SelectPage(int Id)
        {            
            MySqlCommand selectInfo = DB.CreateCommand();
            selectInfo.CommandText = "SELECT * FROM pages WHERE Id="+Id;
            MySqlDataReader selectedInfo = selectInfo.ExecuteReader();
            List<Page> result = Page.SelectPages(selectedInfo);

            selectedInfo.Close();
            
            return result[0];
        }
        public List<Page> SelectTerminatePages()
        {
            
            MySqlCommand selectInfo = DB.CreateCommand();
            selectInfo.CommandText = "SELECT * FROM pages WHERE IsTerminate=1";
            MySqlDataReader selectedInfo = selectInfo.ExecuteReader();
            List<Page> result = Page.SelectPages(selectedInfo);
            selectedInfo.Close();
            return result;
        }
        public List<Page> SelectNonTerminatePages()
        {
            
            MySqlCommand selectInfo = DB.CreateCommand();
            selectInfo.CommandText = "SELECT * FROM pages WHERE IsTerminate=0 ORDER BY Id";
            MySqlDataReader selectedInfo = selectInfo.ExecuteReader();
            List<Page> result = Page.SelectPages(selectedInfo);
            selectedInfo.Close();
            return result;
        }
        public void AddFile(Files file)
        {
            
            var command = new MySqlCommand("", DB);

            command.CommandText = "INSERT INTO files(PID,Name,Size,URL,IsActual,File) VALUES (@PID,@Name,@Size,@URl,@IsActual,@File);";
            var paramPID = new MySqlParameter("@PID", MySqlDbType.Int32, 11);
            var paramName = new MySqlParameter("@Name", MySqlDbType.VarChar, 500);
            var paramIsActual = new MySqlParameter("@IsActual", MySqlDbType.Int32, 2);
            var paramSize = new MySqlParameter("@Size", MySqlDbType.VarChar, 100);
            var paramURL = new MySqlParameter("@URL", MySqlDbType.VarChar, 500);
            var paramFile = new MySqlParameter("@File", MySqlDbType.LongBlob);

            paramPID.Value = file.PID;
            paramName.Value = file.Name;
            paramIsActual.Value = file.IsActual;
            paramSize.Value = file.Size;
            paramURL.Value = file.URL;
            paramFile.Value = file.File;

            command.Parameters.Add(paramPID);
            command.Parameters.Add(paramName);
            command.Parameters.Add(paramIsActual);
            command.Parameters.Add(paramSize);
            command.Parameters.Add(paramURL);
            command.Parameters.Add(paramFile);

            command.ExecuteNonQuery();

            //string insert = "INSERT INTO pages(PID,Name,Size,URL,IsActual) VALUES ('" + file.PID + "','" +
            //                                                             file.Name + "','" +
            //                                                             file.Size + "','" +
            //                                                             file.URL + "','" +
            //                                                             file.IsActual +  "');";
            //MySqlScript insertScript = new MySqlScript(DB, insert);
            //insertScript.Execute();
        }
    }

    public class Page
    {
        public int Id { get; set; }
        public string URL { get; set; }
        public int Parent { get; set; }
        public string Author { get; set; }
        public string Updated { get; set; }
        public string Header { get; set; }
        public string InnerText { get; set; }
        public int IsTerminate { get; set; }
        public int InfoNotAllowed { get; set; }
        public int ChildCount { get; set; }
        public List<Files> Files { get; set; }

        public static List<Page> SelectPages(MySqlDataReader select)
        {
            List<Page> result = new List<Page>();
            while (select.Read())
            {
                Page nextPage = new Page
                {
                    Id = select.GetInt32(0),
                    URL = select.GetString(1),
                    Parent = select.GetInt32(2),
                    Author = select.GetString(3),
                    Updated = select.GetDateTime(4).ToString(),
                    Header = select.GetString(5),
                    InnerText = select.GetString(6),
                    IsTerminate = select.GetInt32(7),
                    InfoNotAllowed = select.GetInt32(8),
                    ChildCount = select.GetInt32(9)
                };
                result.Add(nextPage);
            }
            return result;
        }
    }
    public class Files
    {
        public int Id { get; set; }
        public int PID { get; set; }
        public string Name { get; set; }
        public string Size { get; set; }
        public string URL { get; set; }
        public int IsActual { get; set; }
        public byte[] File { get; set; } 
        
    }



    class Program
    {
        static void Main(string[] args)
        {
            string path = @"temp";


            FirefoxOptions optToDownload = new FirefoxOptions
            {
                Profile = new FirefoxProfileManager().GetProfile("SE Download Profile")
            };
            FirefoxDriver firefox = new FirefoxDriver(optToDownload);
            Serializer<KnowledgeBaseParams> kBParams = new Serializer<KnowledgeBaseParams>();
            if (!Directory.Exists(kBParams.Fields.TempFolder))
                Directory.CreateDirectory(kBParams.Fields.TempFolder);
            BarsAuth auth = new BarsAuth();
            auth.Authorize(ref firefox, 10);

            Serializer<MySQLConnect> dbParams = new Serializer<MySQLConnect>();
            MySqlConnection DB = dbParams.Fields.Connection;
            DB.Open();
            KnowledgeBaseSaver kb = new KnowledgeBaseSaver(firefox, DB, kBParams);
            Page rootPage = new Page
            {
                URL = "https://help.bars-open.ru/index.php?/Knowledgebase/List",
                Parent = 0,
                Header = "База знаний",
                IsTerminate = 0,
                Updated = "0001-01-01 00:00:00"
            };
            //kb.Traversing(rootPage);
            //kb.AddFiles();

            NameValueCollection cc = new NameValueCollection();
            cc.Add("Id", "12544");
            cc.Add("sssss", "ssssscdecde");
            kb.InsertQueryString("table",cc);
            WikiApi wikiApi = new WikiApi()
            {
                Host = "http://localhost",
                Login="Admin",
                Password="25632541789"
            };
            WebHeaderCollection Header = new WebHeaderCollection();
            Header.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:56.0) Gecko/20100101 Firefox/56.0");
            Header.Add(HttpRequestHeader.Accept, "*/*");
            Header.Add(HttpRequestHeader.AcceptEncoding, "gzip, deflate");
            Header.Add(HttpRequestHeader.ContentType, "multipart/form-data; boundary=" + wikiApi.Boundary);//"application/x-www-form-urlencoded"
            wikiApi.WebQuery.Headers = Header;

            //wikiApi.LogIn();
            //string r=wikiApi.EditPage("TestClass", "[[Category:Разработка]]");
            //List<Files> ff = kb.SelectFiles();
            //string rrr=wikiApi.UploadFile(ff[0].File,"1234567.zip");
            //File.WriteAllBytes(kBParams.Fields.TempFolder+"/"+ff[0].Name,ff[0].File);

            List<Page> nonTerminatePages = kb.SelectNonTerminatePages();
            foreach (Page page in nonTerminatePages)
            {
                if (page.Parent != 0)
                {
                    Page parent = new Page();
                    foreach (Page parentPage in nonTerminatePages)
                    {
                        if (page.Parent == parentPage.Id)
                        {
                            parent = parentPage;
                            string res = wikiApi.EditPage("Категория:" + page.Header, "[[Category:" + parent.Header + "]]");
                            break;
                        }

                    }
                    

                }
            }

            List<Page> terminatePages = kb.SelectTerminatePages();
            foreach(Page page in terminatePages)
            {
                Page parent = kb.SelectPage(page.Parent);
                string res;
                string innerText = HttpUtility.HtmlDecode(page.InnerText);
                string divOpen=Regex.Replace(innerText, @"<[\s]*pre", "<div nowrap=\"true\"");
                string divClose= Regex.Replace(divOpen, @"</[\s]*pre[\s]*>", "</div>");
                string text = divClose+ Environment.NewLine + "[[Category:" + parent.Header + "]]";
                    res = wikiApi.EditPage(page.Header,text);

                    
            }
        }
    }

    class WikiApi
    {
        public WebQuery WebQuery { get; set; }
        public string LoginToken { get; private set; }
        public string CsrfToken { get; private set; }

        public string Host { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }

        public string Boundary { get; set; }
        public WikiApi()
        {
            WebQuery = new WebQuery();
            Boundary = "--------------------" + DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        }

        public void LogIn()
        {
            GetLoginToken();

            var loginBody = new MultipartFormDataContent(Boundary);
            loginBody.Add(new StringContent(Login), "lgname");
            loginBody.Add(new StringContent(Password), "lgpassword");
            loginBody.Add(new StringContent(LoginToken), "lgtoken");
            string login = WebQuery.POST(Host+"/api.php?action=login&format=json", loginBody.ReadAsStringAsync().Result);
            GetScrtToken();
        }

        void GetLoginToken()
        {
            string loginTokenQuery = WebQuery.GET(Host + "/api.php?action=query&meta=tokens&type=login&format=json");
            dynamic objLoginToken = JsonConvert.DeserializeObject(loginTokenQuery);
            LoginToken = objLoginToken["query"]["tokens"]["logintoken"].Value;
            
        }
        void GetScrtToken()
        {
            string CsrfTokenQuery = WebQuery.GET(Host+ "/api.php?action=query&meta=tokens&type=csrf&format=json");
            dynamic objCsrfToken = JsonConvert.DeserializeObject(CsrfTokenQuery);
            CsrfToken= objCsrfToken["query"]["tokens"]["csrftoken"].Value;
        }
        public string EditPage(string title,string text)
        {
            LogIn();
            string result;
            var addPageBody = new MultipartFormDataContent(Boundary);
            addPageBody.Add(new StringContent(title), "title");
            addPageBody.Add(new StringContent(text), "text");
            addPageBody.Add(new StringContent(CsrfToken), "token");
            string ss = addPageBody.ReadAsStringAsync().Result;
            result = WebQuery.POST(Host+"/api.php?action=edit&format=json", addPageBody.ReadAsStringAsync().Result);
            return result;
        }
        public string UploadFile(byte[] file,string fileName)
        {
            LogIn();
            string result;
            var uploadPageBody = new MultipartFormDataContent(Boundary);
            
            uploadPageBody.Add(new StringContent(fileName), "filename");
            uploadPageBody.Add(new ByteArrayContent(file),"file",fileName);
            uploadPageBody.Add(new StringContent(CsrfToken), "token");
            string sss = uploadPageBody.ReadAsStringAsync().Result;
            result = WebQuery.POST(Host + "/api.php?action=upload&format=json", uploadPageBody.ReadAsByteArrayAsync().Result);
            return result;
        }

    }

    //Класс представлющий комбинацию HTTPWebREquest+HTTPWebResponce
    //В классе содержатся данные о заголовках запроса и ответа, а также данные куки
    //Можно установить прокси сервер
    public class WebQuery
    {
        //Запрещает/ разрешает автоматический редирект
        bool _allowAutoRedirect;
        public bool AllowAutoRedirect
        {
            get { return _allowAutoRedirect; }
            set { _allowAutoRedirect = value; }
        }

        //Возвращает код ответа
        int _responceStatus;
        public int ResponceStatusCode
        {
            get { return _responceStatus; }
        }

        //Устанавливает прокси
        IWebProxy _proxy;
        public IWebProxy Proxy
        {
            set { _proxy = value; }
        }

        //Адрес ресурса в ответе
        Uri _responceUri;
        public Uri ResponceUri
        {
            get { return _responceUri; }
        }

        //Заголовки запроса
        WebHeaderCollection _headers;
        public WebHeaderCollection Headers
        {
            get { return _headers; }
            set
            {
                foreach (string key in value)
                {
                    var val = value[key];
                    Headers.Add(key, val);
                }
            }
        }

        //Заголовки ответа
        WebHeaderCollection _responceHeaders;
        public WebHeaderCollection ResponceHeaders
        {
            get { return _responceHeaders; }
            private set { _responceHeaders = value; }
        }

        //Куки запроса
        CookieCollection _cookie;


        public WebQuery()
        {
            //Инициализация контейнеров
            _headers = new WebHeaderCollection();
            _responceHeaders = new WebHeaderCollection();
            _cookie = new CookieCollection();
            _allowAutoRedirect = true;// редирект разрешен
        }

        //Установка заголовкой запроса
        //Для некоторых заголовков имеются выделенные свойства, которые нельзя добавлять через Add
        private void AddHeaders(HttpWebRequest request, WebHeaderCollection headers)
        {
            foreach (string header in headers)
            {
                var value = headers[header];
                switch (header)
                {
                    case "User-Agent":
                        request.UserAgent = value;
                        break;
                    case "Accept":
                        request.Accept = value;
                        break;
                    case "Content-Type":
                        request.ContentType = value;
                        break;
                    case "Referer":
                        request.Referer = value;
                        break;
                    default:
                        request.Headers.Add(header, value);
                        break;
                }
            }
        }
        void AddCookies(HttpWebRequest request)
        {
            request.CookieContainer = new CookieContainer();
            foreach (System.Net.Cookie c in _cookie)
            {
                request.CookieContainer.Add(c);
            }
        }
        void SetAllowAutoRedirect(ref HttpWebRequest request)
        {
            request.AllowAutoRedirect = _allowAutoRedirect;
        }
        void SetProxy(ref HttpWebRequest request)
        {
            if (_proxy != null)
                request.Proxy = (WebProxy)_proxy;
        }
        //Обобщенный метод GET/POST запроса
        string Request(string address, string Method, string body = "")
        {

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = Method;
            SetProxy(ref request);
            SetAllowAutoRedirect(ref request);


            AddHeaders(request, Headers);
            AddCookies(request);
            switch (Method)
            {
                case "GET":
                    break;
                case "POST":
                    //Формирование тела POST запроса
                    UTF8Encoding encoding = new UTF8Encoding();/////
                    //ASCIIEncoding encoding = new ASCIIEncoding();
                    byte[] bytePostData = encoding.GetBytes(body);
                    request.ContentLength = bytePostData.Length;
                    using (Stream postStream = request.GetRequestStream())
                    {
                        postStream.Write(bytePostData, 0, bytePostData.Length);
                    }
                    break;
            }
            HttpWebResponse responce = (HttpWebResponse)request.GetResponse();

            Stream dataStream = responce.GetResponseStream();
            string HtmlResponse;
            using (StreamReader reader = new StreamReader(dataStream))
            {
                HtmlResponse = reader.ReadToEnd();
            }
            _responceUri = responce.ResponseUri;
            _responceStatus = (int)responce.StatusCode;

            //Обновление куки
            CookieCollection bufCookies = new CookieCollection();
            if (responce.Cookies.Count != 0)
            {
                foreach (System.Net.Cookie cookieResponce in responce.Cookies)
                {
                    bool isNewCookie = true;

                    foreach (System.Net.Cookie cookieRequest in _cookie)
                    {
                        if (cookieRequest.Name == cookieResponce.Name)
                        {
                            isNewCookie = false;
                            bufCookies.Add(cookieResponce);
                            break;
                        }

                    }
                    if (isNewCookie)
                    {
                        bufCookies.Add(cookieResponce);
                    }
                }
                _cookie = bufCookies;
            }
            else
            {

            }

            //Установка заголовка Cookie 
            if (responce.Headers.Get("Set-Cookie") != null)
            {
                if (Headers.Get("Cookie") != null)
                {
                    Headers.Set(HttpRequestHeader.Cookie, responce.Headers.Get("Set-Cookie"));
                }
                else
                {
                    Headers.Add(HttpRequestHeader.Cookie, responce.Headers.Get("Set-Cookie"));
                }
            }
            ResponceHeaders = responce.Headers;
            responce.Close();

            return HtmlResponse;
        }

        //
        string Request(string address, string Method, byte[] body)
        {

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = Method;
            SetProxy(ref request);
            SetAllowAutoRedirect(ref request);


            AddHeaders(request, Headers);
            AddCookies(request);
            switch (Method)
            {
                case "GET":
                    break;
                case "POST":
                    //Формирование тела POST запроса
                    UTF8Encoding encoding = new UTF8Encoding();/////
                    //ASCIIEncoding encoding = new ASCIIEncoding();
                    //byte[] bytePostData = encoding.GetBytes(body);
                    request.ContentLength = body.Length;
                    using (Stream postStream = request.GetRequestStream())
                    {
                        postStream.Write(body, 0, body.Length);
                    }
                    break;
            }
            HttpWebResponse responce = (HttpWebResponse)request.GetResponse();

            Stream dataStream = responce.GetResponseStream();
            string HtmlResponse;
            using (StreamReader reader = new StreamReader(dataStream))
            {
                HtmlResponse = reader.ReadToEnd();
            }
            _responceUri = responce.ResponseUri;
            _responceStatus = (int)responce.StatusCode;

            //Обновление куки
            CookieCollection bufCookies = new CookieCollection();
            if (responce.Cookies.Count != 0)
            {
                foreach (System.Net.Cookie cookieResponce in responce.Cookies)
                {
                    bool isNewCookie = true;

                    foreach (System.Net.Cookie cookieRequest in _cookie)
                    {
                        if (cookieRequest.Name == cookieResponce.Name)
                        {
                            isNewCookie = false;
                            bufCookies.Add(cookieResponce);
                            break;
                        }

                    }
                    if (isNewCookie)
                    {
                        bufCookies.Add(cookieResponce);
                    }
                }
                _cookie = bufCookies;
            }

            //Установка заголовка Cookie 
            if (responce.Headers.Get("Set-Cookie") != null)
            {
                if (Headers.Get("Cookie") != null)
                {
                    Headers.Set(HttpRequestHeader.Cookie, responce.Headers.Get("Set-Cookie"));
                }
                else
                {
                    Headers.Add(HttpRequestHeader.Cookie, responce.Headers.Get("Set-Cookie"));
                }
            }
            ResponceHeaders = responce.Headers;
            responce.Close();

            return HtmlResponse;
        }


        public string GET(string Url)
        {
            return Request(Url, "GET");
        }
        public string POST(string Url, string body)
        {
            return Request(Url, "POST", body);
        }
        public string POST(string Url, byte[] body)
        {
            return Request(Url, "POST", body);
        }
        public string POST(string Url, NameValueCollection body)
        {
            string strBody = GetPostBody(body);
            return Request(Url, "POST", strBody);
        }
        public string POST(string Url, NameValueCollection body,string boundary)
        {
            string strBody = "";
            string cd = "Content-Disposition: form-data; name=";
            strBody += boundary;
            foreach (string name in body.Keys)
            {
                //strBody += boundary;
                strBody += Environment.NewLine;
                strBody += cd + "\""+name+"\"";
                strBody += Environment.NewLine + Environment.NewLine;
                strBody += body.Get(name);
                strBody += Environment.NewLine;
                strBody += boundary;
            }
            return Request(Url, "POST", strBody);
        }
        //Вовращает строку тела POST запроса
        public string GetPostBody(NameValueCollection data)
        {
            string result = "";
            foreach (string name in data.Keys)
            {
                result += name + "=" + data.Get(name) + "&";
            }
            result = result.Substring(0, result.Length - 1);
            return result;
        }
    }
}
