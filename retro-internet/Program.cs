using HtmlAgilityPack;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Device.Gpio;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Caching.Memory;
using retro_internet;
using System.Text.Json;

bool _greenLEDOn = false;
bool _redLEDOn = false;
int _errorPin = 17;
int _successPin = 27;
var _imageCache = new MemoryCache(new MemoryCacheOptions() { });
var _htmlCache = new MemoryCache(new MemoryCacheOptions() { });
var _badResultsCache = new MemoryCache(new MemoryCacheOptions() { });

///
/// Main method that accepts a url path, pulls the mapped snapshot from Archive.org, strips out the header and footer content, rewrites the urls and image src's to look
/// like they're coming from the original domain. Then returns all of this. When the user clicks a link on the rendered html, we do the lookups to match to the snapshot
/// Support image caching, html caching, and blinking Raspberry PI GPIO LEDs (if we're running on a pi)
///
async Task<IResult> ProxyWaybackPage(HttpContext context, string path, HttpClient httpClient, Dictionary<string, (string Url, bool DefaultRouteDefined)> domainMappings)
{
    path = path.ToLower();

    if (path.EndsWith(".txt"))
    {
        return Results.BadRequest();
    }

    bool skipMapping = false;

    if (path.Contains("web-static"))
    {
        path = path.TrimStart('/');
        skipMapping = true;

        if (!path.StartsWith("http"))
        {
            path = $"http://{path}";
        }
    }

    var sw = new Stopwatch();
    sw.Start();

    WriteDebugMessage($"Proxy hit for path - {path}, host - {context.Request.Host.Host}", sw);

    var ledCancelletionTokenSource = new CancellationTokenSource();
    var ledCancellationToken = ledCancelletionTokenSource.Token;

    using var gpioController = new GPIOAbstraction().Controller;

    try
    {
        await BlinkLedAsync(gpioController, Speed.Medium, ledCancellationToken);

        var devHost = "http://localhost:5271";

#if DEBUG
        //remove local debugging host
        if (path.ToLower().StartsWith(devHost))
        {
            path = path.Remove(0, devHost.Length);
        }
        if (path.ToLower().StartsWith(devHost.Substring(devHost.LastIndexOf('/'), devHost.Length - devHost.LastIndexOf('/'))))
        {
            path = path.Remove(0, devHost.Length);
        }
#endif

        //if we've already had a failure on this path, don't try again
        if (_badResultsCache != null && _badResultsCache.TryGetValue(path, out bool result))
        {
            if (!result)
            {
                return Results.BadRequest();
            }
        }

        if (IsImageUrl(path) && _imageCache.TryGetValue(path, out string imageArchiveUrl))
        {
            WriteDebugMessage($"Found image cache path for - {path}, archive url - {imageArchiveUrl}", sw);

            var imgResponse = await httpClient.GetAsync(imageArchiveUrl);
            if (!imgResponse.IsSuccessStatusCode)
            {
                // Example function call - assuming you have this implemented for indicating issues
                await BlinkLedAsync(gpioController, Speed.Fast, ledCancellationToken);
                await Task.Delay(500);

                // Log the bad result for this URL
                _badResultsCache.Set(path, false);

                return Results.BadRequest(); // Send a 400 Bad Request response
            }
            else
            {
                WriteDebugMessage($"Loaded image cache for - {path}, archive url - {imageArchiveUrl}", sw);

                var imageData = await imgResponse.Content.ReadAsByteArrayAsync();

                // Return the image data with the correct content type
                // Assuming the content type can be derived from the response or a predetermined value can be used
                var contentType = imgResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
                return Results.File(imageData, contentType);
            }
        }
        else
        {
            WriteDebugMessage($"FAILED to load image cache for - {path}", sw);
        }

        var url = string.Empty;

        //TODO: extract the root domain here to find mapping

        var rootPath = GetDomainFromUrl(path);

        if (!skipMapping)
        {
            if (domainMappings.TryGetValue(rootPath, out var foundMapping))
            {
                if (foundMapping.Url == null)
                {
                    WriteDebugMessage($"Could not find mapping for path  {path}", sw);
                    await BlinkLedAsync(gpioController, Speed.Fast, ledCancellationToken, true);

                    return Results.NotFound("Domain mapping was null.");
                }

                url = $"{foundMapping.Url}{path}";
            }
            else
            {
                WriteDebugMessage($"Could not find mapping for path {path}", sw);
                await BlinkLedAsync(gpioController, Speed.Fast, ledCancellationToken, true);

                return Results.NotFound($"Could not find mapping for path {path}");
            }
        }
        else
        {
            url = path;
        }

        var cacheKey = $"{context.Request.Host.Host}.{path}";

        if (_htmlCache.TryGetValue(cacheKey, out string html) && !string.IsNullOrEmpty(html))
        {
            WriteDebugMessage($"Cache hit for URL - {url} / PATH - {path}, LOADING...", sw);
            return new HtmlString(html);
        }
        else
        {
            WriteDebugMessage($"Cache MISS for URL - {url} / PATH - {path}", sw);
        }

        WriteDebugMessage($"URL - {url}, LOADING...", sw);

        var response = await httpClient.GetAsync(url);
        var content = string.Empty;

        if (!response.IsSuccessStatusCode)
        {
            await BlinkLedAsync(gpioController, Speed.Fast, ledCancellationToken);
            await Task.Delay(500);

            _badResultsCache.Set(path, false);

            return Results.BadRequest();
        }
        else
        {
            content = await response.Content.ReadAsStringAsync();
        }

        WriteDebugMessage($"URL - {url}, DONE LOADING, REWRITING HTML", sw);

        await BlinkLedAsync(gpioController, Speed.Fast, ledCancellationToken);

        var wbCommentPattern = @"<!-- BEGIN WAYBACK TOOLBAR INSERT -->.*?<!-- END WAYBACK TOOLBAR INSERT -->";
        var cleanedHtmlContent = Regex.Replace(content, wbCommentPattern, string.Empty, RegexOptions.Singleline);

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(cleanedHtmlContent);

        var nodesWithUrls = htmlDoc.DocumentNode.SelectNodes("//*[@href or @src]");

        if (nodesWithUrls != null)
        {
            foreach (var node in nodesWithUrls)
            {
                foreach (var attribute in node.Attributes.Where(a => a.Name == "href" || a.Name == "src"))
                {
                    var srcAttr = node.Attributes["src"];

                    if (srcAttr != null && IsImageUrl(attribute.Value))
                    {
                        var originalValue = attribute.Value;
                        attribute.Value = ExtractUrl(srcAttr.Value);
                        _imageCache.Set(attribute.Value, originalValue);
#if DEBUG
                        attribute.Value = $"{devHost}/{ExtractUrl(srcAttr.Value)}";
#endif
                    }
                    else
                    {


                        attribute.Value = ExtractUrl(attribute.Value);
#if DEBUG
                        attribute.Value = $"{devHost}/{ExtractUrl(attribute.Value)}";
#endif
                    }
                }
            }
        }

        WriteDebugMessage($"URL - {url}, HTML REWRITE DONE, RETURNING DATA", sw);

        var returnString = new HtmlString(htmlDoc.DocumentNode.OuterHtml);

        WriteDebugMessage($"URL - {url}, SAVING CACHE FOR - {context.Request.Host.Host}.{path}", sw);

        _htmlCache.CreateEntry(cacheKey);
        _htmlCache.Set(cacheKey, returnString);

        return returnString;
    }
    catch (Exception ex)
    {
        await BlinkLedAsync(gpioController, Speed.Fast, ledCancellationToken, true);
        await Task.Delay(1000);

        WriteDebugMessage($"ERROR for path - {path}, Exception - {ex.Message}", sw);

        ledCancelletionTokenSource.Cancel();

        return new HtmlString($"<html><body><h1>Error occurred while trying to fetch 90's internet:</h1></br></br>{WebUtility.HtmlEncode(ex.Message)}</body></html>");
    }
    finally
    {
        TurnOffBothLedsAsync(gpioController);

        try
        {
            if (gpioController != null)
            {
                gpioController.ClosePin(_errorPin);
                gpioController.ClosePin(_successPin);
            }
        }
        catch (Exception ex)
        {
            WriteDebugMessage("ERROR - Could not close pins", sw);
        }

        sw.Stop();
    }
}

static bool IsRunningOnPi()
{
    return RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)
        && File.Exists("/proc/cpuinfo")
        && File.ReadAllText("/proc/cpuinfo").Contains("Raspberry Pi");
}

static string GetDomainFromUrl(string url)
{
    // Add protocol if missing. This helps Uri parse the URL correctly in all cases.
    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
    {
        url = "http://" + url;
    }

    Uri uri = new Uri(url);
    string domain = uri.Host;

    // If the domain has subdomains, remove them.
    if (domain.Split('.').Length > 2)
    {
        int lastIndex = domain.LastIndexOf(".");
        int index = domain.LastIndexOf(".", lastIndex - 1);
        domain = domain.Substring(index + 1);
    }

    return domain.ToLower();
}

static string ExtractUrl(string archiveUrl)
{
    var lastIndexOfHttp = archiveUrl.ToLower().LastIndexOf("http");

    if (lastIndexOfHttp < 0)
    {
        //return string.Empty;
        return archiveUrl;
    }

    lastIndexOfHttp = lastIndexOfHttp + 4; //cover http

    if (archiveUrl[lastIndexOfHttp + 1].ToString().ToLower() == "s")
    {
        lastIndexOfHttp++;
    }

    lastIndexOfHttp = lastIndexOfHttp + 3; // colon and two slashes

    return archiveUrl.Substring(lastIndexOfHttp, archiveUrl.Length - lastIndexOfHttp);
}

bool IsImageUrl(string url)
{
    return url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
           url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
           url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
           url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
}

async Task BlinkLedAsync(GpioController controller, Speed blinkSpeed, CancellationToken cancellationToken, bool isError = false)
{
    try
    {

        if (controller != null && IsRunningOnPi())
        {
            bool ledState = isError ? _redLEDOn : _greenLEDOn;
            var pin = isError ? _errorPin : _successPin;

            var blinkSpeedMilliseconds = 100;

            switch (blinkSpeed)
            {
                case Speed.Slow:
                    blinkSpeedMilliseconds = 1000;
                    break;
                case Speed.Medium:
                    blinkSpeedMilliseconds = 500;
                    break;
                case Speed.Fast:
                    blinkSpeedMilliseconds = 100;
                    break;
                default:
                    break;
            }

            var sw = new Stopwatch();
            sw.Start();

            while (sw.ElapsedMilliseconds < blinkSpeedMilliseconds) // Replace with a condition to stop blinking as needed
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    //WriteDebugMessage($"toggling pin {pin} to {ledState}", null);
                    controller.Write(pin, ledState ? PinValue.High : PinValue.Low);
                    ledState = !ledState;

                    if (isError)
                    {
                        _redLEDOn = ledState;
                    }
                    else
                    {
                        _greenLEDOn = ledState;
                    }

                    // Wait for the interval asynchronously
                    await Task.Delay(blinkSpeedMilliseconds / 100);
                }
                else
                {
                    return;
                }
            }

            sw.Stop();
        }
    }
    catch { }
}

void TurnOffBothLedsAsync(GpioController controller)
{
    try
    {
        if (controller != null && IsRunningOnPi())
        {
            controller.Write(_errorPin, PinValue.Low);
            controller.Write(_successPin, PinValue.Low);
        }
    }
    catch { }
}

static void WriteDebugMessage(string stringToPrint, Stopwatch stopwatch = null)
{
    if (stopwatch != null)
    {
        var ms = stopwatch == null ? -1 : stopwatch?.ElapsedMilliseconds;
        stringToPrint = $"{DateTime.Now.ToString()} - {stringToPrint}. Elapsed ms = {ms}";
    }

    Console.WriteLine(stringToPrint);
    Debug.WriteLine(stringToPrint);
}





//
//
//Startup code
//
//

var mappingFilePath = "domainMappings.json";

var builder = WebApplication.CreateBuilder(args);

//open it up
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
        });
});

var app = builder.Build();

var httpClient = new HttpClient();

var domainMappings = GetDomainMappings(mappingFilePath);

app.UseCors("AllowAll");

foreach (var entry in domainMappings)
{
    Console.WriteLine($"Domain: {entry.Key}, Archive URL: {entry.Value.Item1}, SkipRootPage: {entry.Value.Item2}");
}

//
//Endpoints
//

app.MapGet("/admin", () => Results.File(Path.Combine(Directory.GetCurrentDirectory(), "admin.html"), "text/html"));

//admin endpoints
app.MapGet("/entries", async () =>
{
    if (!File.Exists(mappingFilePath))
    {
        return Results.NotFound();
    }

    var json = await File.ReadAllTextAsync(mappingFilePath);
    var entries = JsonSerializer.Deserialize<List<WebEntry>>(json);
    return Results.Ok(entries);
});

app.MapPost("/entries", async (WebEntry newEntry) =>
{
    List<WebEntry> entries;

    if (File.Exists(mappingFilePath))
    {
        var json = await File.ReadAllTextAsync(mappingFilePath);
        entries = JsonSerializer.Deserialize<List<WebEntry>>(json);
    }
    else
    {
        entries = new List<WebEntry>();
    }

    entries.Add(newEntry);
    var updatedJson = JsonSerializer.Serialize(entries);
    await File.WriteAllTextAsync(mappingFilePath, updatedJson);

    domainMappings = GetDomainMappings(mappingFilePath);

    return Results.Ok(newEntry);
});

app.MapPut("/entries", async (WebEntry updatedEntry) =>
{
    if (!File.Exists(mappingFilePath))
    {
        return Results.NotFound();
    }

    var json = await File.ReadAllTextAsync(mappingFilePath);
    var entries = JsonSerializer.Deserialize<List<WebEntry>>(json);

    var entry = entries.Find(e => e.Url == updatedEntry.Url);
    if (entry == null)
    {
        return Results.NotFound();
    }

    entry.ArchiveUrl = updatedEntry.ArchiveUrl;
    entry.SkipRootPage = updatedEntry.SkipRootPage;

    var updatedJson = JsonSerializer.Serialize(entries);
    await File.WriteAllTextAsync(mappingFilePath, updatedJson);

    domainMappings = GetDomainMappings(mappingFilePath);

    return Results.Ok(updatedEntry);
});

//proxy endpoints
app.MapGet("/waybackproxyredirect", async (HttpContext context, string url) =>
{
    // Decode and parse the original URL to extract the domain and path
    // Then redirect or proxy to the Wayback Machine content as before
    // This logic would be similar to your existing ProxyWaybackPage method
    return Results.Redirect($"/{url}");
});

app.MapGet("", async (HttpContext context) =>
{
    return await ProxyWaybackPage(context, string.Empty, httpClient, domainMappings);
});

app.MapGet("/{**path}", async (HttpContext context, string path) =>
{
    return await ProxyWaybackPage(context, path, httpClient, domainMappings);
});

//Run it
app.Run();

static Dictionary<string, (string, bool SkipRootPage)> GetDomainMappings(string mappingFilePath)
{
    var domainMappingsPath = Path.Combine(Directory.GetCurrentDirectory(), mappingFilePath);

    // Read the JSON file
    var json = File.ReadAllText(domainMappingsPath);

    // Parse the JSON file
    var domainMappings = JsonSerializer.Deserialize<List<WebEntry>>(json)
        .ToDictionary(item => item.Url.ToLower(), item => (item.ArchiveUrl.ToLower(), item.SkipRootPage));
    return domainMappings;
}