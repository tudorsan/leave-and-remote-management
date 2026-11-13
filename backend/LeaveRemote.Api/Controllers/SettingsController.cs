using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

/// <summary>Setările globale și sărbătorile legale (doar Admin).</summary>
[Route("api/settings")]
[Authorize(Roles = "Admin")]
public class SettingsController(SettingsService settings) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await settings.GetAllAsync());

    [HttpPut]
    [RequestWriteTransaction]
    public async Task<IActionResult> Update([FromBody] GlobalSettingsInput input) =>
        Respond(await settings.UpdateAsync(input));

    [HttpPut("holidays")]
    [RequestWriteTransaction]
    public async Task<IActionResult> ReplaceHolidays([FromBody] List<HolidayInput?> holidays) =>
        Respond(await settings.ReplaceHolidaysAsync(holidays));
}
