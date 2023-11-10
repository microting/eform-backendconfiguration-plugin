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

using System;
using System.Collections.Generic;
using System.Linq;
using BackendConfiguration.Pn.Infrastructure.Models.Report;
using BackendConfiguration.Pn.Services.BackendConfigurationReportService;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;

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
    private readonly IBackendConfigurationReportService _reportService;

    public ReportController(IWordService wordService, IBackendConfigurationReportService reportService)
    {
        _wordService = wordService;
        _reportService = reportService;
    }

    [HttpGet]
    [Route("word")]
    public async Task GetWordReport(int propertyId, int areaId, int year)
    {
        var result = await _wordService.GenerateReport(propertyId, areaId, year).ConfigureAwait(false);
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
                await Response.Body.WriteAsync(bytes, 0, result.Message.Length).ConfigureAwait(false);
                await Response.Body.FlushAsync().ConfigureAwait(false);
            }
            else
            {
                var wordStream = result.Model;
                int bytesRead;
                Response.ContentLength = wordStream.Length;
                Response.ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

                while ((bytesRead = await wordStream.ReadAsync(buffer, 0, buffer.Length)) > 0 &&
                       !HttpContext.RequestAborted.IsCancellationRequested)
                {
                    await Response.Body.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                    await Response.Body.FlushAsync().ConfigureAwait(false);
                }
            }
        });
    }

    [HttpPost]
    [Route("reports")]
    public async Task<OperationDataResult<List<OldReportEformModel>>> GenerateReport([FromBody]GenerateReportModel requestModel)
    {
        return await _reportService.GenerateReport(requestModel, false);
    }

    [HttpPost]
    [Route("new-reports")]
    public async Task<OperationDataResult<List<ReportEformModel>>> GenerateNewReport([FromBody] GenerateReportModel requestModel)
    {
        return await _reportService.GenerateReportV2(requestModel, false);
    }

    /// <summary>Download records export word</summary>
    /// <param name="dateFrom">The date from.</param>
    /// <param name="dateTo">The date to.</param>
    /// <param name="tagIds">The tag ids.</param>
    /// <param name="type">docx or xlsx</param>
    [HttpGet]
    [Route("reports/file")]

    [ProducesResponseType(typeof(string), 400)]
    public async Task GenerateReportFile([FromQuery]DateTime dateFrom, [FromQuery]DateTime dateTo, [FromQuery]string tagIds, [FromQuery]string type, [FromQuery]bool version2 = false)
    {
        var requestModel = new GenerateReportModel();
        var tags = tagIds?.Split(",").ToList();
        if (tags != null)
        {
            foreach (string tag in tags)
            {
                requestModel.TagIds.Add(int.Parse(tag));
            }
        }

        requestModel.DateFrom = dateFrom;
        requestModel.DateTo = dateTo;
        requestModel.Type = type;
        var result = await _reportService.GenerateReportFile(requestModel, version2);
        const int bufferSize = 4086;
        byte[] buffer = new byte[bufferSize];
        Response.OnStarting(async () =>
        {
            if (!result.Success)
            {
                Response.ContentLength = result.Message.Length;
                Response.ContentType = "text/plain";
                Response.StatusCode = 400;
                byte[] bytes = Encoding.UTF8.GetBytes(result.Message);
                await Response.Body.WriteAsync(bytes, 0, result.Message.Length);
                await Response.Body.FlushAsync();
            }
            else
            {
                await using var wordStream = result.Model;
                int bytesRead;
                Response.ContentLength = wordStream.Length;
                Response.ContentType = requestModel.Type switch
                {
                    "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "pdf" => "application/pdf",
                    _ => "text/plain"
                };

                while ((bytesRead = await wordStream.ReadAsync(buffer, 0, buffer.Length)) > 0 &&
                       !HttpContext.RequestAborted.IsCancellationRequested)
                {
                    await Response.Body.WriteAsync(buffer, 0, bytesRead);
                    await Response.Body.FlushAsync();
                }
            }
        });
    }

    [HttpPut]
    [Route("cases")]
    public async Task<IActionResult> Update([FromBody] ReplyRequest model)
    {
        return Ok(await _reportService.Update(model));
    }
}