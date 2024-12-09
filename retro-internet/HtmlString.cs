namespace retro_internet
{
    public class HtmlString : IResult
    {
        private readonly string _htmlContent;
        public HtmlString(string htmlContent)
        {
            _htmlContent = htmlContent;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "text/html";
            await httpContext.Response.WriteAsync(_htmlContent);
        }
    }
}
