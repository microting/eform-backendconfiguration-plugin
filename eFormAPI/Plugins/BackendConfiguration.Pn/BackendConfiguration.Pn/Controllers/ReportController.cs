/*
The MIT License (MIT)
Copyright (c) 2007 - 2022 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Controllers;

using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.WordService;

[Authorize]
[Route("api/backend-configuration-pn/report")]
public class ReportController : Controller
{
    private readonly IWordService _wordService;

    public ReportController(IWordService wordService)
    {
        _wordService = wordService;
    }

    [HttpGet]
    [Route("word")]
    public async Task GetWordReport(int propertyId, int areaId, int year)
    {
        var result = await _wordService.GenerateReport(propertyId, areaId, year);
        const int bufferSize = 4086;
        var buffer = new byte[bufferSize];
        Response.OnStarting(async () =>
        {
            if (!result.Success)
            {
                Response.ContentLength = result.Message.Length;
                Response.ContentType = "text/plain";
                Response.StatusCode = 400;
                var bytes = Encoding.UTF8.GetBytes(result.Message);
                await Response.Body.WriteAsync(bytes, 0, result.Message.Length);
                await Response.Body.FlushAsync();
            }
            else
            {
                await using var wordStream = result.Model;
                int bytesRead;
                Response.ContentLength = wordStream.Length;
                Response.ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

                while ((bytesRead = wordStream.Read(buffer, 0, buffer.Length)) > 0 &&
                       !HttpContext.RequestAborted.IsCancellationRequested)
                {
                    await Response.Body.WriteAsync(buffer, 0, bytesRead);
                    await Response.Body.FlushAsync();
                }
            }
        });
    }
}