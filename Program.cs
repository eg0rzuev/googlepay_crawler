using System;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using PuppeteerSharp;
using PuppeteerSharp.Helpers;
using PuppeteerSharp.Media;

class AppDto 
{
    public string Name { get; set; }
    public string Rating { get; set; }
    public string Category { get; set; }
    public string Version { get; set; }

}

class Crawler
{
    public IBrowser Br;
    public Crawler() 
    {
        InitializeAsync().Wait();
    } 

    private async Task InitializeAsync()
    {
            Br = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = false,
                //CHANGE EXEC PATH IF NECESSARY
                ExecutablePath = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
            }).ConfigureAwait(false);
    }

    public async Task<List<string>> GetAppsFromSearch(string request, IPage page) 
    {
        // Get apps from the search result
        var appIds = new List<string>();
        string url = $"https://play.google.com/store/search?q={request}&c=apps";
            
        await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
        await page.GoToAsync(url);
        var appHrefElements = await page.QuerySelectorAllAsync("a.Si6A0c.Gy4nib");
        foreach (var elem in appHrefElements)
        {
            var href = await elem.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
            var appId = href.Split('=')[1]; 
            appIds.Add(appId);
        }
        return appIds;
    }
    public async Task GetAppIds()
    {
        // Get apps from Google play iterating throught English alphabet and numbers
        var appIds = new HashSet<string>();
        var page = await Br.NewPageAsync();
        var tasks = new List<Task<List<string>>>();
        for (int i = 0; i < 500; i++) 
        {
            var currLetter = ((char)('a' + i)).ToString();
            var currString = i > 26 ? i.ToString() : currLetter;
            appIds.UnionWith((await this.GetAppsFromSearch(currString, page)).ToHashSet());
            if (appIds.Count >= 1000) break;
            
        }
        var filePath = "apps.txt";
        await File.WriteAllLinesAsync(filePath, appIds.ToList());
        await page.CloseAsync();
    }

    public async Task<Tuple<string,AppDto>> GetAppData(string id, IPage page) {
        const string url = "https://play.google.com/store/apps/details?id=";
        var appUrl = url + id;
        await page.GoToAsync(url + id);
        await page.EvaluateExpressionAsync("() => window.stop()");

        var nameElem = await page.QuerySelectorAsync("h1.Fd93Bb");
        var appName = nameElem == null ? "" : await nameElem.EvaluateFunctionAsync<string>("element => element.textContent");

        var starsElem = await page.QuerySelectorAsync("div.TT9eCd");
        var stars = starsElem == null ? "" : await starsElem.EvaluateFunctionAsync<string>("element => element.textContent");

        var categotyElem = await page.QuerySelectorAsync("a.WpHeLc.VfPpkd-mRLv6.VfPpkd-RLmnJb");
        var category = categotyElem == null ? "" : await categotyElem.EvaluateFunctionAsync<string>("el => el.getAttribute('aria-label')");

        try {
            await page.FocusAsync("button.VfPpkd-Bz112c-LgbsSe.yHy1rc.eT1oJ.QDwDD.mN1ivc.VxpoF");
            await page.Keyboard.TypeAsync("\n");
        }
        catch {
            //await System.Console.Out.WriteLineAsync(id);
        }
        
        var versionElement = await page.QuerySelectorAsync("div.reAt0");
        var version = versionElement == null ? "" : await versionElement.EvaluateFunctionAsync<string>("element => element.textContent");
        //await page.CloseAsync();
        var appDto = new AppDto
        {
                Name = appName,
                Category = category,
                Rating = stars,
                Version = version
        };
        return Tuple.Create(id, appDto);
    }
}
class Program
{
    static async Task Main(string[] args)
    {   
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var cr = new Crawler();

        //Obtain the data from Google Play
        //await cr.GetAppIds();

        string filePath = "apps.txt";
        string[] appIds = File.ReadAllLines(filePath);

        int tabNum = 100;
        var batchSize = appIds.Length / tabNum;

        var pageList = new List<IPage>();
        for (int i = 0; i < tabNum; i++) {
            var page = await cr.Br.NewPageAsync();
            pageList.Add(page);
            await pageList[i].SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
            pageList[i].DefaultNavigationTimeout = 100000;
        }
        
        var apps = new List<Tuple<string,AppDto>>();
        var tasks = new List<Task<Tuple<string,AppDto>>>();
        for (int i = 0; i < batchSize; i++)
        {
            var batch = appIds.Skip(i * tabNum).Take(tabNum).ToList();
            for(int j = 0; j < tabNum; j++) 
            {     
                tasks.Add(cr.GetAppData(batch[j], pageList[j]));
            }
            apps.AddRange((await Task.WhenAll(tasks)).Select(u=>u));
        }
        foreach (var el in apps) {
            System.Console.WriteLine(el.Item2.Name);
        }
        
        Parallel.ForEach(apps, app => {
            var filename = "results/" + app.Item1 + ".json";
            var json = JsonSerializer.Serialize(app.Item2);
            File.WriteAllText(filename, json);
        });

        var elapsedMs = watch.ElapsedMilliseconds;
        System.Console.WriteLine(elapsedMs);
        watch.Stop();
    }
}

