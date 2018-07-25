using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Firefox;
using System.Xml.Serialization;
using System.IO;
using System.Threading;
using System.Data.SqlClient;
using MySql.Data.MySqlClient;

public static class SeleniumExtension
{

    public static void Authorise(this IWebDriver webDriver, By loginBox, string login, By passwordBox, string password, By AuthoriseButton, int timeOut)
    {
        try
        {
            webDriver.FindElement(loginBox, timeOut).SendKeys(login);
            webDriver.FindElement(passwordBox, timeOut).SendKeys(password);
            webDriver.FindElement(AuthoriseButton, timeOut).Click();
        }
        catch (Exception e)
        {
            throw new Exception(e.Message);
        }
    }
    public static void Authorise(this IWebDriver webDriver, By loginBox, string login, By passwordBox, string password, By AuthoriseButton)
    {
        webDriver.Authorise(loginBox, login, passwordBox, password, AuthoriseButton, 0);
    }
    public static IWebElement FindElement(this IWebDriver driver, By by, int timeoutInSeconds)
    {
        if (timeoutInSeconds > 0)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutInSeconds));
            return wait.Until(drv => drv.FindElement(by));
        }
        else
        {
            return driver.FindElement(by);
            //throw new Exception( "Timeout must be grater then 0");
        }
        
    }

    
}

public static class StringExtension
{
    /// <summary>
    /// Принимает XPath строку и преобразует в By
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static By ToByXPath(this string path)
    {
        return By.XPath(path);
    }
}



/// <summary>
/// Класс содержащий путь к полю авторизации и его значение
/// </summary>
[Serializable]
public class Field
    {
    public string Value { get; set; }
    public string Path { get; set; }

    public Field(string Val,string P)
    {
        Value = Val;
        Path = P;
    }
    public Field()
    {

    }
    }

/// <summary>
/// Класс содержит функции для сериализации/десериализации
/// </summary>
/// <typeparam name="T"></typeparam>
public class Serializer<T> where T : AParams, new()
{
    public T Fields { get; set; }
    public Serializer()
    {
        Fields = new T();
        if (File.Exists(Fields.XMLFileName))
        {
            ReadParams();
        }
        else
        {
            WriteParams();
        }
    }
    public void ReadParams()
    {
        try
        {
            XmlSerializer xml = new XmlSerializer(Fields.GetType());
            using (TextReader reader = new StreamReader(Fields.XMLFileName))
            {
                dynamic buf = xml.Deserialize(reader);
                Fields = buf;
            }
        }
        catch
        {

        }
    }
    public void WriteParams()
    {
        XmlSerializer xml = new XmlSerializer(Fields.GetType());
        using (TextWriter writer = new StreamWriter(Fields.XMLFileName))
        {
            xml.Serialize(writer, Fields);
        }
    }
}

/// <summary>
/// Объекты подлежащие сериализации наследуют данный класс
/// </summary>
public abstract class AParams
{
    public string XMLFileName;
}

/// <summary>
/// Наименования полей для поиска из которых берутся данные по БЗ
/// </summary>
public class KnowledgeBaseParams:AParams
{
    public string Category { get; set; }//Поиск категории в дереве
    public string Article { get; set; }//Поиск статьи в дереве
    //public string CategoryTitle { get; set; }
    public string ArticleTitle { get; set; }//Заголовок статьи
    public string ArticleContent { get; set; }//Текст статьи
    public string Attachements { get; set; }//Прикрепленные файлы
    public string AuthorDateInfo { get; set; }//Поск строки в статье, содержащий инфу об авторе и последей редакции
    public string InfoNotAllowed { get; set; }//Ссылка на статью, которая не имеет содержимого

    public string TempFolder { get; set; }//Папка временных файлов, в которую сохраняются прикрепления

    public KnowledgeBaseParams()
    {
        Category = @"//div[@class='kbcategorytitle']/a";
        Article = @"//div[@class='kbarticle']/a";
        ArticleTitle = @"//span[@class='kbtitlemain']";
        ArticleContent = @"//td[@class='kbcontents']";
        Attachements = @"//div[@class='kbattachmentitem']";
        AuthorDateInfo = @"//div[@class='kbinfo']";
        InfoNotAllowed= @"//div[@class='infotextcontainer']";

        TempFolder = "Temp";
        //CategoryTitle=

        XMLFileName = Environment.CurrentDirectory + @"\Knowledge Base Elements";
    }
}


/// <summary>
/// Параметры авторизации на сайте Барса
/// </summary>
public class AuthFields : AParams
{

    public Field Login { get; set; }
    public Field Password { get; set; }
    public string LogInPage { get; set; }
    public string SendButton { get; set; }

    public AuthFields()
    {
        Login = new Field("davam@nso.ru", "//input[@name='scemail']");
        Password = new Field("agt4ir696tyu", "//input[@name='scpassword']");
        SendButton = "//div[@id='loginsubscribebuttons']/input";
        LogInPage = "https://help.bars-open.ru/";
        XMLFileName = Environment.CurrentDirectory + @"\Authentication Parameters";
    }


}
/// <summary>
/// Доступ к бд
/// </summary>
public class MySQLConnect:AParams
{
    public string ServerName { get; set; }
    public string UserName { get; set; }
    public string DbName { get; set; }
    public string Port { get; set; }
    public string Password { get; set; }
    public MySqlConnection Connection { get => connection;  }

    [NonSerialized]
    private MySqlConnection connection;

    public MySQLConnect()
    {
        ServerName = "localhost"; // Адрес сервера (для локальной базы пишите "localhost")
        UserName = "kb_admin"; // Имя пользователя
        DbName = "kb"; //Имя базы данных
        Port = "3306"; // Порт для подключения
        Password = "25632541789"; // Пароль для подключения
        string connStr = "server=" + ServerName +
                        ";user=" + UserName +
                        ";database=" + DbName +
                        ";port=" + Port +
                        ";password=" + Password + ";SslMode=none;";
        XMLFileName= Environment.CurrentDirectory + @"\DataBase Parameters";
    
        connection = new MySqlConnection(connStr);
    }

}



/// <summary>
/// Класс авторизации в ТП БАРС
/// </summary>
public  class BarsAuth:Serializer<AuthFields>
{

    /// <summary>
    /// Авторизация на сайте с заданным временем ожидания
    /// </summary>
    /// <param name="driver"></param>
    /// <param name="timeout"></param>
    public void Authorize(ref FirefoxDriver driver, int timeout)
    {
        driver.Navigate().GoToUrl(Fields.LogInPage);
        driver.Authorise(Fields.Login.Path.ToByXPath(), Fields.Login.Value, Fields.Password.Path.ToByXPath(), Fields.Password.Value, Fields.SendButton.ToByXPath(), timeout);
    }
    public void Authorize(ref FirefoxDriver driver) => Authorize(ref driver, 0);


}

